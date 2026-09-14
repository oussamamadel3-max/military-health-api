using Application.Common.Results;
using Application.Dtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using MapsterMapper;
using Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;

namespace Application.Services.AttachmentService
{
    public class AttachmentService : IAttachmentService
    {
        private readonly IAttachmentRepository _attachments;
        private readonly IDocumentRepository _documents;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly ITextInputSanitizer _textSanitizer;

        public AttachmentService(
            IAttachmentRepository attachments,
            IDocumentRepository documents,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IFileStorageService fileStorage,
            IHttpContextAccessor httpContextAccessor,
            IWebHostEnvironment env,
            ITextInputSanitizer textSanitizer)
        {
            _attachments = attachments;
            _documents = documents;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _fileStorage = fileStorage;
            _httpContextAccessor = httpContextAccessor;
            _env = env;
            _textSanitizer = textSanitizer;
        }

        #region Upload
        public async Task<AttachmentReadDto> UploadAsync(
            long entityId,
            string entityType,
            IFormFile file,
            string? description = null,
            bool isProfilePhoto = false)
        {
            if (file == null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ.");

            if (!_fileStorage.ValidateFile(file, out string err))
                throw new InvalidUserDataException(err);

            var (path, size) = await _fileStorage.SaveFileAsync(file, entityType, entityId);

            var entity = new Attachment
            {
                AttachmentId = Guid.NewGuid(),
                OwnerId = entityId,
                OwnerType = entityType,
                FileName = SanitizeDisplayFileName(file.FileName),
                FileExtension = Path.GetExtension(file.FileName)?.ToLower() ?? "",
                FilePath = path,
                FileSize = size,
                ContentType = file.ContentType,
                Description = _textSanitizer.SanitizePlainText(description),
                IsProfilePhoto = isProfilePhoto,
                UploadedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                UpdatedBy = "System"
            };

            _attachments.Add(entity);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(entity);
        }
        #endregion

        #region GetById
        public async Task<AttachmentReadDto> GetByIdAsync(Guid id)
        {
            var entity = await _attachments.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");

            return MapToDto(entity);
        }
        #endregion

        #region Delete
        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _attachments.GetTrackedByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");

            await _fileStorage.DeleteFileAsync(entity.FilePath);

            _attachments.Remove(entity);
            await _unitOfWork.SaveChangesAsync();

            return true;
        }
        #endregion

        #region GetAttachmentsForEntity
        public async Task<List<AttachmentReadDto>> GetAttachmentsForEntityAsync(long entityId, params string[] entityTypes)
        {
            if (entityTypes == null || entityTypes.Length == 0)
                throw new InvalidUserDataException("نوع الكيان غير مُحدد.");

            var types = entityTypes.Select(t => t.ToLower()).ToList();

            var items = (await _attachments.GetByOwnerAsync(entityId, entityTypes))
                .Where(a => types.Contains(a.OwnerType.ToLower()))
                .OrderByDescending(a => a.UploadedAt)
                .ToList();

            return items.Select(MapToDto).ToList();
        }
        #endregion

        #region LinkToEntity
        public async Task LinkToEntityAsync(Guid attachmentId, long entityId, string entityType)
        {
            var entity = await _attachments.GetTrackedByIdAsync(attachmentId)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");

            entity.OwnerId = entityId;
            entity.OwnerType = entityType;
            entity.UpdatedAt = DateTime.UtcNow;

            _attachments.Update(entity);
            await _unitOfWork.SaveChangesAsync();
        }
        #endregion

        #region Download one File
        public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAsync(Guid id)
        {
            var att = await _attachments.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");

            var abs = _fileStorage.GetFullPath(att.FilePath ?? "");
            if (!File.Exists(abs))
                throw new RoleNotFoundException("الملف الفيزيائي غير موجود على الخادم.");

            var bytes = await File.ReadAllBytesAsync(abs);
            var ct = string.IsNullOrWhiteSpace(att.ContentType) ? "application/octet-stream" : att.ContentType!;
            var name = string.IsNullOrWhiteSpace(att.FileName)
                ? $"{att.AttachmentId}{att.FileExtension}"
                : att.FileName!;

            return (bytes, ct, name);
        }
        #endregion

        #region Download ZIP for Entity Attachments
        public async Task<(byte[] Bytes, string ZipName)> DownloadZipForEntityAsync(long entityId, params string[] entityTypes)
        {
            if (entityTypes == null || entityTypes.Length == 0)
                throw new InvalidUserDataException("نوع الكيان غير مُحدد.");

            var types = entityTypes.Select(t => t.ToLower()).ToList();

            var items = (await _attachments.GetByOwnerAsync(entityId, entityTypes))
                .Where(a => types.Contains((a.OwnerType ?? "").ToLower()))
                .OrderByDescending(a => a.UploadedAt)
                .ToList();

            if (items.Count == 0)
                throw new RoleNotFoundException("لا يوجد مرفقات لهذه الوثيقة.");

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var att in items)
                {
                    var abs = _fileStorage.GetFullPath(att.FilePath ?? "");
                    if (!File.Exists(abs))
                        continue;

                    var safeName = string.IsNullOrWhiteSpace(att.FileName)
                        ? $"{att.AttachmentId}{att.FileExtension}"
                        : att.FileName;

                    var entry = zip.CreateEntry(safeName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fs = File.OpenRead(abs);
                    await fs.CopyToAsync(entryStream);
                }
            }

            var documentTitle = await _documents.GetTitleAsync(entityId);
            if (string.IsNullOrWhiteSpace(documentTitle))
                documentTitle = $"Document_{entityId}";

            var zipBytes = ms.ToArray();
            var safeTitle = SanitizeFileName(documentTitle);
            var zipName = $"{safeTitle}.zip";

            return (zipBytes, zipName);
        }
        #endregion

        #region Helpers

        private AttachmentReadDto MapToDto(Attachment attachment)
        {
            var dto = _mapper.Map<AttachmentReadDto>(attachment);
            dto.FileSizeFormatted = FormatFileSize(attachment.FileSize);
            dto.FilePath = null;
            dto.FileUrl = BuildProtectedDownloadUrl(attachment.AttachmentId);
            return dto;
        }

        private string BuildProtectedDownloadUrl(Guid id)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request == null)
                return $"/api/attachments/download/{id}";

            return $"{request.Scheme}://{request.Host}/api/attachments/download/{id}";
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }

        private static string SanitizeDisplayFileName(string name)
        {
            name = Path.GetFileName(name ?? string.Empty);
            name = name.Trim();

            if (string.IsNullOrWhiteSpace(name)) return "file";
            if (name.Length > 200) name = name[..200];

            return name;
        }

        #endregion
    }
}
