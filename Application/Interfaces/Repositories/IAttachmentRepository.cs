using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;

namespace Application.Interfaces.Repositories;

public sealed record FileLinkTarget(Guid Id, Guid MemberId, Guid CenterId);

public interface IAttachmentRepository
{
    void Add(FileAsset file);
    void Update(FileAsset file);
    void UpdateRange(IEnumerable<FileAsset> files);
    void Remove(FileAsset file);
    void AddMedicalAttachment(MedicalAttachment attachment);
    Task<FileAsset?> FindByIdAsync(Guid fileAssetId, CancellationToken cancellationToken = default);
    Task<FileAsset?> GetTrackedByIdAsync(Guid fileAssetId, CancellationToken cancellationToken = default);
    Task<List<FileAsset>> GetByIdsAsync(IEnumerable<Guid> fileAssetIds, bool asNoTracking = false, CancellationToken cancellationToken = default);
    Task<FileLinkTarget?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<FileLinkTarget?> FindConsultationAsync(Guid consultationId, CancellationToken cancellationToken = default);
    Task<FileLinkTarget?> FindInvestigationAsync(Guid investigationId, CancellationToken cancellationToken = default);
    Task<UserProfile?> FindTrackedProfileAsync(string userId, CancellationToken cancellationToken = default);
    void AddProfile(UserProfile profile);
    Task<Guid?> FindActiveAssignmentCenterIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<Guid?> FindFirstActiveCenterIdAsync(CancellationToken cancellationToken = default);
    Task<List<FileAsset>> ListMemberFilesAsync(Guid memberId, Guid centerId, CancellationToken cancellationToken = default);
    Task<List<FileAsset>> ListConsultationFilesAsync(Guid consultationId, Guid centerId, CancellationToken cancellationToken = default);
    Task<List<FileAsset>> ListInvestigationFilesAsync(Guid investigationId, Guid centerId, CancellationToken cancellationToken = default);
    Task<bool> HasMedicalLinkAsync(Guid fileAssetId, CancellationToken cancellationToken = default);
    Task<bool> IsProfilePhotoAsync(string userId, Guid fileAssetId, CancellationToken cancellationToken = default);
    Task ClearProfilePhotoAsync(Guid fileAssetId, CancellationToken cancellationToken = default);
}
