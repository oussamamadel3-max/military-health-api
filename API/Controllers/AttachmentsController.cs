using API.Infrastructure;
using API.Validation;
using Application.Dtos;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentService _service;

        public AttachmentsController(IAttachmentService service)
        {
            _service = service;
        }

        // Upload file
        [HttpPost("upload/{entityType}/{entityId}")]
        public async Task<IResult> Upload(string entityType, string entityId, IFormFile file, string? description = null)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            if (file is null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ أو غير محدد.");

            var result = await _service.UploadAsync(entityId, entityType, file, description);
            return ApiResponse.Success("تم رفع المرفق بنجاح.", result);
        }

        [HttpGet("{entityType}/{entityId}")]
        public async Task<IResult> GetByEntity(string entityType, string entityId)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            var result = await _service.GetAttachmentsForEntityAsync(entityId, entityType);
            return ApiResponse.Success("تم جلب المرفقات بنجاح.", result);
        }

        // Get by ID
        [HttpGet("{id:Guid}")]
        public async Task<IResult> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            return ApiResponse.Success("تم جلب المرفق بنجاح.", result);
        }

        // Delete
        [HttpDelete("{id:Guid}")]
        public async Task<IResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return ApiResponse.Success("تم حذف المرفق بنجاح.");
        }

        // Link to entity
        [HttpPost("link/{attachmentId:Guid}/{entityId}/{entityType}")]
        public async Task<IResult> Link(Guid attachmentId, string entityId, string entityType)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            await _service.LinkToEntityAsync(attachmentId, entityId, entityType);
            return ApiResponse.Success("تم ربط المرفق بنجاح.");
        }

        // تنزيل ملف واحد
        [HttpGet("download/{id:guid}")]
        public async Task<IActionResult> Download(Guid id)
        {
            var res = await _service.DownloadAsync(id);
            // الصور تُعرض inline داخل <img>؛ باقي الملفات تُنزَّل بالاسم.
            if (!string.IsNullOrWhiteSpace(res.ContentType) &&
                res.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return File(res.Bytes, res.ContentType);
            }

            return File(res.Bytes, res.ContentType, res.FileName);
        }

        // تنزيل كل المرفقات كـ Zip لوثيقة
        [HttpGet("download-zip/{entityType}/{entityId}")]
        public async Task<IActionResult> DownloadZip(string entityType, string entityId)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            var res = await _service.DownloadZipForEntityAsync(entityId, entityType);
            return File(res.Bytes, "application/zip", res.ZipName);
        }
    }
}
