using API.Validation;
using Application.Interfaces.Services;
using Application.Dtos;
using Microsoft.AspNetCore.Authorization;
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
        public async Task<Result<AttachmentReadDto>> Upload(string entityType,long entityId,IFormFile file,string? description = null)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                return Result<AttachmentReadDto>.Failure(new[] { "نوع الكيان غير صالح." });
            if (file is null || file.Length == 0)
                return Result<AttachmentReadDto>.Failure(new[] { "الملف فارغ أو غير محدد." });

            return await _service.UploadAsync(entityId, entityType, file, description, false);
        }


        // Upload multiple temp attachments for Document (OwnerId = 0)
        [HttpPost("upload-temp/document")]
        [Consumes("multipart/form-data")]
        public async Task<Result<List<AttachmentReadDto>>> UploadTempForDocument([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0 || files.All(f => f == null || f.Length == 0))
                return Result<List<AttachmentReadDto>>.Failure(new[] { "لم يتم تحديد ملفات" });

            var results = new List<AttachmentReadDto>();

            foreach (var file in files.Where(f => f?.Length > 0))
            {
                // نرفع مع OwnerId = 0 (غير مربوط)، ونضبط OwnerType = "Document"
                var up = await _service.UploadAsync(
                    entityId: 0,              // مؤقتًا غير مربوط
                    entityType: "Document",
                    file: file,
                    description: null,
                    isProfilePhoto: false
                );

                if (!up.Succeeded || up.Value == null)
                    return Result<List<AttachmentReadDto>>.Failure(up.Errors);

                results.Add(up.Value);
            }

            return Result<List<AttachmentReadDto>>.Success(results);
        }

        [HttpPost("delete-temp")]
        public async Task<Result<string>> DeleteTemp([FromBody][MinLength(1, ErrorMessage = "لم يتم تحديد مرفقات.")] List<Guid> attachmentIds)
        {
            foreach (var id in attachmentIds)
                await _service.DeleteAsync(id);

            return Result<string>.Success("تم حذف المرفقات المؤقتة.");
        }


        // Upload profile photo
        [HttpPost("upload-profile/{entityType}/{entityId:long}")]
        public async Task<Result<AttachmentReadDto>> UploadProfile(string entityType,long entityId,IFormFile file)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                return Result<AttachmentReadDto>.Failure(new[] { "نوع الكيان غير صالح." });
            if (file is null || file.Length == 0)
                return Result<AttachmentReadDto>.Failure(new[] { "الملف فارغ أو غير محدد." });

            return await _service.UploadAsync(entityId, entityType, file, null, true);
        }


        // Get attachments for entity
        [HttpGet("{entityType}/{entityId:long}")]
        public async Task<Result<List<AttachmentReadDto>>> GetByEntity(string entityType, long entityId)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                return Result<List<AttachmentReadDto>>.Failure(new[] { "نوع الكيان غير صالح." });

            return await _service.GetAttachmentsForEntityAsync(entityId, entityType);
        }


        // Get by ID
        [HttpGet("{id:Guid}")]
        public async Task<Result<AttachmentReadDto>> GetById(Guid id)
        {
            return await _service.GetByIdAsync(id);
        }


        // Delete
        [HttpDelete("{id:Guid}")]
        public async Task<Result<bool>> Delete(Guid id)
        {
            return await _service.DeleteAsync(id);
        }

        // Link to entity
        [HttpPost("link/{attachmentId:Guid}/{entityId:long}/{entityType}")]
        public async Task<Result<string>> Link(Guid attachmentId, long entityId, string entityType)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                return Result<string>.Failure(new[] { "نوع الكيان غير صالح." });

            return await _service.LinkToEntityAsync(attachmentId, entityId, entityType);
        }

        // تنزيل ملف واحد
        [HttpGet("download/{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Download(Guid id)
        {
            var res = await _service.DownloadAsync(id);
            if (!res.Succeeded)
                return BadRequest(res);

            return File(res.Value.Bytes, res.Value.ContentType, res.Value.FileName);
        }


        // تنزيل كل المرفقات كـ Zip لوثيقة
        [HttpGet("download-zip/{entityType}/{entityId:long}")]
        [Authorize]
        public async Task<IActionResult> DownloadZip(string entityType, long entityId)
        {
            if (!AttachmentRouteConstraints.IsValidEntityType(entityType))
                return BadRequest(new { message = "نوع الكيان غير صالح." });

            var res = await _service.DownloadZipForEntityAsync(entityId, entityType);
            if (!res.Succeeded)
                return BadRequest(res);

            return File(res.Value.Bytes, "application/zip", res.Value.ZipName);
        }


    }

}
