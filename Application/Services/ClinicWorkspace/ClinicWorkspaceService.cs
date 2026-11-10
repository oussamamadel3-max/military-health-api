using System.Net;
using System.Text.Json;
using Application.Common.Options;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Time;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.Services.ClinicWorkspace;

/// <summary>
/// عمليات مساحة عمل طبيب العيادة: بحث/فتح، ملاحظات عامة، ومتابعة مؤجلات العيادة
/// (FR-032 / FR-042 / FR-043 / FR-044 / شريحة فتح FR-045).
/// </summary>
public class ClinicWorkspaceService : IClinicWorkspaceService
{
    public const string ClinicMemberViewEventType = "ClinicMemberView";
    public const string GeneralNoteSavedEventType = "MemberGeneralNoteSaved";
    public const string DeferredActionCompletedEventType = "DeferredActionCompleted";
    public const short DeferredResultStatusId = 3;
    public const string DeferredStatusRequested = "REQUESTED";
    public const string DeferredStatusCompleted = "COMPLETED";
    public const string DeferredStatusClosed = "CLOSED";

    private readonly IMemberService _members;
    private readonly IMemberRepository _memberRepository;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly IUserRepository _users;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;
    private readonly ClinicWorkspaceOptions _options;
    private readonly IBusinessClock _clock;

    public ClinicWorkspaceService(
        IMemberService members,
        IMemberRepository memberRepository,
        IClinicWorkspaceRepository clinicWorkspace,
        IUserRepository users,
        ICenterClinicRepository centerClinics,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser,
        IOptions<ClinicWorkspaceOptions> options,
        IBusinessClock? businessClock = null)
    {
        _members = members;
        _memberRepository = memberRepository;
        _clinicWorkspace = clinicWorkspace;
        _users = users;
        _centerClinics = centerClinics;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _currentUser = currentUser;
        _options = options.Value;
        _clock = businessClock ?? BusinessClock.Utc;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoctorClinicDto>> ListMyClinicsAsync(
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();

        var rows = await _users.QueryUserClinicAssignments()
            .Include(a => a.Clinic)
            .Where(a => a.UserId == userId
                && a.CenterId == centerId
                && a.IsActive
                && a.RevokedAt == null)
            .OrderBy(a => a.Clinic.SortOrder)
            .ThenBy(a => a.Clinic.Code)
            .Select(a => new DoctorClinicDto
            {
                ClinicId = a.ClinicId,
                ClinicCode = a.Clinic.Code,
                ClinicNameAr = a.Clinic.NameAr,
                ClinicSortOrder = a.Clinic.SortOrder,
                SpecialtyTitle = a.SpecialtyTitle
            })
            .ToListAsync(cancellationToken);

        // التأكد من أن رابط CenterClinic نشط للاستخدام التشغيلي.
        if (rows.Count == 0)
            return rows;

        var clinicIds = rows.Select(r => r.ClinicId).ToList();
        var activeLinks = await _centerClinics.Query()
            .Where(l => l.CenterId == centerId
                && clinicIds.Contains(l.ClinicId)
                && l.IsActive)
            .Select(l => l.ClinicId)
            .ToListAsync(cancellationToken);

        var activeSet = activeLinks.ToHashSet();
        return rows.Where(r => activeSet.Contains(r.ClinicId)).ToList();
    }

    /// <inheritdoc />
    public async Task<PaginatedList<ClinicMemberSearchItemDto>> SearchMembersAsync(
        Guid clinicId,
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (clinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        await EnsureDoctorClinicAsync(centerId, clinicId, cancellationToken);

        var page = await _members.ListAsync(query, clientCenterId, cancellationToken);
        var statuses = await ResolveStatusesForMembersAsync(
            centerId,
            clinicId,
            page.Items.Select(i => i.MemberId).ToList(),
            currentUserId: null,
            acquireLock: false,
            cancellationToken);

        var items = page.Items.Select(m =>
        {
            var status = statuses.GetValueOrDefault(m.MemberId);
            return new ClinicMemberSearchItemDto
            {
                MemberId = m.MemberId,
                FileNumber = m.FileNumber,
                QueueNumber = m.QueueNumber,
                FullName = m.FullName,
                RecruitmentNumber = m.RecruitmentNumber,
                Barcode = m.FileNumber.ToString(),
                ClinicExamStatus = status?.StatusCode ?? ClinicExamStatusCodes.NotRegistered
            };
        }).ToList();

        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 500);
        return new PaginatedList<ClinicMemberSearchItemDto>(
            items,
            page.TotalCount,
            page.PageNumber,
            pageSize);
    }

    /// <inheritdoc />
    public async Task<ClinicMemberSummaryDto> OpenMemberAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (clinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.View,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, clinicId, cancellationToken);

        var details = await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var statusMap = await ResolveStatusesForMembersAsync(
            centerId,
            clinicId,
            new List<Guid> { memberId },
            currentUserId: userId,
            acquireLock: true,
            cancellationToken);

        var status = statusMap[memberId];

        RecordClinicViewAudit(userId, centerId, memberId, clinicId, status.StatusCode);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var latestNote = details.GeneralNotes.Count > 0 ? details.GeneralNotes[0] : null;

        return new ClinicMemberSummaryDto
        {
            MemberId = details.MemberId,
            ClinicId = clinicId,
            FullName = details.FullName,
            FileNumber = details.FileNumber,
            RecruitmentNumber = details.RecruitmentNumber,
            RecruitmentCenterNameAr = details.RecruitmentCenterNameAr,
            BirthDate = details.BirthDate,
            AgeYears = CalculateAgeYears(details.BirthDate),
            QueueNumber = details.QueueNumber,
            Barcode = details.FileNumber.ToString(),
            ClinicExamStatus = status.StatusCode,
            CanAcquireEditLock = status.CanAcquireEditLock,
            EditLock = status.EditLock,
            DeferredSummary = status.DeferredSummary,
            LatestGeneralNote = latestNote is null
                ? null
                : new ClinicGeneralNoteDto
                {
                    NoteId = latestNote.NoteId,
                    NoteText = latestNote.NoteText,
                    AuthorUserId = latestNote.AuthorUserId,
                    AuthorUserName = latestNote.AuthorUserName,
                    AuthorClinicId = latestNote.AuthorClinicId,
                    AuthorClinicNameAr = latestNote.AuthorClinicNameAr,
                    WrittenAt = latestNote.WrittenAt
                }
        };
    }

    /// <inheritdoc />
    public async Task<MemberGeneralNoteSavedDto> UpsertGeneralNoteAsync(
        Guid memberId,
        UpsertMemberGeneralNoteRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new InvalidClinicWorkspaceDataException("بيانات الملاحظة العامة مطلوبة.");

        if (memberId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف المنتسب مطلوب.");

        if (request.ClinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Members.GeneralNotes,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, request.ClinicId, cancellationToken);

        // عزل المركز: GetById يرفض منتسب مركز آخر.
        await _members.GetByIdAsync(memberId, clientCenterId, cancellationToken);

        var normalizedText = string.IsNullOrWhiteSpace(request.NoteText)
            ? string.Empty
            : request.NoteText.Trim();
        var now = DateTimeOffset.UtcNow;

        var existing = await _memberRepository.GetGeneralNoteForUpdateAsync(memberId, cancellationToken);
        var previousText = existing?.NoteText;

        if (existing is null)
        {
            existing = new MemberGeneralNote
            {
                NoteId = Guid.NewGuid(),
                MemberId = memberId,
                CenterId = centerId,
                NoteText = normalizedText,
                AuthorUserId = userId,
                AuthorClinicId = request.ClinicId,
                IsActive = true,
                WrittenAt = now,
                UpdatedByUserId = userId,
                UpdatedAt = now
            };
            _memberRepository.AddGeneralNote(existing);
        }
        else
        {
            if (existing.CenterId != centerId)
                throw new InvalidClinicWorkspaceDataException("الملاحظة العامة خارج نطاق المركز النشط.");

            existing.NoteText = normalizedText;
            existing.IsActive = true;
            existing.AuthorUserId = userId;
            existing.AuthorClinicId = request.ClinicId;
            existing.WrittenAt = now;
            existing.UpdatedByUserId = userId;
            existing.UpdatedAt = now;
        }

        RecordGeneralNoteAudit(
            userId,
            centerId,
            memberId,
            request.ClinicId,
            existing.NoteId,
            previousText,
            normalizedText);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var clinicName = await _centerClinics.Query()
            .Where(l => l.CenterId == centerId && l.ClinicId == request.ClinicId)
            .Select(l => l.Clinic.NameAr)
            .FirstOrDefaultAsync(cancellationToken);

        var authorName = await _users.QueryUsers()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName ?? u.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        return new MemberGeneralNoteSavedDto
        {
            NoteId = existing.NoteId,
            MemberId = memberId,
            NoteText = existing.NoteText,
            HasGeneralNotesAlert = !string.IsNullOrWhiteSpace(existing.NoteText),
            AuthorUserId = existing.AuthorUserId,
            AuthorUserName = authorName,
            AuthorClinicId = existing.AuthorClinicId,
            AuthorClinicNameAr = clinicName,
            WrittenAt = existing.WrittenAt
        };
    }

    /// <inheritdoc />
    public async Task<PaginatedList<DeferredActionListItemDto>> ListDeferredActionsAsync(
        DeferredActionListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            throw new InvalidClinicWorkspaceDataException("معاملات قائمة المؤجلات مطلوبة.");

        if (query.ClinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.DeferredAction,
            clientCenterId,
            cancellationToken);

        await EnsureDoctorClinicAsync(centerId, query.ClinicId, cancellationToken);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 500);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var statusFilter = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : query.Status.Trim().ToUpperInvariant();

        if (statusFilter is not null
            && statusFilter is not DeferredStatusRequested and not DeferredStatusCompleted)
        {
            throw new InvalidClinicWorkspaceDataException(
                "حالة الإجراء غير صالحة. استخدم REQUESTED أو COMPLETED.");
        }

        var source = _clinicWorkspace.QueryDeferredActions()
            .Where(d => d.CenterId == centerId
                && d.ClinicId == query.ClinicId
                && d.Status != DeferredStatusClosed);

        if (statusFilter is not null)
            source = source.Where(d => d.Status == statusFilter);

        if (query.ActionTypeId.HasValue)
            source = source.Where(d => d.ActionTypeId == query.ActionTypeId.Value);

        if (query.FromDate.HasValue)
        {
            var fromOffset = _clock.StartOfDay(query.FromDate.Value);
            source = source.Where(d => d.RequestedAt >= fromOffset);
        }

        if (query.ToDate.HasValue)
        {
            var toOffset = _clock.EndOfDayExclusive(query.ToDate.Value);
            source = source.Where(d => d.RequestedAt < toOffset);
        }

        if (query.FileNumber.HasValue)
            source = source.Where(d => d.Member.FileNumber == query.FileNumber.Value);

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            var name = query.FullName.Trim();
            source = source.Where(d => d.Member.FullName.Contains(name));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var rows = await source
            .OrderByDescending(d => d.RequestedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DeferredActionListItemDto
            {
                DeferredActionId = d.DeferredActionId,
                MemberId = d.MemberId,
                FileNumber = d.Member.FileNumber,
                FullName = d.Member.FullName,
                ClinicId = d.ClinicId!.Value,
                ClinicNameAr = d.Clinic != null ? d.Clinic.NameAr : null,
                DeferReason = d.DeferReason,
                ActionTypeId = d.ActionTypeId,
                ActionTypeNameAr = d.ActionType.NameAr,
                ActionDescription = d.ActionDescription,
                FollowupNote = d.FollowupNote,
                Status = d.Status,
                DeferUntil = d.DeferUntil,
                RequestedAt = d.RequestedAt,
                RequestedByUserId = d.RequestedByUserId,
                RequestedByUserName = d.RequestedByUser.FullName ?? d.RequestedByUser.UserName,
                CompletedByUserId = d.CompletedByUserId,
                CompletedAt = d.CompletedAt,
                IsOverdue = d.Status == DeferredStatusRequested
                    && d.DeferUntil != null
                    && d.DeferUntil < today
            })
            .ToListAsync(cancellationToken);

        return new PaginatedList<DeferredActionListItemDto>(rows, totalCount, pageNumber, pageSize);
    }

    /// <inheritdoc />
    public async Task<DeferredActionCompletedDto> CompleteDeferredActionAsync(
        Guid deferredActionId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        if (deferredActionId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف الإجراء المؤجّل مطلوب.");

        if (clinicId == Guid.Empty)
            throw new InvalidClinicWorkspaceDataException("معرّف العيادة مطلوب.");

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.DeferredAction,
            clientCenterId,
            cancellationToken);

        var userId = RequireUserId();
        await EnsureDoctorClinicAsync(centerId, clinicId, cancellationToken);

        var action = await _clinicWorkspace.QueryDeferredActions(asNoTracking: false)
            .FirstOrDefaultAsync(
                d => d.DeferredActionId == deferredActionId
                    && d.CenterId == centerId
                    && d.ClinicId == clinicId,
                cancellationToken);

        if (action is null)
            throw new DeferredActionNotFoundException();

        if (!string.Equals(action.Status, DeferredStatusRequested, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDeferredActionStateException(
                action.Status == DeferredStatusCompleted
                    ? "الإجراء مكتمل مسبقاً."
                    : "لا يمكن تعليم إجراء مغلق أو غير مطلوب كمكتمل.");
        }

        var now = DateTimeOffset.UtcNow;
        action.Status = DeferredStatusCompleted;
        action.CompletedByUserId = userId;
        action.CompletedAt = now;

        _audit.Add(new AuditEvent
        {
            OccurredAt = now,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = DeferredActionCompletedEventType,
            Result = "SUCCESS",
            EntityType = nameof(DeferredAction),
            EntityId = action.DeferredActionId.ToString(),
            ReasonText = $"إتمام إجراء مؤجّل للمنتسب {action.MemberId:D} في العيادة {clinicId:D}",
            OldValues = JsonSerializer.Serialize(DeferredStatusRequested),
            NewValues = JsonSerializer.Serialize(DeferredStatusCompleted),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeferredActionCompletedDto
        {
            DeferredActionId = action.DeferredActionId,
            MemberId = action.MemberId,
            ClinicId = clinicId,
            Status = action.Status,
            CompletedByUserId = userId,
            CompletedAt = now
        };
    }

    #region Private Helpers

    private async Task EnsureDoctorClinicAsync(
        Guid centerId,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        await DoctorClinicAccess.RequireActiveAssignmentAsync(
            _users, _centerClinics, centerId, clinicId, userId, cancellationToken);
    }

    private async Task<Dictionary<Guid, ResolvedClinicStatus>> ResolveStatusesForMembersAsync(
        Guid centerId,
        Guid clinicId,
        IReadOnlyList<Guid> memberIds,
        string? currentUserId,
        bool acquireLock,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ResolvedClinicStatus>();
        if (memberIds.Count == 0)
            return result;

        var now = DateTimeOffset.UtcNow;
        var timeoutMinutes = NormalizeTimeoutMinutes(_options.EditLockTimeoutMinutes);

        var locks = await _clinicWorkspace.QueryEditLocks()
            .Where(l => l.CenterId == centerId
                && l.ClinicId == clinicId
                && memberIds.Contains(l.MemberId))
            .ToListAsync(cancellationToken);

        var lockByMember = locks.ToDictionary(l => l.MemberId);

        var currentExams = await _clinicWorkspace.QueryMedicalExams()
            .Where(e => e.CenterId == centerId
                && e.ClinicId == clinicId
                && e.IsCurrent
                && memberIds.Contains(e.MemberId))
            .Select(e => new { e.MemberId, e.ExamId })
            .ToListAsync(cancellationToken);

        var examByMember = currentExams.ToDictionary(e => e.MemberId, e => e.ExamId);

        var assessments = await _clinicWorkspace.QueryClinicAssessments()
            .Where(a => a.CenterId == centerId
                && a.ClinicId == clinicId
                && memberIds.Contains(a.MemberId))
            .Select(a => new AssessmentSnapshot(a.MemberId, a.ExamId, a.AssessmentId, a.ResultStatusId))
            .ToListAsync(cancellationToken);

        var assessmentByMember = assessments
            .GroupBy(a => a.MemberId)
            .ToDictionary(g => g.Key, g => g.First());

        var deferredMemberIds = assessments
            .Where(a => a.ResultStatusId == DeferredResultStatusId)
            .Select(a => a.MemberId)
            .Distinct()
            .ToList();

        var deferredActions = deferredMemberIds.Count == 0
            ? new List<DeferredAction>()
            : await _clinicWorkspace.QueryDeferredActions()
                .Where(d => d.CenterId == centerId
                    && d.ClinicId == clinicId
                    && deferredMemberIds.Contains(d.MemberId)
                    && d.Status != DeferredStatusClosed)
                .OrderByDescending(d => d.RequestedAt)
                .ToListAsync(cancellationToken);

        var deferredByMember = deferredActions
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var memberId in memberIds)
        {
            lockByMember.TryGetValue(memberId, out var existingLock);
            var lockActive = existingLock is not null && existingLock.ExpiresAt > now;
            var lockedByOther = lockActive
                && currentUserId is not null
                && !string.Equals(existingLock!.LockedByUserId, currentUserId, StringComparison.Ordinal);

            if (lockedByOther)
            {
                result[memberId] = new ResolvedClinicStatus
                {
                    StatusCode = ClinicExamStatusCodes.InEdit,
                    CanAcquireEditLock = false,
                    EditLock = MapLock(existingLock!, isOwnedByCurrentUser: false)
                };
                continue;
            }

            if (acquireLock && currentUserId is not null)
            {
                var owned = await AcquireOrRefreshLockAsync(
                    centerId,
                    clinicId,
                    memberId,
                    currentUserId,
                    existingLock,
                    now,
                    timeoutMinutes,
                    cancellationToken);

                result[memberId] = BuildNonLockStatus(
                    memberId,
                    examByMember,
                    assessmentByMember,
                    deferredByMember,
                    ownedLock: owned,
                    canAcquire: true);
                continue;
            }

            // مسار البحث: إبلاغ InEdit إن وُجد قفل نشط (حتى لو للمستخدم نفسه)، وإلا حالة الفحص/التقييم.
            if (lockActive)
            {
                var isOwned = currentUserId is not null
                    && string.Equals(existingLock!.LockedByUserId, currentUserId, StringComparison.Ordinal);
                result[memberId] = new ResolvedClinicStatus
                {
                    StatusCode = ClinicExamStatusCodes.InEdit,
                    CanAcquireEditLock = isOwned,
                    EditLock = MapLock(existingLock!, isOwned)
                };
                continue;
            }

            result[memberId] = BuildNonLockStatus(
                memberId,
                examByMember,
                assessmentByMember,
                deferredByMember,
                ownedLock: null,
                canAcquire: true);
        }

        return result;
    }

    private async Task<ClinicExamEditLock> AcquireOrRefreshLockAsync(
        Guid centerId,
        Guid clinicId,
        Guid memberId,
        string userId,
        ClinicExamEditLock? existing,
        DateTimeOffset now,
        int timeoutMinutes,
        CancellationToken cancellationToken)
    {
        var expiresAt = now.AddMinutes(timeoutMinutes);

        if (existing is null)
        {
            var created = new ClinicExamEditLock
            {
                LockId = Guid.NewGuid(),
                MemberId = memberId,
                CenterId = centerId,
                ClinicId = clinicId,
                LockedByUserId = userId,
                LockedAt = now,
                ExpiresAt = expiresAt
            };
            _clinicWorkspace.AddEditLock(created);
            return created;
        }

        // إعادة تحميل متعقّبة للتحديث عند انتهاء المهلة أو ملكية القفل.
        var tracked = await _clinicWorkspace.GetEditLockAsync(
            memberId,
            clinicId,
            asNoTracking: false,
            cancellationToken);

        if (tracked is null)
        {
            var created = new ClinicExamEditLock
            {
                LockId = Guid.NewGuid(),
                MemberId = memberId,
                CenterId = centerId,
                ClinicId = clinicId,
                LockedByUserId = userId,
                LockedAt = now,
                ExpiresAt = expiresAt
            };
            _clinicWorkspace.AddEditLock(created);
            return created;
        }

        if (tracked.ExpiresAt <= now
            || string.Equals(tracked.LockedByUserId, userId, StringComparison.Ordinal))
        {
            if (tracked.ExpiresAt <= now)
                tracked.LockedAt = now;

            tracked.LockedByUserId = userId;
            tracked.ExpiresAt = expiresAt;
            return tracked;
        }

        // طبيب آخر ما زال يملك قفلاً نشطاً — يفترض أن المستدعي قد عالج الحالة مسبقاً.
        return tracked;
    }

    private static ResolvedClinicStatus BuildNonLockStatus(
        Guid memberId,
        Dictionary<Guid, Guid> examByMember,
        Dictionary<Guid, AssessmentSnapshot> assessmentByMember,
        Dictionary<Guid, DeferredAction> deferredByMember,
        ClinicExamEditLock? ownedLock,
        bool canAcquire)
    {
        ClinicDeferredSummaryDto? deferredSummary = null;
        string status;

        if (assessmentByMember.TryGetValue(memberId, out var assessment)
            && assessment.ResultStatusId == DeferredResultStatusId)
        {
            status = ClinicExamStatusCodes.Deferred;
            if (deferredByMember.TryGetValue(memberId, out var deferred))
            {
                deferredSummary = new ClinicDeferredSummaryDto
                {
                    DeferReason = deferred.DeferReason,
                    ActionTypeId = deferred.ActionTypeId,
                    ActionTypeNameAr = deferred.ActionType?.NameAr,
                    ActionDescription = deferred.ActionDescription,
                    Status = deferred.Status
                };
            }
        }
        else if (examByMember.ContainsKey(memberId))
        {
            status = ClinicExamStatusCodes.Registered;
        }
        else
        {
            status = ClinicExamStatusCodes.NotRegistered;
        }

        return new ResolvedClinicStatus
        {
            StatusCode = status,
            CanAcquireEditLock = canAcquire,
            EditLock = ownedLock is null ? null : MapLock(ownedLock, isOwnedByCurrentUser: true),
            DeferredSummary = deferredSummary
        };
    }

    private static ClinicMemberEditLockDto MapLock(ClinicExamEditLock editLock, bool isOwnedByCurrentUser)
        => new()
        {
            LockedByUserId = editLock.LockedByUserId,
            LockedByUserName = editLock.LockedByUser?.FullName ?? editLock.LockedByUser?.UserName,
            LockedAt = editLock.LockedAt,
            ExpiresAt = editLock.ExpiresAt,
            IsOwnedByCurrentUser = isOwnedByCurrentUser
        };

    private void RecordClinicViewAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        Guid clinicId,
        string statusCode)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = ClinicMemberViewEventType,
            Result = "SUCCESS",
            EntityType = nameof(Member),
            EntityId = memberId.ToString(),
            ReasonText = $"اطلاع ملف منتسب من العيادة {clinicId:D} بحالة {statusCode}",
            OldValues = null,
            NewValues = null,
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private void RecordGeneralNoteAudit(
        string userId,
        Guid centerId,
        Guid memberId,
        Guid clinicId,
        Guid noteId,
        string? previousText,
        string newText)
    {
        _audit.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = userId,
            ContextScope = CenterClaimTypes.Center,
            CenterId = centerId,
            EventType = GeneralNoteSavedEventType,
            Result = "SUCCESS",
            EntityType = nameof(MemberGeneralNote),
            EntityId = noteId.ToString(),
            ReasonText = $"حفظ ملاحظة عامة للمنتسب {memberId:D} من العيادة {clinicId:D}",
            // jsonb يتطلب تمثيلاً JSON صالحاً؛ النص الخام (عربي/فارغ) يرفضه PostgreSQL بـ 22P02.
            OldValues = previousText is null ? null : JsonSerializer.Serialize(previousText),
            NewValues = JsonSerializer.Serialize(newText),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : _currentUser.CorrelationId,
            IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        });
    }

    private string RequireUserId()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }

    private static int NormalizeTimeoutMinutes(int minutes)
        => minutes < 1 ? 30 : minutes;

    private static int CalculateAgeYears(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
            age--;
        return age < 0 ? 0 : age;
    }

    private sealed class ResolvedClinicStatus
    {
        public string StatusCode { get; set; } = ClinicExamStatusCodes.NotRegistered;
        public bool CanAcquireEditLock { get; set; }
        public ClinicMemberEditLockDto? EditLock { get; set; }
        public ClinicDeferredSummaryDto? DeferredSummary { get; set; }
    }

    private sealed record AssessmentSnapshot(
        Guid MemberId,
        Guid ExamId,
        Guid AssessmentId,
        short ResultStatusId);

    #endregion
}
