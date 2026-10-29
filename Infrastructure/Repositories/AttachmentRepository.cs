using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class AttachmentRepository : IAttachmentRepository
{
    private readonly AppDbContext _context;

    public AttachmentRepository(AppDbContext context) => _context = context;

    public void Add(FileAsset file) => _context.FileAssets.Add(file);
    public void Update(FileAsset file) => _context.FileAssets.Update(file);
    public void UpdateRange(IEnumerable<FileAsset> files) => _context.FileAssets.UpdateRange(files);
    public void Remove(FileAsset file) => _context.FileAssets.Remove(file);
    public void AddMedicalAttachment(MedicalAttachment attachment) => _context.MedicalAttachments.Add(attachment);

    public Task<FileAsset?> FindByIdAsync(Guid fileAssetId, CancellationToken cancellationToken = default)
        => _context.FileAssets.AsNoTracking().FirstOrDefaultAsync(a => a.FileAssetId == fileAssetId, cancellationToken);

    public async Task<FileAsset?> GetTrackedByIdAsync(Guid fileAssetId, CancellationToken cancellationToken = default)
        => await _context.FileAssets.FirstOrDefaultAsync(a => a.FileAssetId == fileAssetId, cancellationToken);

    public Task<List<FileAsset>> GetByIdsAsync(IEnumerable<Guid> fileAssetIds, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var ids = fileAssetIds.ToList();
        var query = _context.FileAssets.Where(a => ids.Contains(a.FileAssetId));
        if (asNoTracking) query = query.AsNoTracking();
        return query.ToListAsync(cancellationToken);
    }

    public Task<FileLinkTarget?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
        => _context.Members.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Select(m => new FileLinkTarget(m.MemberId, m.MemberId, m.CurrentMedicalCenterId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<FileLinkTarget?> FindConsultationAsync(Guid consultationId, CancellationToken cancellationToken = default)
        => _context.Consultations.AsNoTracking()
            .Where(c => c.ConsultationId == consultationId)
            .Select(c => new FileLinkTarget(c.ConsultationId, c.MemberId, c.CenterId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<FileLinkTarget?> FindInvestigationAsync(Guid investigationId, CancellationToken cancellationToken = default)
        => _context.Investigations.AsNoTracking()
            .Where(i => i.InvestigationId == investigationId)
            .Select(i => new FileLinkTarget(i.InvestigationId, i.MemberId, i.CenterId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<UserProfile?> FindTrackedProfileAsync(string userId, CancellationToken cancellationToken = default)
        => _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public void AddProfile(UserProfile profile) => _context.UserProfiles.Add(profile);

    public Task<Guid?> FindActiveAssignmentCenterIdAsync(string userId, CancellationToken cancellationToken = default)
        => _context.UserCenters.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Center.IsActive)
            .OrderBy(x => x.AssignedAt)
            .Select(x => (Guid?)x.CenterId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> FindFirstActiveCenterIdAsync(CancellationToken cancellationToken = default)
        => _context.MedicalCenters.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => (Guid?)x.CenterId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<FileAsset>> ListMemberFilesAsync(Guid memberId, Guid centerId, CancellationToken cancellationToken = default)
        => ListLinkedAsync(a => a.MemberId == memberId && a.CenterId == centerId, cancellationToken);

    public Task<List<FileAsset>> ListConsultationFilesAsync(Guid consultationId, Guid centerId, CancellationToken cancellationToken = default)
        => ListLinkedAsync(a => a.ConsultationId == consultationId && a.CenterId == centerId, cancellationToken);

    public Task<List<FileAsset>> ListInvestigationFilesAsync(Guid investigationId, Guid centerId, CancellationToken cancellationToken = default)
        => ListLinkedAsync(a => a.InvestigationId == investigationId && a.CenterId == centerId, cancellationToken);

    public async Task<List<MedicalRequestFileLink>> ListFilesForMedicalRequestsAsync(
        Guid memberId,
        Guid centerId,
        IEnumerable<Guid> consultationIds,
        IEnumerable<Guid> investigationIds,
        CancellationToken cancellationToken = default)
    {
        var consultationIdSet = consultationIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var investigationIdSet = investigationIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (consultationIdSet.Count == 0 && investigationIdSet.Count == 0)
            return new List<MedicalRequestFileLink>();

        return await _context.MedicalAttachments.AsNoTracking()
            .Where(a => a.MemberId == memberId && a.CenterId == centerId)
            .Where(a =>
                (a.ConsultationId != null && consultationIdSet.Contains(a.ConsultationId.Value))
                || (a.InvestigationId != null && investigationIdSet.Contains(a.InvestigationId.Value)))
            .Join(
                _context.FileAssets.AsNoTracking().Where(f => !f.IsDeleted),
                a => a.FileAssetId,
                f => f.FileAssetId,
                (a, f) => new MedicalRequestFileLink(a.ConsultationId, a.InvestigationId, f))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasMedicalLinkAsync(Guid fileAssetId, CancellationToken cancellationToken = default)
        => _context.MedicalAttachments.AnyAsync(a => a.FileAssetId == fileAssetId, cancellationToken);

    public Task<bool> IsProfilePhotoAsync(string userId, Guid fileAssetId, CancellationToken cancellationToken = default)
        => _context.UserProfiles.AnyAsync(p => p.UserId == userId && p.ProfileFileAssetId == fileAssetId, cancellationToken);

    public async Task ClearProfilePhotoAsync(Guid fileAssetId, CancellationToken cancellationToken = default)
    {
        var profiles = await _context.UserProfiles.Where(p => p.ProfileFileAssetId == fileAssetId).ToListAsync(cancellationToken);
        foreach (var profile in profiles)
            profile.ProfileFileAssetId = null;
    }

    private Task<List<FileAsset>> ListLinkedAsync(
        System.Linq.Expressions.Expression<Func<MedicalAttachment, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return _context.MedicalAttachments.AsNoTracking()
            .Where(predicate)
            .Join(_context.FileAssets.AsNoTracking(), a => a.FileAssetId, f => f.FileAssetId, (a, f) => f)
            .ToListAsync(cancellationToken);
    }
}
