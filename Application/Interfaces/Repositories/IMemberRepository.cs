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
}
