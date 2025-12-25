using ArchiveProject.Application.Common.Interfaces.AttachmentInterfaces;
using ArchiveProject.Application.Common.Interfaces.FileStorageInterfaces;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using AutoMapper;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace ArchiveProject.Infrastructure.Services.AttachmentService
{
    public class AttachmentService : IAttachmentService
    {
        private readonly MainDbContext _context;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;
        public AttachmentService(MainDbContext context, IMapper mapper, IFileStorageService fileStorage, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
        {
            _context = context;
            _mapper = mapper;
            _fileStorage = fileStorage;
            _httpContextAccessor = httpContextAccessor;
            _env = env;
        }

        #region Upload
        public async Task<Result<AttachmentReadDto>> UploadAsync(
            long entityId,
            string entityType,
            IFormFile file,
            string? description = null,
            bool isProfilePhoto = false)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Result<AttachmentReadDto>.Failure(new[] { "الملف فارغ" });

                // Validate file
                if (!_fileStorage.ValidateFile(file, out string err))
                    return Result<AttachmentReadDto>.Failure(new[] { err });

                // Save physically
                var (path, size) = await _fileStorage.SaveFileAsync(file, entityType, entityId);

                // Create entity
                var entity = new Attachment
                {
                    AttachmentId = Guid.NewGuid(),
                    OwnerId = entityId,
                    OwnerType = entityType,
                    FileName = file.FileName,
                    FileExtension = Path.GetExtension(file.FileName)?.ToLower() ?? "",
                    FilePath = path,
                    FileSize = size,
                    ContentType = file.ContentType,
                    Description = description,
                    IsProfilePhoto = isProfilePhoto,
                    UploadedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    UpdatedBy = "System"
                };

                _context.Attachments.Add(entity);
                await _context.SaveChangesAsync();

                return Result<AttachmentReadDto>.Success(MapToDto(entity));
            }
            catch (Exception ex)
            {
                return Result<AttachmentReadDto>.Failure(new[] { $"خطأ أثناء الرفع: {ex.Message}" });
            }
        }
        #endregion

        #region GetById
        public async Task<Result<AttachmentReadDto>> GetByIdAsync(Guid id)
        {
            var entity = await _context.Attachments.FindAsync(id);
            if (entity == null)
                return Result<AttachmentReadDto>.Failure(new[] { "المرفق غير موجود" });

            return Result<AttachmentReadDto>.Success(MapToDto(entity));

        }
        #endregion

        #region Delete
        public async Task<Result<bool>> DeleteAsync(Guid id)
        {
            try
            {
                var entity = await _context.Attachments.FindAsync(id);
                if (entity == null)
                    return Result<bool>.Failure(new[] { "المرفق غير موجود" });

                // delete physical file
                await _fileStorage.DeleteFileAsync(entity.FilePath);

                // delete db record
                _context.Attachments.Remove(entity);
                await _context.SaveChangesAsync();

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure(new[] { $"خطأ أثناء حذف المرفق: {ex.Message}" });
            }
        }
        #endregion

        #region GetAttachmentsForEntity
        public async Task<Result<List<AttachmentReadDto>>> GetAttachmentsForEntityAsync(long entityId,params string[] entityTypes)
        {
            if (entityTypes == null || entityTypes.Length == 0)
                return Result<List<AttachmentReadDto>>.Failure(new[] { "نوع الكيان غير مُحدد" });

            var types = entityTypes.Select(t => t.ToLower()).ToList();

            var items = await _context.Attachments
                .Where(a => a.OwnerId == entityId && types.Contains(a.OwnerType.ToLower()))
                .OrderByDescending(a => a.UploadedAt)
                .ToListAsync();

            var result = items.Select(MapToDto).ToList();
            return Result<List<AttachmentReadDto>>.Success(result);

        }
        #endregion

        #region LinkToEntity
        public async Task<Result<string>> LinkToEntityAsync(Guid attachmentId, long entityId, string entityType)
        {
            var entity = await _context.Attachments.FindAsync(attachmentId);
            if (entity == null)
                return Result<string>.Failure(new[] { "المرفق غير موجود" });

            entity.OwnerId = entityId;
            entity.OwnerType = entityType;
            entity.UpdatedAt = DateTime.UtcNow;

            _context.Attachments.Update(entity);
            await _context.SaveChangesAsync();

            return Result<string>.Success("تم الربط بنجاح");
        }
        #endregion


        #region Download one File
        public async Task<Result<(byte[] Bytes, string ContentType, string FileName)>> DownloadAsync(Guid id)
        {
            var att = await _context.Attachments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AttachmentId == id);

            if (att == null)
                return Result<(byte[], string, string)>.Failure(new[] { "المرفق غير موجود" });

            var abs = _fileStorage.GetFullPath(att.FilePath ?? "");
            if (!File.Exists(abs))
                return Result<(byte[], string, string)>.Failure(new[] { "الملف الفيزيائي غير موجود على الخادم" });

            var bytes = await File.ReadAllBytesAsync(abs);
            var ct = string.IsNullOrWhiteSpace(att.ContentType) ? "application/octet-stream" : att.ContentType!;
            var name = string.IsNullOrWhiteSpace(att.FileName)
                ? $"{att.AttachmentId}{att.FileExtension}"
                : att.FileName!;

            return Result<(byte[], string, string)>.Success((bytes, ct, name));
        }
        #endregion

        #region Download ZIP for Entity Attachments
        public async Task<Result<(byte[] Bytes, string ZipName)>> DownloadZipForEntityAsync(long entityId, params string[] entityTypes)
        {
            if (entityTypes == null || entityTypes.Length == 0)
                return Result<(byte[], string)>.Failure(new[] { "نوع الكيان غير مُحدد" });

            var types = entityTypes.Select(t => t.ToLower()).ToList();

            var items = await _context.Attachments.AsNoTracking()
                .Where(a => a.OwnerId == entityId && types.Contains((a.OwnerType ?? "").ToLower()))
                .OrderByDescending(a => a.UploadedAt)
                .ToListAsync();

            if (items.Count == 0)
                return Result<(byte[], string)>.Failure(new[] { "لا يوجد مرفقات لهذه الوثيقة" });

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

                    safeName = $"{safeName}";

                    var entry = zip.CreateEntry(safeName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fs = File.OpenRead(abs);
                    await fs.CopyToAsync(entryStream);
                }
            }
            // اسم الوثيقة
            var documentTitle = await _context.Documents
                .AsNoTracking()
                .Where(d => d.DocumentId == entityId)
                .Select(d => d.Title)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(documentTitle))
                documentTitle = $"Document_{entityId}";

            var zipBytes = ms.ToArray();
            var safeTitle = SanitizeFileName(documentTitle);
            var zipName = $"{safeTitle}.zip";

            return Result<(byte[], string)>.Success((zipBytes, zipName));
        }
        #endregion



        // ⬆ MAP TO DTO
        private AttachmentReadDto MapToDto(Attachment attachment)
        {
            var dto = _mapper.Map<AttachmentReadDto>(attachment);

            dto.FileSizeFormatted = FormatFileSize(attachment.FileSize);

            // ✅ لا تكشف المسار الداخلي
            dto.FilePath = null;

            // ✅ رابط تحميل محمي عبر API
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


        // ⬆ FORMAT FILE SIZE
        private string FormatFileSize(long bytes)
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

        // ⬆ BUILD FILE URL
        private string BuildFileUrl(string relativePath)
        {
            try
            {
                var request = _httpContextAccessor.HttpContext?.Request;
                if (request == null)
                    return relativePath;

                return $"{request.Scheme}://{request.Host}/{relativePath}";
            }
            catch
            {
                return relativePath;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }

    }
}
