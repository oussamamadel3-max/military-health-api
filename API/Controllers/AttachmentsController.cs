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
        [HttpPost("upload/{entityType}/{entityId:long}")]
        public async Task<IResult> Upload(string entityType, long entityId, IFormFile file, string? description = null)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            if (file is null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ أو غير محدد.");

            var result = await _service.UploadAsync(entityId, entityType, file, description, false);
            return ApiResponse.Success("تم رفع المرفق بنجاح.", result);
        }

        // Upload multiple temp attachments for Document (OwnerId = 0)
        [HttpPost("upload-temp/document")]
        [Consumes("multipart/form-data")]
        public async Task<IResult> UploadTempForDocument([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0 || files.All(f => f == null || f.Length == 0))
                throw new InvalidUserDataException("لم يتم تحديد ملفات.");

            var results = new List<AttachmentReadDto>();

            foreach (var file in files.Where(f => f?.Length > 0))
            {
                var up = await _service.UploadAsync(
                    entityId: 0,
                    entityType: "Document",
                    file: file,
                    description: null,
                    isProfilePhoto: false
                );
                results.Add(up);
            }

            return ApiResponse.Success("تم رفع المرفقات المؤقتة بنجاح.", results);
        }

        [HttpPost("delete-temp")]
        public async Task<IResult> DeleteTemp([FromBody][MinLength(1, ErrorMessage = "لم يتم تحديد مرفقات.")] List<Guid> attachmentIds)
        {
            foreach (var id in attachmentIds)
                await _service.DeleteAsync(id);

            return ApiResponse.Success("تم حذف المرفقات المؤقتة.");
        }

        // Upload profile photo
        [HttpPost("upload-profile/{entityType}/{entityId:long}")]
        public async Task<IResult> UploadProfile(string entityType, long entityId, IFormFile file)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            if (file is null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ أو غير محدد.");

            var result = await _service.UploadAsync(entityId, entityType, file, null, true);
            return ApiResponse.Success("تم رفع صورة الملف الشخصي بنجاح.", result);
        }

        // Get attachments for entity
        [HttpGet("{entityType}/{entityId:long}")]
        public async Task<IResult> GetByEntity(string entityType, long entityId)
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
        [HttpPost("link/{attachmentId:Guid}/{entityId:long}/{entityType}")]
        public async Task<IResult> Link(Guid attachmentId, long entityId, string entityType)
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
            return File(res.Bytes, res.ContentType, res.FileName);
        }

        // تنزيل كل المرفقات كـ Zip لوثيقة
        [HttpGet("download-zip/{entityType}/{entityId:long}")]
        public async Task<IActionResult> DownloadZip(string entityType, long entityId)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                throw new InvalidUserDataException("نوع الكيان غير صالح.");

            var res = await _service.DownloadZipForEntityAsync(entityId, entityType);
            return File(res.Bytes, "application/zip", res.ZipName);
        }
    }
}
