using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AttachmentRepository : IAttachmentRepository
{
    private readonly AppDbContext _context;

    public AttachmentRepository(AppDbContext context) => _context = context;

    public void Add(Attachment attachment) => _context.Attachments.Add(attachment);
    public void Update(Attachment attachment) => _context.Attachments.Update(attachment);
    public void UpdateRange(IEnumerable<Attachment> attachments) => _context.Attachments.UpdateRange(attachments);
    public void Remove(Attachment attachment) => _context.Attachments.Remove(attachment);

    public Task<Attachment?> FindByIdAsync(Guid attachmentId, CancellationToken cancellationToken = default)
        => _context.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, cancellationToken);

    public async Task<Attachment?> GetTrackedByIdAsync(Guid attachmentId, CancellationToken cancellationToken = default)
        => await _context.Attachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, cancellationToken);

    public async Task<List<Attachment>> GetByOwnerAsync(long ownerId, IEnumerable<string> ownerTypes, bool asNoTracking = true, CancellationToken cancellationToken = default)
    {
        var types = ownerTypes.ToList();
        var query = _context.Attachments.Where(a => a.OwnerId == ownerId && types.Contains(a.OwnerType));
        if (asNoTracking) query = query.AsNoTracking();
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetIdsByDocumentAsync(long documentId, CancellationToken cancellationToken = default)
        => await _context.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == "Document" && a.OwnerId == documentId)
            .Select(a => a.AttachmentId)
            .ToListAsync(cancellationToken);

    public async Task<List<Attachment>> GetByIdsAsync(IEnumerable<Guid> attachmentIds, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var ids = attachmentIds.ToList();
        var query = _context.Attachments.Where(a => ids.Contains(a.AttachmentId));
        if (asNoTracking) query = query.AsNoTracking();
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<long> SumFileSizeForScopedDocumentsAsync(ActorScope scope, DateTime? createdFrom, DateTime? createdToExclusive, string ownerType = "Document", CancellationToken cancellationToken = default)
    {
        var docs = _context.Documents.AsNoTracking();
        if (!scope.IsSuperAdmin && !scope.IsBranchManager)
            docs = docs.Where(d => d.UserId == scope.UserId);
        if (createdFrom.HasValue)
            docs = docs.Where(d => d.CreatedAt >= createdFrom.Value);
        if (createdToExclusive.HasValue)
            docs = docs.Where(d => d.CreatedAt < createdToExclusive.Value);

        return await _context.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType)
            .Join(docs, a => a.OwnerId, d => d.DocumentId, (a, _) => a.FileSize)
            .SumAsync(size => (long?)size, cancellationToken) ?? 0;
    }
}
