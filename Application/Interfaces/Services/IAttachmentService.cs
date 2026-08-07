using Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services
{
    public interface IAttachmentService
    {
        Task<AttachmentReadDto> UploadAsync(
            long entityId,
            string entityType,
            IFormFile file,
            string? description = null,
            bool isProfilePhoto = false);

        Task<AttachmentReadDto> GetByIdAsync(Guid id);
        Task<bool> DeleteAsync(Guid id);
        Task LinkToEntityAsync(Guid attachmentId, long entityId, string entityType);
        Task<List<AttachmentReadDto>> GetAttachmentsForEntityAsync(long entityId, params string[] entityTypes);
        Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAsync(Guid id);
        Task<(byte[] Bytes, string ZipName)> DownloadZipForEntityAsync(long entityId, params string[] entityTypes);
    }
}
