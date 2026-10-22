using System.Net;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Members;

/// <summary>
/// خدمات ملف المنتسب المحلية: تسجيل (MH-P3-020) وتعديل (MH-P3-050 / FR-024) وقائمة/تفاصيل مع عزل المركز النشط وتدقيق صريح.
/// </summary>
public class MemberService : IMemberService
{
    private readonly IMemberRepository _members;
    private readonly IMedicalCenterRepository _centers;
    private readonly IReferenceDataRepository _references;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;

    public MemberService(
        IMemberRepository members,
        IMedicalCenterRepository centers,
        IReferenceDataRepository references,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser)
    {
        _members = members;
        _centers = centers;
        _references = references;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<MemberListItemDto>> ListAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        MemberFieldValidation.ValidateListDateFilter(query.Date, query.FromDate, query.ToDate);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        if (pageSize > 500) pageSize = 500;

        var q = _members.Query().Where(m => m.CurrentMedicalCenterId == centerId);

        if (query.FileNumber.HasValue)
            q = q.Where(m => m.FileNumber == query.FileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.RecruitmentNumber))
        {
            var term = query.RecruitmentNumber.Trim().ToLower();
            q = q.Where(m => m.RecruitmentNumber.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var term = query.FullName.Trim().ToLower();
            q = q.Where(m => m.FullName.ToLower().Contains(term));
        }

        q = ApplyCreatedAtFilter(q, query.Date, query.FromDate, query.ToDate);
        q = q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.FileNumber);

        var totalCount = await q.CountAsync(cancellationToken);
        var page = await q
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var categories = await LoadBmiCategoriesAsync(cancellationToken);
        var items = page.Select(m =>
        {
            var dto = _mapper.Map<MemberListItemDto>(m);
            var bmi = m.Bmi ?? MemberBmi.Calculate(m.HeightCm, m.WeightKg);
            dto.Bmi = bmi;
            dto.BmiCategoryNameAr = MemberBmi.Classify(bmi, categories);
            return dto;
        }).ToList();

        return new PaginatedList<MemberListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<MemberDetailsDto> GetByIdAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();

        return await ToDetailsAsync(member, cancellationToken);
    }

    /// <summary>
    /// يسجّل منتسباً في المركز النشط مع توليد FileNumber/QueueNumber داخل معاملة واحدة،
    /// وحساب BMI في الاستجابة، وتحويل سباق تكرار رقم التجنيد إلى استثناء أعمال.
    /// </summary>
    public async Task<MemberDetailsDto> CreateAsync(
        CreateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.CreateUpdate,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeRules = await LoadActiveValidationRulesAsync(cancellationToken);
        MemberFieldValidation.ValidateCreateOrUpdate(request, today, activeRules);

        await EnsureReferenceAsync(request.MaritalStatusId, request.RecruitmentCenterId, request.BloodTypeId, cancellationToken);

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        var recruitmentNumber = request.RecruitmentNumber.Trim();
        if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, cancellationToken: cancellationToken))
            throw new MemberRecruitmentNumberExistsException(recruitmentNumber);

        await using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var (fileNumber, queueNumber) = await _members.AllocateFileAndQueueNumbersAsync(
                centerId, today, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var memberId = Guid.NewGuid();
            var member = new Member
            {
                MemberId = memberId,
                FileNumber = fileNumber,
                RecruitmentNumber = recruitmentNumber,
                FullName = request.FullName.Trim(),
                MotherName = request.MotherName.Trim(),
                BirthDate = request.BirthDate,
                MaritalStatusId = request.MaritalStatusId,
                Occupation = request.Occupation.Trim(),
                RecruitmentCenterId = request.RecruitmentCenterId,
                BloodTypeId = request.BloodTypeId,
                BloodSugarMgDl = request.BloodSugarMgDl,
                HeightCm = request.HeightCm,
                WeightKg = request.WeightKg,
                BloodPressure = TrimOrNull(request.BloodPressure),
                PulseBpm = request.PulseBpm,
                HasTattoo = request.HasTattoo,
                DistinguishingMarks = TrimOrNull(request.DistinguishingMarks),
                CurrentMedicalCenterId = centerId,
                QueueMedicalCenterId = centerId,
                QueueDate = today,
                QueueNumber = queueNumber,
                ExportStatus = "UNEXPORTED",
                DataRevision = 1,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _members.Add(member);
            _members.AddIdentity(new MemberCenterIdentity
            {
                MemberCenterIdentityId = Guid.NewGuid(),
                MemberId = memberId,
                MedicalCenterId = centerId,
                CenterMemberId = fileNumber.ToString(),
                ValidFrom = now,
                ValidTo = null,
                AssignedByUserId = userId,
                ClosedByUserId = null
            });

            RecordAudit(
                userId,
                centerId,
                "MemberCreate",
                "SUCCESS",
                memberId.ToString(),
                "تم إنشاء ملف المنتسب",
                oldValues: null,
                newValues: JsonSerializer.Serialize(new
                {
                    member.FileNumber,
                    member.QueueNumber,
                    member.RecruitmentNumber,
                    member.FullName,
                    member.CurrentMedicalCenterId
                }));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var created = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
                ?? throw new MemberNotFoundException();
            return await ToDetailsAsync(created, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            // سباق TOCTOU على القيد الفريد لرقم التجنيد بعد الفحص المسبق.
            if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, cancellationToken: cancellationToken))
                throw new MemberRecruitmentNumberExistsException(recruitmentNumber);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// يعدّل بيانات المنتسب الشخصية والطبية ضمن المركز النشط (FR-024)، مع ثبات الأرقام/المركز وإعادة حساب BMI عبر العمود المحسوب.
    /// </summary>
    /// <remarks>
    /// TODO(FR-024 A5): تقييد التعديل عند وجود قرار نهائي معتمد معلّق حتى اعتماد سياسة الجهة / مسار القرار النهائي (T-07).
    /// السلوك المؤقت المعتمد: السماح بالتعديل مع تسجيل Audit بالقيم السابقة والجديدة دون قفل.
    /// </remarks>
    public async Task<MemberDetailsDto> UpdateAsync(
        Guid memberId,
        UpdateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        // TODO(FR-024 A5): لا يُفحص FinalDecision هنا — السماح + Audit حتى سياسة الجهة / T-07.
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.CreateUpdate,
            clientCenterId,
            cancellationToken);

        var userId = RequireUser();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeRules = await LoadActiveValidationRulesAsync(cancellationToken);
        MemberFieldValidation.ValidateCreateOrUpdate(request, today, activeRules);

        await EnsureReferenceAsync(request.MaritalStatusId, request.RecruitmentCenterId, request.BloodTypeId, cancellationToken);

        var member = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: false, cancellationToken)
            ?? throw new MemberNotFoundException();

        var recruitmentNumber = request.RecruitmentNumber.Trim();
        if (await _members.RecruitmentNumberExistsAsync(recruitmentNumber, excludingMemberId: memberId, cancellationToken))
            throw new MemberRecruitmentNumberExistsException(recruitmentNumber);

        var oldSnapshot = JsonSerializer.Serialize(new
        {
            member.FullName,
            member.MotherName,
            member.BirthDate,
            member.MaritalStatusId,
            member.Occupation,
            member.RecruitmentNumber,
            member.RecruitmentCenterId,
            member.BloodTypeId,
            member.BloodSugarMgDl,
            member.HeightCm,
            member.WeightKg,
            member.BloodPressure,
            member.PulseBpm,
            member.HasTattoo,
            member.DistinguishingMarks,
            member.FileNumber,
            member.CurrentMedicalCenterId,
            member.DataRevision
        });

        var preservedFile = member.FileNumber;
        var preservedCenter = member.CurrentMedicalCenterId;
        var preservedQueueCenter = member.QueueMedicalCenterId;
        var preservedQueueDate = member.QueueDate;
        var preservedQueueNumber = member.QueueNumber;

        member.FullName = request.FullName.Trim();
        member.MotherName = request.MotherName.Trim();
        member.BirthDate = request.BirthDate;
        member.MaritalStatusId = request.MaritalStatusId;
        member.Occupation = request.Occupation.Trim();
        member.RecruitmentNumber = recruitmentNumber;
        member.RecruitmentCenterId = request.RecruitmentCenterId;
        member.BloodTypeId = request.BloodTypeId;
        member.BloodSugarMgDl = request.BloodSugarMgDl;
        member.HeightCm = request.HeightCm;
        member.WeightKg = request.WeightKg;
        member.BloodPressure = TrimOrNull(request.BloodPressure);
        member.PulseBpm = request.PulseBpm;
        member.HasTattoo = request.HasTattoo;
        member.DistinguishingMarks = TrimOrNull(request.DistinguishingMarks);
        member.FileNumber = preservedFile;
        member.CurrentMedicalCenterId = preservedCenter;
        member.QueueMedicalCenterId = preservedQueueCenter;
        member.QueueDate = preservedQueueDate;
        member.QueueNumber = preservedQueueNumber;
        member.DataRevision += 1;
        member.UpdatedByUserId = userId;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        RecordAudit(
            userId,
            centerId,
            "MemberUpdate",
            "SUCCESS",
            member.MemberId.ToString(),
            "تم تعديل ملف المنتسب",
            oldValues: oldSnapshot,
            newValues: JsonSerializer.Serialize(new
            {
                member.FullName,
                member.MotherName,
                member.BirthDate,
                member.MaritalStatusId,
                member.Occupation,
                member.RecruitmentNumber,
                member.RecruitmentCenterId,
                member.BloodTypeId,
                member.BloodSugarMgDl,
                member.HeightCm,
                member.WeightKg,
                member.BloodPressure,
                member.PulseBpm,
                member.HasTattoo,
                member.DistinguishingMarks,
                member.FileNumber,
                member.CurrentMedicalCenterId,
                member.DataRevision
            }));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _members.GetByIdInCenterAsync(memberId, centerId, asNoTracking: true, cancellationToken)
            ?? throw new MemberNotFoundException();
        return await ToDetailsAsync(updated, cancellationToken);
    }

    #region Private helpers

    private async Task<MemberDetailsDto> ToDetailsAsync(Member member, CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<MemberDetailsDto>(member);
        var bmi = member.Bmi ?? MemberBmi.Calculate(member.HeightCm, member.WeightKg);
        dto.Bmi = bmi;
        dto.BmiCategoryNameAr = MemberBmi.Classify(bmi, await LoadBmiCategoriesAsync(cancellationToken));
        return dto;
    }

    private async Task EnsureReferenceAsync(
        short maritalStatusId,
        Guid recruitmentCenterId,
        short? bloodTypeId,
        CancellationToken cancellationToken)
    {
        var maritalOk = await _references.MaritalStatuses()
            .AnyAsync(x => x.MaritalStatusId == maritalStatusId && x.IsActive, cancellationToken);
        if (!maritalOk)
            throw new InvalidMemberDataException("الحالة الاجتماعية غير موجودة أو غير فعّالة.");

        var recruitmentOk = await _references.RecruitmentCenters()
            .AnyAsync(x => x.RecruitmentCenterId == recruitmentCenterId && x.IsActive, cancellationToken);
        if (!recruitmentOk)
            throw new InvalidMemberDataException("مركز التجنيد غير موجود أو غير فعّال.");

        if (bloodTypeId.HasValue)
        {
            var bloodOk = await _references.BloodTypes()
                .AnyAsync(x => x.BloodTypeId == bloodTypeId.Value && x.IsActive, cancellationToken);
            if (!bloodOk)
                throw new InvalidMemberDataException("زمرة الدم غير موجودة أو غير فعّالة.");
        }
    }

    private async Task<List<ValidationRule>> LoadActiveValidationRulesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await _references.ValidationRules()
            .Where(r => r.IsActive
                && r.TargetEntity.ToLower() == "member"
                && r.ValidFrom <= now
                && (r.ValidTo == null || r.ValidTo >= now))
            .ToListAsync(cancellationToken);
    }

    private Task<List<BmiCategory>> LoadBmiCategoriesAsync(CancellationToken cancellationToken)
        => _references.BmiCategories().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

    private static IQueryable<Member> ApplyCreatedAtFilter(
        IQueryable<Member> query,
        DateOnly? date,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (date.HasValue)
        {
            var start = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);
            return query.Where(m => m.CreatedAt >= start && m.CreatedAt < end);
        }

        if (fromDate.HasValue)
        {
            var start = new DateTimeOffset(fromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(m => m.CreatedAt >= start);
        }

        if (toDate.HasValue)
        {
            var endExclusive = new DateTimeOffset(toDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
            query = query.Where(m => m.CreatedAt < endExclusive);
        }

        return query;
    }

    private void RecordAudit(
        string userId,
        Guid centerId,
        string eventType,
        string result,
        string entityId,
        string reasonText,
        string? oldValues,
        string? newValues)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = eventType,
            Result = result,
            EntityType = nameof(Member),
            EntityId = entityId,
            ReasonText = reasonText,
            OldValues = oldValues,
            NewValues = newValues,
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private string RequireUser()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion
}
