using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// يجرد ويُسند مراجع المركز الطبية غير الصالحة على الكيانات التشغيلية المحلية.
/// لا يمس السجلات المركزية، ولا صور الملف الشخصي بلا مركز، ولا يعيد كتابة نقل تاريخي يتيم.
/// </summary>
public class HistoricalCenterMigrationRepository : IHistoricalCenterMigrationRepository
{
    public const int SampleSize = 20;
    public const string TransferBlockReason =
        "سجل النقل يشير إلى مركز غير معروف، ولا يُعاد كتابة طرفيه آلياً حتى لا يُزوَّر تاريخ النقل.";

    private readonly AppDbContext _context;

    public HistoricalCenterMigrationRepository(AppDbContext context) => _context = context;

    public Task<int> CountActiveCentersAsync(CancellationToken cancellationToken = default)
        => _context.MedicalCenters.AsNoTracking().CountAsync(c => c.IsActive, cancellationToken);

    public Task<HistoricalCenterMigrationCompletion?> GetLatestCompletionAsync(CancellationToken cancellationToken = default)
        => _context.HistoricalCenterMigrationCompletions
            .AsNoTracking()
            .OrderByDescending(c => c.VerifiedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> IsActiveCenterAsync(Guid centerId, CancellationToken cancellationToken = default)
        => _context.MedicalCenters.AsNoTracking()
            .AnyAsync(c => c.CenterId == centerId && c.IsActive, cancellationToken);

    public async Task<Guid?> GetSoleActiveCenterIdAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _context.MedicalCenters.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.CenterId)
            .Take(2)
            .ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }

    public async Task<HistoricalMigrationScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var known = await LoadKnownCenterIdsAsync(cancellationToken);
        var slices = new List<HistoricalMigrationSlice>();
        await AddMeasuredSlicesAsync(slices, known, cancellationToken);
        return new HistoricalMigrationScan { Slices = slices };
    }

    public async Task<HistoricalMigrationScan> AssignRepairableAsync(
        Guid targetCenterId,
        CancellationToken cancellationToken = default)
    {
        var known = await LoadKnownCenterIdsAsync(cancellationToken);
        var transfers = await OrphanTransfers(known).Select(t => t.TransferId).ToListAsync(cancellationToken);
        if (transfers.Count > 0)
        {
            return new HistoricalMigrationScan
            {
                Slices =
                [
                    Slice(
                        "MemberTransfer",
                        "سجلات النقل",
                        unassignable: true,
                        TransferBlockReason,
                        transfers.Count,
                        assigned: 0,
                        transfers.Take(SampleSize).ToList())
                ]
            };
        }

        var slices = new List<HistoricalMigrationSlice>();

        var members = await OrphanMembers(known).ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            if (!known.Contains(member.CurrentMedicalCenterId))
                member.CurrentMedicalCenterId = targetCenterId;
            if (!known.Contains(member.QueueMedicalCenterId))
                member.QueueMedicalCenterId = targetCenterId;
        }
        slices.Add(Counted("Member", "المنتسبون", members.Count, members.Select(m => m.MemberId)));

        var identities = await OrphanIdentities(known).ToListAsync(cancellationToken);
        foreach (var row in identities)
            row.MedicalCenterId = targetCenterId;
        slices.Add(Counted("MemberCenterIdentity", "هويات المركز", identities.Count, identities.Select(x => x.MemberCenterIdentityId)));

        var notes = await OrphanNotes(known).ToListAsync(cancellationToken);
        foreach (var row in notes)
            row.CenterId = targetCenterId;
        slices.Add(Counted("MemberGeneralNote", "الملاحظات العامة", notes.Count, notes.Select(x => x.NoteId)));

        var imports = await OrphanImports(known).ToListAsync(cancellationToken);
        foreach (var row in imports)
            row.CenterId = targetCenterId;
        slices.Add(Counted("MemberImportBatch", "دفعات الاستيراد", imports.Count, imports.Select(x => x.ImportBatchId)));

        var exams = await OrphanExams(known).ToListAsync(cancellationToken);
        foreach (var row in exams)
            row.CenterId = targetCenterId;
        slices.Add(Counted("MedicalExam", "الفحوصات", exams.Count, exams.Select(x => x.ExamId)));

        var assessments = await OrphanAssessments(known).ToListAsync(cancellationToken);
        foreach (var row in assessments)
            row.CenterId = targetCenterId;
        slices.Add(Counted("ClinicAssessment", "تقييمات العيادات", assessments.Count, assessments.Select(x => x.AssessmentId)));

        var deferred = await OrphanDeferred(known).ToListAsync(cancellationToken);
        foreach (var row in deferred)
            row.CenterId = targetCenterId;
        slices.Add(Counted("DeferredAction", "إجراءات التأجيل", deferred.Count, deferred.Select(x => x.DeferredActionId)));

        var decisions = await OrphanDecisions(known).ToListAsync(cancellationToken);
        foreach (var row in decisions)
            row.CenterId = targetCenterId;
        slices.Add(Counted("FinalDecision", "القرارات النهائية", decisions.Count, decisions.Select(x => x.FinalDecisionId)));

        var history = await OrphanHistory(known).ToListAsync(cancellationToken);
        foreach (var row in history)
            row.CenterId = targetCenterId;
        slices.Add(Counted("FinalDecisionHistory", "سجل القرارات", history.Count, history.Select(x => x.HistoryId)));

        var objections = await OrphanObjections(known).ToListAsync(cancellationToken);
        foreach (var row in objections)
            row.CenterId = targetCenterId;
        slices.Add(Counted("SupervisorObjection", "اعتراضات المشرف", objections.Count, objections.Select(x => x.ObjectionId)));

        var locks = await OrphanLocks(known).ToListAsync(cancellationToken);
        foreach (var row in locks)
            row.CenterId = targetCenterId;
        slices.Add(Counted("ClinicExamEditLock", "أقفال تعديل الفحص", locks.Count, locks.Select(x => x.LockId)));

        var consultations = await OrphanConsultations(known).ToListAsync(cancellationToken);
        foreach (var row in consultations)
            row.CenterId = targetCenterId;
        slices.Add(Counted("Consultation", "الاستشارات", consultations.Count, consultations.Select(x => x.ConsultationId)));

        var investigations = await OrphanInvestigations(known).ToListAsync(cancellationToken);
        foreach (var row in investigations)
            row.CenterId = targetCenterId;
        slices.Add(Counted("Investigation", "الاستقصاءات", investigations.Count, investigations.Select(x => x.InvestigationId)));

        var attachments = await OrphanAttachments(known).ToListAsync(cancellationToken);
        foreach (var row in attachments)
            row.CenterId = targetCenterId;
        slices.Add(Counted("MedicalAttachment", "المرفقات الطبية", attachments.Count, attachments.Select(x => x.AttachmentId)));

        var archives = await OrphanArchives(known).ToListAsync(cancellationToken);
        foreach (var row in archives)
            row.CenterId = targetCenterId;
        slices.Add(Counted("MemberArchive", "الأرشيف", archives.Count, archives.Select(x => x.ArchiveId)));

        var exports = await OrphanExports(known).ToListAsync(cancellationToken);
        foreach (var row in exports)
            row.CenterId = targetCenterId;
        slices.Add(Counted("RecruitmentExportBatch", "دفعات التصدير", exports.Count, exports.Select(x => x.ExportBatchId)));

        var clinics = await OrphanCenterClinics(known).ToListAsync(cancellationToken);
        foreach (var row in clinics)
            row.CenterId = targetCenterId;
        slices.Add(Counted("CenterClinic", "عيادات المركز", clinics.Count, clinics.Select(x => x.CenterClinicId)));

        var files = await OrphanFiles(known).ToListAsync(cancellationToken);
        foreach (var row in files)
            row.CenterId = targetCenterId;
        slices.Add(Counted("FileAsset", "ملفات بمركز غير صالح", files.Count, files.Select(x => x.FileAssetId)));

        slices.Add(Slice("MemberTransfer", "سجلات النقل", true, TransferBlockReason, 0, 0, []));
        return new HistoricalMigrationScan { Slices = slices };
    }

    public void AddCompletion(HistoricalCenterMigrationCompletion completion)
        => _context.HistoricalCenterMigrationCompletions.Add(completion);

    public void DiscardPendingChanges() => _context.ChangeTracker.Clear();

    private async Task AddMeasuredSlicesAsync(
        List<HistoricalMigrationSlice> slices,
        List<Guid> known,
        CancellationToken cancellationToken)
    {
        slices.Add(await MeasureAsync("Member", "المنتسبون", OrphanMembers(known).Select(x => x.MemberId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MemberCenterIdentity", "هويات المركز", OrphanIdentities(known).Select(x => x.MemberCenterIdentityId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MemberGeneralNote", "الملاحظات العامة", OrphanNotes(known).Select(x => x.NoteId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MemberImportBatch", "دفعات الاستيراد", OrphanImports(known).Select(x => x.ImportBatchId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MedicalExam", "الفحوصات", OrphanExams(known).Select(x => x.ExamId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("ClinicAssessment", "تقييمات العيادات", OrphanAssessments(known).Select(x => x.AssessmentId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("DeferredAction", "إجراءات التأجيل", OrphanDeferred(known).Select(x => x.DeferredActionId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("FinalDecision", "القرارات النهائية", OrphanDecisions(known).Select(x => x.FinalDecisionId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("FinalDecisionHistory", "سجل القرارات", OrphanHistory(known).Select(x => x.HistoryId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("SupervisorObjection", "اعتراضات المشرف", OrphanObjections(known).Select(x => x.ObjectionId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("ClinicExamEditLock", "أقفال تعديل الفحص", OrphanLocks(known).Select(x => x.LockId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("Consultation", "الاستشارات", OrphanConsultations(known).Select(x => x.ConsultationId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("Investigation", "الاستقصاءات", OrphanInvestigations(known).Select(x => x.InvestigationId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MedicalAttachment", "المرفقات الطبية", OrphanAttachments(known).Select(x => x.AttachmentId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MemberArchive", "الأرشيف", OrphanArchives(known).Select(x => x.ArchiveId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("RecruitmentExportBatch", "دفعات التصدير", OrphanExports(known).Select(x => x.ExportBatchId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("CenterClinic", "عيادات المركز", OrphanCenterClinics(known).Select(x => x.CenterClinicId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("FileAsset", "ملفات بمركز غير صالح", OrphanFiles(known).Select(x => x.FileAssetId), false, null, cancellationToken));
        slices.Add(await MeasureAsync("MemberTransfer", "سجلات النقل", OrphanTransfers(known).Select(x => x.TransferId), true, TransferBlockReason, cancellationToken));
    }

    private async Task<HistoricalMigrationSlice> MeasureAsync(
        string entityName,
        string labelAr,
        IQueryable<Guid> ids,
        bool unassignable,
        string? blockReason,
        CancellationToken cancellationToken)
    {
        var count = await ids.CountAsync(cancellationToken);
        var samples = count == 0
            ? new List<Guid>()
            : await ids.Take(SampleSize).ToListAsync(cancellationToken);
        return Slice(entityName, labelAr, unassignable, blockReason, count, assigned: 0, samples);
    }

    private Task<List<Guid>> LoadKnownCenterIdsAsync(CancellationToken cancellationToken)
        => _context.MedicalCenters.AsNoTracking().Select(c => c.CenterId).ToListAsync(cancellationToken);

    private IQueryable<Domain.Entities.Patient.Member> OrphanMembers(List<Guid> known)
        => known.Count == 0
            ? _context.Members
            : _context.Members.Where(m =>
                !known.Contains(m.CurrentMedicalCenterId) || !known.Contains(m.QueueMedicalCenterId));

    private IQueryable<Domain.Entities.Patient.MemberCenterIdentity> OrphanIdentities(List<Guid> known)
        => known.Count == 0
            ? _context.MemberCenterIdentities
            : _context.MemberCenterIdentities.Where(x => !known.Contains(x.MedicalCenterId));

    private IQueryable<Domain.Entities.Patient.MemberGeneralNote> OrphanNotes(List<Guid> known)
        => known.Count == 0
            ? _context.MemberGeneralNotes
            : _context.MemberGeneralNotes.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Patient.MemberImportBatch> OrphanImports(List<Guid> known)
        => known.Count == 0
            ? _context.MemberImportBatches
            : _context.MemberImportBatches.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.MedicalExam> OrphanExams(List<Guid> known)
        => known.Count == 0
            ? _context.MedicalExams
            : _context.MedicalExams.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.ClinicAssessment> OrphanAssessments(List<Guid> known)
        => known.Count == 0
            ? _context.ClinicAssessments
            : _context.ClinicAssessments.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.DeferredAction> OrphanDeferred(List<Guid> known)
        => known.Count == 0
            ? _context.DeferredActions
            : _context.DeferredActions.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.FinalDecision> OrphanDecisions(List<Guid> known)
        => known.Count == 0
            ? _context.FinalDecisions
            : _context.FinalDecisions.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.FinalDecisionHistory> OrphanHistory(List<Guid> known)
        => known.Count == 0
            ? _context.FinalDecisionHistories
            : _context.FinalDecisionHistories.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.SupervisorObjection> OrphanObjections(List<Guid> known)
        => known.Count == 0
            ? _context.SupervisorObjections
            : _context.SupervisorObjections.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Clinical.ClinicExamEditLock> OrphanLocks(List<Guid> known)
        => known.Count == 0
            ? _context.ClinicExamEditLocks
            : _context.ClinicExamEditLocks.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Medical.Consultation> OrphanConsultations(List<Guid> known)
        => known.Count == 0
            ? _context.Consultations
            : _context.Consultations.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Medical.Investigation> OrphanInvestigations(List<Guid> known)
        => known.Count == 0
            ? _context.Investigations
            : _context.Investigations.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Medical.MedicalAttachment> OrphanAttachments(List<Guid> known)
        => known.Count == 0
            ? _context.MedicalAttachments
            : _context.MedicalAttachments.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Archive.MemberArchive> OrphanArchives(List<Guid> known)
        => known.Count == 0
            ? _context.MemberArchives
            : _context.MemberArchives.Where(x => !known.Contains(x.CenterId));

    private IQueryable<Domain.Entities.Export.RecruitmentExportBatch> OrphanExports(List<Guid> known)
        => known.Count == 0
            ? _context.RecruitmentExportBatches
            : _context.RecruitmentExportBatches.Where(x => !known.Contains(x.CenterId));

    private IQueryable<CenterClinic> OrphanCenterClinics(List<Guid> known)
        => known.Count == 0
            ? _context.CenterClinics
            : _context.CenterClinics.Where(x => !known.Contains(x.CenterId));

    private IQueryable<FileAsset> OrphanFiles(List<Guid> known)
        => known.Count == 0
            ? _context.FileAssets.Where(x => x.CenterId != null)
            : _context.FileAssets.Where(x => x.CenterId != null && !known.Contains(x.CenterId.Value));

    private IQueryable<Domain.Entities.Patient.MemberTransfer> OrphanTransfers(List<Guid> known)
        => known.Count == 0
            ? _context.MemberTransfers
            : _context.MemberTransfers.Where(x => !known.Contains(x.FromCenterId) || !known.Contains(x.ToCenterId));

    private static HistoricalMigrationSlice Counted(
        string entityName,
        string labelAr,
        int count,
        IEnumerable<Guid> ids)
        => Slice(entityName, labelAr, false, null, count, count, ids.Take(SampleSize).ToList());

    private static HistoricalMigrationSlice Slice(
        string entityName,
        string labelAr,
        bool unassignable,
        string? blockReason,
        int orphanCount,
        int assigned,
        IReadOnlyList<Guid> samples)
        => new()
        {
            EntityName = entityName,
            LabelAr = labelAr,
            Unassignable = unassignable,
            BlockReason = blockReason,
            OrphanCount = orphanCount,
            AssignedCount = assigned,
            SampleIds = samples
        };
}
