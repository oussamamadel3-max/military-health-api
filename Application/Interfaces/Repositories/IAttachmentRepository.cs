using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAttachmentRepository
{
    void Add(Attachment attachment);
    void Update(Attachment attachment);
    void UpdateRange(IEnumerable<Attachment> attachments);
    void Remove(Attachment attachment);
    Task<Attachment?> FindByIdAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task<Attachment?> GetTrackedByIdAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task<List<Attachment>> GetByOwnerAsync(long ownerId, IEnumerable<string> ownerTypes, bool asNoTracking = true, CancellationToken cancellationToken = default);
    Task<List<Attachment>> GetByIdsAsync(IEnumerable<Guid> attachmentIds, bool asNoTracking = false, CancellationToken cancellationToken = default);
}
