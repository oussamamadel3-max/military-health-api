using Domain.Entities.Archive;

namespace Application.Interfaces.Repositories;

/// <summary>
/// وصول قراءة/كتابة لسجل أرشيف المنتسب بعد القرار النهائي (FR-069 / BR-017).
/// </summary>
public interface IMemberArchiveRepository
{
    IQueryable<MemberArchive> Query(bool asNoTracking = true);

    Task<MemberArchive?> GetByFinalDecisionIdAsync(
        Guid finalDecisionId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    Task<MemberArchive?> GetByMemberIdAsync(
        Guid memberId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يخصص رقماً أرشيفياً فريداً يحمل رمز مركز صدور القرار.
    /// يجب استدعاؤه داخل معاملة مفتوحة؛ على PostgreSQL يُقفل التسلسل حتى نهاية المعاملة.
    /// </summary>
    Task<string> AllocateArchiveNumberAsync(
        Guid centerId,
        string centerCode,
        CancellationToken cancellationToken = default);

    void Add(MemberArchive archive);
}
