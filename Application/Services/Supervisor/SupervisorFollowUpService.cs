using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Members;
using Domain.Entities.Clinical;
using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// قائمة متابعة المشرف المحلية مع البحث والتصفية والترقيم (FR-057).
/// </summary>
public class SupervisorFollowUpService : ISupervisorFollowUpService
{
    public const string SortUpdatedAtDesc = "UpdatedAtDesc";
    public const string SortUpdatedAtAsc = "UpdatedAtAsc";
    public const string SortCreatedAtDesc = "CreatedAtDesc";
    public const string SortFileNumberAsc = "FileNumberAsc";
    public const string SortDecidedAtDesc = "DecidedAtDesc";

    public const string DeferredStatusClosed = "CLOSED";

    private readonly IPermissionGate _permissionGate;
    private readonly IMemberRepository _members;
    private readonly IClinicWorkspaceRepository _clinicWorkspace;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IReferenceDataRepository _referenceData;

    public SupervisorFollowUpService(
        IPermissionGate permissionGate,
        IMemberRepository members,
        IClinicWorkspaceRepository clinicWorkspace,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData)
    {
        _permissionGate = permissionGate;
        _members = members;
        _clinicWorkspace = clinicWorkspace;
        _centerClinics = centerClinics;
        _referenceData = referenceData;
    }

    /// <inheritdoc />
    public async Task<PaginatedList<SupervisorFollowUpItemDto>> ListAsync(
        SupervisorFollowUpQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new SupervisorFollowUpQuery();

        var centerId = await _permissionGate.EnsureLocalAsync(
            MilitaryHealthPermissions.Clinical.FinalDecision,
            clientCenterId,
            cancellationToken);

        NormalizeAndValidateQuery(query);

        var mandatoryClinics = await SupervisorMandatoryClinicLoader.LoadAsync(
            centerId,
            _centerClinics,
            _referenceData,
            cancellationToken);

        var mandatoryPairs = mandatoryClinics.Select(c => (c.ClinicId, c.Code)).ToList();
        var mandatoryIds = mandatoryClinics.Select(c => c.ClinicId).ToList();
        var mandatoryCount = mandatoryIds.Count;

        var membersQuery = BuildMemberBaseQuery(centerId, query);
        membersQuery = ApplyStatusAndClinicalFilters(
            membersQuery,
            centerId,
            query,
            mandatoryIds,
            mandatoryCount);

        membersQuery = ApplySort(membersQuery, centerId, query.SortBy);

        var pageNumber = query.PageNumber;
        var pageSize = query.PageSize;
        var totalCount = await membersQuery.CountAsync(cancellationToken);
        var pageMembers = await membersQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (pageMembers.Count == 0)
        {
            return new PaginatedList<SupervisorFollowUpItemDto>(
                Array.Empty<SupervisorFollowUpItemDto>(),
                totalCount,
                pageNumber,
                pageSize);
        }

        var memberIds = pageMembers.Select(m => m.MemberId).ToList();

        var assessments = await _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.CenterId == centerId
                && memberIds.Contains(a.MemberId)
                && mandatoryIds.Contains(a.ClinicId))
            .Select(a => new { a.MemberId, a.ClinicId })
            .ToListAsync(cancellationToken);

        var assessedByMember = assessments
            .GroupBy(a => a.MemberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ClinicId).ToHashSet());

        var decisions = await _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId && memberIds.Contains(d.MemberId))
            .ToListAsync(cancellationToken);
        var decisionByMember = decisions.ToDictionary(d => d.MemberId);

        var amendedMemberIds = await _clinicWorkspace.QueryFinalDecisionHistories(asNoTracking: true)
            .Where(h => h.CenterId == centerId && memberIds.Contains(h.MemberId))
            .Select(h => h.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var amendedSet = amendedMemberIds.ToHashSet();

        var openDeferred = await _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.CenterId == centerId
                && memberIds.Contains(d.MemberId)
                && d.FinalDecisionId != null
                && d.Status != DeferredStatusClosed)
            .OrderByDescending(d => d.RequestedAt)
            .ToListAsync(cancellationToken);

        var openDeferredByMember = openDeferred
            .GroupBy(d => d.MemberId)
            .ToDictionary(g => g.Key, g => g.First());

        var statusIds = decisions.Select(d => d.ResultStatusId).Distinct().ToList();
        var statusNames = statusIds.Count == 0
            ? new Dictionary<short, string>()
            : await _referenceData.ResultStatuses(asNoTracking: true)
                .Where(s => statusIds.Contains(s.StatusId))
                .ToDictionaryAsync(s => s.StatusId, s => s.NameAr, cancellationToken);

        var actionTypeIds = openDeferred.Select(d => d.ActionTypeId).Distinct().ToList();
        var actionTypeNames = actionTypeIds.Count == 0
            ? new Dictionary<short, string>()
            : await _referenceData.RequiredActionTypes()
                .Where(a => actionTypeIds.Contains(a.ActionTypeId))
                .ToDictionaryAsync(a => a.ActionTypeId, a => a.NameAr, cancellationToken);

        var items = pageMembers.Select(m =>
        {
            assessedByMember.TryGetValue(m.MemberId, out var assessedSet);
            assessedSet ??= new HashSet<Guid>();
            var missing = MandatoryClinicCompleteness.GetMissingClinicCodes(mandatoryPairs, assessedSet);
            var complete = MandatoryClinicCompleteness.AreComplete(mandatoryPairs, assessedSet)
                && mandatoryCount > 0;

            decisionByMember.TryGetValue(m.MemberId, out var decision);
            var isAmended = amendedSet.Contains(m.MemberId);
            var canIssue = complete && decision is null;
            openDeferredByMember.TryGetValue(m.MemberId, out var deferred);

            return new SupervisorFollowUpItemDto
            {
                MemberId = m.MemberId,
                FileNumber = m.FileNumber,
                Barcode = m.FileNumber.ToString(),
                QueueNumber = m.QueueNumber,
                QueueDate = m.QueueDate,
                FullName = m.FullName,
                RecruitmentNumber = m.RecruitmentNumber,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                ExportStatus = m.ExportStatus,
                MandatoryClinicCount = mandatoryCount,
                AssessedMandatoryClinicCount = assessedSet.Count,
                AreMandatoryClinicsComplete = complete,
                MissingClinicCodes = missing,
                IsReadyToDecide = canIssue,
                CanIssueFinalDecision = canIssue,
                IsDeferred = decision?.ResultStatusId == FinalDecisionService.DeferredResultStatusId,
                IsAmended = isAmended,
                FollowUpStatus = ResolveFollowUpStatus(decision, complete, isAmended),
                FinalDecisionId = decision?.FinalDecisionId,
                FinalResultStatusId = decision?.ResultStatusId,
                FinalResultStatusNameAr = decision is null
                    ? null
                    : statusNames.GetValueOrDefault(decision.ResultStatusId),
                RejectionReason = decision?.RejectionReason,
                DecidedAt = decision?.DecidedAt,
                OpenDeferredActionId = deferred?.DeferredActionId,
                OpenDeferredActionTypeId = deferred?.ActionTypeId,
                OpenDeferredActionTypeNameAr = deferred is null
                    ? null
                    : actionTypeNames.GetValueOrDefault(deferred.ActionTypeId),
                OpenDeferredStatus = deferred?.Status,
                OpenDeferUntil = deferred?.DeferUntil
            };
        }).ToList();

        return new PaginatedList<SupervisorFollowUpItemDto>(items, totalCount, pageNumber, pageSize);
    }

    #region Query building

    private static void NormalizeAndValidateQuery(SupervisorFollowUpQuery query)
    {
        var barcodeCarrier = new MemberListQuery
        {
            Barcode = query.Barcode,
            FileNumber = query.FileNumber
        };

        try
        {
            MemberFieldValidation.ApplyBarcodeToFileNumber(barcodeCarrier);
            MemberFieldValidation.ValidateListDateFilter(query.Date, query.FromDate, query.ToDate);
        }
        catch (InvalidMemberDataException ex)
        {
            throw new InvalidSupervisorFollowUpDataException(ex.Message);
        }

        query.FileNumber = barcodeCarrier.FileNumber;

        if (query.PageNumber < 1) query.PageNumber = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > 200) query.PageSize = 200;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToUpperInvariant();
            if (status is not SupervisorFollowUpStatuses.Ready
                and not SupervisorFollowUpStatuses.Incomplete
                and not SupervisorFollowUpStatuses.Deferred
                and not SupervisorFollowUpStatuses.Decided
                and not SupervisorFollowUpStatuses.Amended)
            {
                throw new InvalidSupervisorFollowUpDataException(
                    "حالة الملف غير معروفة. استخدم READY أو INCOMPLETE أو DEFERRED أو DECIDED أو AMENDED.");
            }

            query.Status = status;
        }
        else
        {
            query.Status = null;
        }

        if (query.ResultStatusId.HasValue
            && query.ResultStatusId is not FinalDecisionService.AcceptedResultStatusId
                and not FinalDecisionService.RejectedResultStatusId
                and not FinalDecisionService.DeferredResultStatusId)
        {
            throw new InvalidSupervisorFollowUpDataException(
                "نتيجة القرار النهائي للتصفية يجب أن تكون 1 أو 2 أو 3.");
        }

        if (query.MissingClinicId.HasValue && query.MissingClinicId.Value == Guid.Empty)
            throw new InvalidSupervisorFollowUpDataException("معرّف العيادة الناقصة غير صالح.");

        if (!string.IsNullOrWhiteSpace(query.ActionStatus))
        {
            var actionStatus = query.ActionStatus.Trim().ToUpperInvariant();
            if (actionStatus is not "REQUESTED" and not "COMPLETED" and not "CLOSED")
            {
                throw new InvalidSupervisorFollowUpDataException(
                    "حالة الإجراء المؤجل غير صالحة. استخدم REQUESTED أو COMPLETED أو CLOSED.");
            }

            query.ActionStatus = actionStatus;
        }
        else
        {
            query.ActionStatus = null;
        }

        if (!string.IsNullOrWhiteSpace(query.SortBy))
        {
            var sort = query.SortBy.Trim();
            if (!string.Equals(sort, SortUpdatedAtDesc, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sort, SortUpdatedAtAsc, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sort, SortCreatedAtDesc, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sort, SortFileNumberAsc, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sort, SortDecidedAtDesc, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidSupervisorFollowUpDataException(
                    "قيمة الترتيب غير معروفة.");
            }

            query.SortBy = sort;
        }
        else
        {
            query.SortBy = SortUpdatedAtDesc;
        }
    }

    private IQueryable<Member> BuildMemberBaseQuery(Guid centerId, SupervisorFollowUpQuery query)
    {
        var q = _members.Query(asNoTracking: true)
            .Where(m => m.CurrentMedicalCenterId == centerId);

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
        return q;
    }

    private IQueryable<Member> ApplyStatusAndClinicalFilters(
        IQueryable<Member> membersQuery,
        Guid centerId,
        SupervisorFollowUpQuery query,
        IReadOnlyList<Guid> mandatoryIds,
        int mandatoryCount)
    {
        var decisions = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
            .Where(d => d.CenterId == centerId);

        var histories = _clinicWorkspace.QueryFinalDecisionHistories(asNoTracking: true)
            .Where(h => h.CenterId == centerId);

        var assessments = _clinicWorkspace.QueryClinicAssessments(asNoTracking: true)
            .Where(a => a.CenterId == centerId && mandatoryIds.Contains(a.ClinicId));

        var deferredActions = _clinicWorkspace.QueryDeferredActions(asNoTracking: true)
            .Where(d => d.CenterId == centerId);

        if (query.ResultStatusId.HasValue)
        {
            var resultId = query.ResultStatusId.Value;
            membersQuery = membersQuery.Where(m =>
                decisions.Any(d => d.MemberId == m.MemberId && d.ResultStatusId == resultId));
        }

        if (query.MissingClinicId.HasValue)
        {
            var clinicId = query.MissingClinicId.Value;
            if (mandatoryIds.Count > 0 && !mandatoryIds.Contains(clinicId))
            {
                throw new InvalidSupervisorFollowUpDataException(
                    "العيادة المحددة ليست ضمن العيادات الإلزامية للمركز النشط.");
            }

            membersQuery = membersQuery.Where(m =>
                !assessments.Any(a => a.MemberId == m.MemberId && a.ClinicId == clinicId));
        }

        if (query.ActionTypeId.HasValue || !string.IsNullOrWhiteSpace(query.ActionStatus))
        {
            var actionTypeId = query.ActionTypeId;
            var actionStatus = query.ActionStatus;
            membersQuery = membersQuery.Where(m =>
                deferredActions.Any(d =>
                    d.MemberId == m.MemberId
                    && (!actionTypeId.HasValue || d.ActionTypeId == actionTypeId.Value)
                    && (actionStatus == null || d.Status == actionStatus)));
        }

        if (string.IsNullOrWhiteSpace(query.Status))
            return membersQuery;

        switch (query.Status)
        {
            case SupervisorFollowUpStatuses.Ready:
                membersQuery = membersQuery.Where(m =>
                    !decisions.Any(d => d.MemberId == m.MemberId)
                    && mandatoryCount > 0
                    && assessments
                        .Where(a => a.MemberId == m.MemberId)
                        .Select(a => a.ClinicId)
                        .Distinct()
                        .Count() == mandatoryCount);
                break;

            case SupervisorFollowUpStatuses.Incomplete:
                membersQuery = membersQuery.Where(m =>
                    !decisions.Any(d => d.MemberId == m.MemberId)
                    && (mandatoryCount == 0
                        || assessments
                            .Where(a => a.MemberId == m.MemberId)
                            .Select(a => a.ClinicId)
                            .Distinct()
                            .Count() < mandatoryCount));
                break;

            case SupervisorFollowUpStatuses.Deferred:
                membersQuery = membersQuery.Where(m =>
                    decisions.Any(d =>
                        d.MemberId == m.MemberId
                        && d.ResultStatusId == FinalDecisionService.DeferredResultStatusId));
                break;

            case SupervisorFollowUpStatuses.Decided:
                membersQuery = membersQuery.Where(m =>
                    decisions.Any(d =>
                        d.MemberId == m.MemberId
                        && (d.ResultStatusId == FinalDecisionService.AcceptedResultStatusId
                            || d.ResultStatusId == FinalDecisionService.RejectedResultStatusId))
                    && !histories.Any(h => h.MemberId == m.MemberId));
                break;

            case SupervisorFollowUpStatuses.Amended:
                membersQuery = membersQuery.Where(m =>
                    histories.Any(h => h.MemberId == m.MemberId)
                    && decisions.Any(d =>
                        d.MemberId == m.MemberId
                        && (d.ResultStatusId == FinalDecisionService.AcceptedResultStatusId
                            || d.ResultStatusId == FinalDecisionService.RejectedResultStatusId)));
                break;
        }

        return membersQuery;
    }

    private IQueryable<Member> ApplySort(IQueryable<Member> membersQuery, Guid centerId, string? sortBy)
    {
        if (string.Equals(sortBy, SortUpdatedAtAsc, StringComparison.OrdinalIgnoreCase))
            return membersQuery.OrderBy(m => m.UpdatedAt).ThenBy(m => m.FileNumber);

        if (string.Equals(sortBy, SortCreatedAtDesc, StringComparison.OrdinalIgnoreCase))
            return membersQuery.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.FileNumber);

        if (string.Equals(sortBy, SortFileNumberAsc, StringComparison.OrdinalIgnoreCase))
            return membersQuery.OrderBy(m => m.FileNumber);

        if (string.Equals(sortBy, SortDecidedAtDesc, StringComparison.OrdinalIgnoreCase))
        {
            var decisions = _clinicWorkspace.QueryFinalDecisions(asNoTracking: true)
                .Where(d => d.CenterId == centerId);

            return membersQuery
                .OrderByDescending(m => decisions
                    .Where(d => d.MemberId == m.MemberId)
                    .Select(d => (DateTimeOffset?)d.DecidedAt)
                    .FirstOrDefault())
                .ThenByDescending(m => m.UpdatedAt);
        }

        return membersQuery.OrderByDescending(m => m.UpdatedAt).ThenByDescending(m => m.FileNumber);
    }

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
            var endExclusive = new DateTimeOffset(toDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                .AddDays(1);
            query = query.Where(m => m.CreatedAt < endExclusive);
        }

        return query;
    }

    private static string ResolveFollowUpStatus(
        FinalDecision? decision,
        bool complete,
        bool isAmended)
    {
        if (decision is null)
            return complete ? SupervisorFollowUpStatuses.Ready : SupervisorFollowUpStatuses.Incomplete;

        if (decision.ResultStatusId == FinalDecisionService.DeferredResultStatusId)
            return SupervisorFollowUpStatuses.Deferred;

        return isAmended
            ? SupervisorFollowUpStatuses.Amended
            : SupervisorFollowUpStatuses.Decided;
    }

    #endregion
}
