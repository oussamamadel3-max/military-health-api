using Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services
{
    public interface IAttachmentService
    {
        // 📌 رفع مرفق جديد
        Task<Result<AttachmentReadDto>> UploadAsync(
            long entityId,
            string entityType,
            IFormFile file,
            string? description = null,
            bool isProfilePhoto = false);

        // 📌 جلب مرفق حسب ID
        Task<Result<AttachmentReadDto>> GetByIdAsync(Guid id);

        // 📌 حذف مرفق (قاعدة بيانات + ملف فيزيائي)
        Task<Result<bool>> DeleteAsync(Guid id);

        // 📌 ربط مرفقات بكيان معيّن
        Task<Result<string>> LinkToEntityAsync(Guid attachmentId, long entityId, string entityType);

        // 📌 جلب جميع مرفقات كيان معيّن
        Task<Result<List<AttachmentReadDto>>> GetAttachmentsForEntityAsync(long entityId,params string[] entityTypes);

        // تنزيل ملف واحد
        Task<Result<(byte[] Bytes, string ContentType, string FileName)>> DownloadAsync(Guid id);

        // تنزيل كل مرفقات كيان كملف Zip
        Task<Result<(byte[] Bytes, string ZipName)>> DownloadZipForEntityAsync(long entityId, params string[] entityTypes);
    }
}
