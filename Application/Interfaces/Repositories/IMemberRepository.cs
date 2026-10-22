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
    /// </summary>
    Task<(long FileNumber, int QueueNumber)> AllocateFileAndQueueNumbersAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default);

    void Add(Member member);

    void AddIdentity(MemberCenterIdentity identity);
}
