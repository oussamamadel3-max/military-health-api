using Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services
{
    public interface IAttachmentService
    {
        Task<AttachmentReadDto> UploadAsync(string entityId, string entityType, IFormFile file, string? description = null);
        Task<AttachmentReadDto> GetByIdAsync(Guid id);
        Task<bool> DeleteAsync(Guid id);
        Task LinkToEntityAsync(Guid attachmentId, string entityId, string entityType);
        Task<List<AttachmentReadDto>> GetAttachmentsForEntityAsync(string entityId, params string[] entityTypes);
        Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAsync(Guid id);
        Task<(byte[] Bytes, string ZipName)> DownloadZipForEntityAsync(string entityId, params string[] entityTypes);
    }
}
