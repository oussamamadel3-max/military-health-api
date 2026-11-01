using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ClinicWorkspace;

/// <summary>
/// تجميع استشارات واستقصاءات ومرفقات المنتسب للعرض ضمن نطاق المركز والصلاحيات (FR-050).
/// </summary>
public class MemberMedicalRequestService : IMemberMedicalRequestService
{
    public const string RequestKindConsultation = "Consultation";
    public const string RequestKindInvestigation = "Investigation";
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";
    public const string EmptyMessageAr =
        "لا توجد استشارات أو استقصاءات مسجّلة لهذا المنتسب في المركز الحالي.";

    private const string AttachmentDownloadPathFormat = "/api/Attachments/download/{0}";

    private readonly IConsultationRepository _consultations;
    private readonly IInvestigationRepository _investigations;
    private readonly IAttachmentRepository _attachments;
    private readonly IMemberRepository _members;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUserRepository _users;
    private readonly IPermissionGate _permissionGate;

    public MemberMedicalRequestService(
        IConsultationRepository consultations,
        IInvestigationRepository investigations,
        IAttachmentRepository attachments,
        IMemberRepository members,
        IReferenceDataRepository referenceData,
        IUserRepository users,
        IPermissionGate permissionGate)
    {
        _consultations = consultations;
        _investigations = investigations;
        _attachments = attachments;
        _members = members;
        _referenceData = referenceData;
        _users = users;
        _permissionGate = permissionGate;
    }

    /// <inheritdoc />
    public async Task<MemberMedicalRequestsDto> GetForMemberAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidUserDataException("معرّف المنتسب مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
            clientCenterId,
            cancellationToken);

        return await LoadForMemberInCenterAsync(memberId, centerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberMedicalRequestsDto> GetForMemberInAuthorizedCenterAsync(
        Guid memberId,
        Guid authorizedCenterId,
        CancellationToken cancellationToken = default)
    {
        if (memberId == Guid.Empty)
            throw new InvalidUserDataException("معرّف المنتسب مطلوب.");

        if (authorizedCenterId == Guid.Empty)
            throw new InvalidUserDataException("معرّف المركز مطلوب.");

        return await LoadForMemberInCenterAsync(memberId, authorizedCenterId, cancellationToken);
    }

    #region Private Helpers

    private async Task<MemberMedicalRequestsDto> LoadForMemberInCenterAsync(
        Guid memberId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var member = await _members.GetByIdInCenterAsync(
            memberId,
            centerId,
            asNoTracking: true,
            cancellationToken);
        if (member is null)
            throw new MemberNotFoundException();

        var includeAttachments = _permissionGate.Has(MilitaryHealthPermissions.Medical.AttachmentsRead);

        var consultations = await _consultations.Query(asNoTracking: true)
            .Where(c => c.MemberId == memberId && c.CenterId == centerId)
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(cancellationToken);

        var investigations = await _investigations.Query(asNoTracking: true)
            .Where(i => i.MemberId == memberId && i.CenterId == centerId)
            .OrderByDescending(i => i.RequestedAt)
            .ToListAsync(cancellationToken);

        if (consultations.Count == 0 && investigations.Count == 0)
        {
            return new MemberMedicalRequestsDto
            {
                MemberId = memberId,
                CenterId = centerId,
                IsEmpty = true,
                EmptyMessageAr = EmptyMessageAr,
                TotalCount = 0,
                PendingCount = 0,
                CompletedCount = 0,
                AttachmentsIncluded = includeAttachments,
                Items = Array.Empty<MemberMedicalRequestItemDto>()
            };
        }

        var clinicIds = consultations.Select(c => c.ClinicId)
            .Concat(investigations.Select(i => i.ClinicId))
            .Distinct()
            .ToList();

        var clinics = await _referenceData.Clinics(asNoTracking: true)
            .Where(c => clinicIds.Contains(c.ClinicId))
            .Select(c => new { c.ClinicId, c.NameAr, c.Code })
            .ToListAsync(cancellationToken);
        var clinicMap = clinics.ToDictionary(c => c.ClinicId);

        var consultationTypeIds = consultations.Select(c => c.ConsultationTypeId).Distinct().ToList();
        var consultationTypes = await _referenceData.ConsultationTypes(asNoTracking: true)
            .Where(t => consultationTypeIds.Contains(t.ConsultationTypeId))
            .Select(t => new { t.ConsultationTypeId, t.NameAr })
            .ToListAsync(cancellationToken);
        var consultationTypeMap = consultationTypes.ToDictionary(t => t.ConsultationTypeId, t => t.NameAr);

        var investigationTypeIds = investigations.Select(i => i.InvestigationTypeId).Distinct().ToList();
        var investigationTypes = await _referenceData.InvestigationTypes(asNoTracking: true)
            .Where(t => investigationTypeIds.Contains(t.InvestigationTypeId))
            .Select(t => new { t.InvestigationTypeId, t.NameAr })
            .ToListAsync(cancellationToken);
        var investigationTypeMap = investigationTypes.ToDictionary(t => t.InvestigationTypeId, t => t.NameAr);

        var userIds = consultations.Select(c => c.RequestedByUserId)
            .Concat(investigations.Select(i => i.RequestedByUserId))
            .Concat(consultations
                .Where(c => !string.IsNullOrEmpty(c.ConsultantUserId))
                .Select(c => c.ConsultantUserId!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var displayNames = userIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _users.GetDisplayNamesByIdsAsync(userIds, cancellationToken);

        Dictionary<Guid, List<MedicalRequestAttachmentDto>> consultationAttachments;
        Dictionary<Guid, List<MedicalRequestAttachmentDto>> investigationAttachments;
        if (includeAttachments)
        {
            var links = await _attachments.ListFilesForMedicalRequestsAsync(
                memberId,
                centerId,
                consultations.Select(c => c.ConsultationId),
                investigations.Select(i => i.InvestigationId),
                cancellationToken);

            consultationAttachments = links
                .Where(l => l.ConsultationId.HasValue)
                .GroupBy(l => l.ConsultationId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(MapAttachment).ToList());

            investigationAttachments = links
                .Where(l => l.InvestigationId.HasValue)
                .GroupBy(l => l.InvestigationId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(MapAttachment).ToList());
        }
        else
        {
            consultationAttachments = new Dictionary<Guid, List<MedicalRequestAttachmentDto>>();
            investigationAttachments = new Dictionary<Guid, List<MedicalRequestAttachmentDto>>();
        }

        var items = new List<MemberMedicalRequestItemDto>(consultations.Count + investigations.Count);

        foreach (var row in consultations)
        {
            clinicMap.TryGetValue(row.ClinicId, out var clinic);
            consultationTypeMap.TryGetValue(row.ConsultationTypeId, out var typeName);
            displayNames.TryGetValue(row.RequestedByUserId, out var requestedByName);
            consultationAttachments.TryGetValue(row.ConsultationId, out var files);

            items.Add(new MemberMedicalRequestItemDto
            {
                RequestKind = RequestKindConsultation,
                RequestId = row.ConsultationId,
                ClinicId = row.ClinicId,
                ClinicNameAr = clinic?.NameAr,
                SpecialtyNameAr = clinic?.NameAr,
                SpecialtyCode = clinic?.Code,
                TypeId = row.ConsultationTypeId,
                TypeNameAr = typeName,
                RequestReason = row.ReferralReason,
                Status = row.Status,
                IsPending = IsPendingStatus(row.Status),
                ResultText = row.ResultText,
                RequestedByUserId = row.RequestedByUserId,
                RequestedByUserName = requestedByName,
                RequestedAt = row.RequestedAt,
                ResultedAt = row.ResultedAt,
                ConsultantUserId = row.ConsultantUserId,
                ConsultantName = row.ConsultantName,
                Attachments = includeAttachments
                    ? (IReadOnlyList<MedicalRequestAttachmentDto>)(files ?? new List<MedicalRequestAttachmentDto>())
                    : Array.Empty<MedicalRequestAttachmentDto>()
            });
        }

        foreach (var row in investigations)
        {
            clinicMap.TryGetValue(row.ClinicId, out var clinic);
            investigationTypeMap.TryGetValue(row.InvestigationTypeId, out var typeName);
            displayNames.TryGetValue(row.RequestedByUserId, out var requestedByName);
            investigationAttachments.TryGetValue(row.InvestigationId, out var files);

            items.Add(new MemberMedicalRequestItemDto
            {
                RequestKind = RequestKindInvestigation,
                RequestId = row.InvestigationId,
                ClinicId = row.ClinicId,
                ClinicNameAr = clinic?.NameAr,
                SpecialtyNameAr = clinic?.NameAr,
                SpecialtyCode = clinic?.Code,
                TypeId = row.InvestigationTypeId,
                TypeNameAr = typeName,
                RequestReason = row.RequestReason,
                TestName = row.TestName,
                Status = row.Status,
                IsPending = IsPendingStatus(row.Status),
                ResultText = row.ResultText,
                RequestedByUserId = row.RequestedByUserId,
                RequestedByUserName = requestedByName,
                RequestedAt = row.RequestedAt,
                ResultedAt = row.ResultedAt,
                DeferredActionId = row.DeferredActionId,
                Attachments = includeAttachments
                    ? (IReadOnlyList<MedicalRequestAttachmentDto>)(files ?? new List<MedicalRequestAttachmentDto>())
                    : Array.Empty<MedicalRequestAttachmentDto>()
            });
        }

        items.Sort((a, b) => b.RequestedAt.CompareTo(a.RequestedAt));

        var pendingCount = items.Count(i => i.IsPending);
        var completedCount = items.Count(i =>
            string.Equals(i.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase));

        return new MemberMedicalRequestsDto
        {
            MemberId = memberId,
            CenterId = centerId,
            IsEmpty = false,
            EmptyMessageAr = null,
            TotalCount = items.Count,
            PendingCount = pendingCount,
            CompletedCount = completedCount,
            AttachmentsIncluded = includeAttachments,
            Items = items
        };
    }

    private static bool IsPendingStatus(string status)
        => string.Equals(status, StatusPending, StringComparison.OrdinalIgnoreCase);

    private static MedicalRequestAttachmentDto MapAttachment(MedicalRequestFileLink link)
    {
        var file = link.File;
        return new MedicalRequestAttachmentDto
        {
            FileAssetId = file.FileAssetId,
            FileName = file.OriginalName,
            FileExtension = file.Extension,
            ContentType = file.MimeType,
            FileSizeBytes = file.SizeBytes,
            UploadedAt = file.CreatedAt,
            DownloadPath = string.Format(AttachmentDownloadPathFormat, file.FileAssetId)
        };
    }

    #endregion
}
