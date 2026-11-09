using Domain.Entities.Patient;

namespace Application.Interfaces.Repositories;

/// <summary>مستودع ملف المنتسب وهوية المركز على الجداول الحالية.</summary>
public interface IMemberRepository
{
    IQueryable<Member> Query(bool asNoTracking = true);

    Task<Member?> GetByIdAsync(Guid memberId, bool asNoTracking = false, CancellationToken cancellationToken = default);

    Task<Member?> GetByIdInCenterAsync(
        Guid memberId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يقفل صف المنتسب داخل المعاملة الحالية (<c>SELECT … FOR UPDATE</c> على PostgreSQL)
    /// لمنع السباق مع إصدار/تعديل القرار النهائي أو تعديل الملف.
    /// لا تأثير على InMemory (يُتخطى كما في أقفال التخصيص).
    /// </summary>
    Task LockMemberForUpdateAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<bool> RecruitmentNumberExistsAsync(
        string recruitmentNumber,
        Guid? excludingMemberId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعيد (رقم الملف التالي، رقم الدور التالي لليوم في المركز).
    /// يجب استدعاؤه داخل معاملة مفتوحة؛ على PostgreSQL يُقفل التسلسل حتى نهاية المعاملة.
    /// يحسب التالي من أعلى رقم محفوظ وأعلى رقم معلّق في وحدة العمل الحالية (ChangeTracker).
    /// </summary>
    Task<(long FileNumber, int QueueNumber)> AllocateFileAndQueueNumbersAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default);

    void Add(Member member);

    void AddIdentity(MemberCenterIdentity identity);

    /// <summary>
    /// يخصّص رقم الدور اليومي التالي للمركز ضمن المعاملة الحالية دون المساس بتسلسل رقم الملف.
    /// </summary>
    Task<int> AllocateDailyQueueNumberAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default);

    /// <summary>الهوية المحلية المفتوحة للمنتسب (ValidTo IS NULL) للتتبع والتحديث.</summary>
    Task<MemberCenterIdentity?> GetOpenIdentityForUpdateAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ينفّذ النقل الذري عبر <c>patient.transferMember</c> (أو محاكاة مكافئة على InMemory)،
    /// بما في ذلك إغلاق/فتح الهوية وتحديث المركز الحالي وإعادة ضبط علامات التصدير.
    /// </summary>
    Task<Guid> ExecuteAuthorizedTransferAsync(
        Guid memberId,
        Guid toCenterId,
        string newCenterMemberId,
        string executedByUserId,
        string transferReason,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken = default);

    /// <summary>استعلام سجلات النقل مع مراكز المصدر/الهدف والمنفّذ.</summary>
    IQueryable<MemberTransfer> QueryTransfers(bool asNoTracking = true);

    /// <summary>الملاحظات العامة النشطة للمنتسب مرتبة من الأحدث.</summary>
    Task<IReadOnlyList<MemberGeneralNote>> GetActiveNotesAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>صف الملاحظة العامة للمنتسب للتتبع والتحديث (صف واحد لكل منتسب).</summary>
    Task<MemberGeneralNote?> GetGeneralNoteForUpdateAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>يضيف صف ملاحظة عامة جديداً للمنتسب.</summary>
    void AddGeneralNote(MemberGeneralNote note);

    /// <summary>آخر عملية نقل للمنتسب إن وُجدت.</summary>
    Task<MemberTransfer?> GetLastTransferAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يحدّد ما إذا كان رفع <c>dataRevision</c> عند التعديل المادي يُنفَّذ عبر محفز PostgreSQL
    /// (<c>prepareMemberRevision</c>) بدلاً من طبقة التطبيق (InMemory وغير Npgsql).
    /// </summary>
    bool MaterialMemberRevisionManagedByDatabase();

    /// <summary>
    /// يعلّم المنتسبين كمصدَّرين للتجنيد بتحديث أعمدة التصدير فقط
    /// (<c>exportStatus</c> / <c>lastExportedRevision</c> / <c>lastExportedAt</c> / التدقيق الزمني)
    /// دون المساس ببقية حقول الملف، حتى لا يطلق PostgreSQL زيادة <c>dataRevision</c>.
    /// </summary>
    Task<int> MarkExportedForRecruitmentAsync(
        Guid centerId,
        IReadOnlyCollection<Guid> memberIds,
        string updatedByUserId,
        DateTimeOffset exportedAt,
        CancellationToken cancellationToken = default);
}
