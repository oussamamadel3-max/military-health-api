using ArchiveProject.Application.Common.Interfaces.FileStorageInterfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ArchiveProject.Infrastructure.Services.FileStorageService
{
    public class FileStorageService : IFileStorageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly string _uploadPath;
        private readonly long _maxFileSizeInBytes;
        private readonly string[] _allowedExtensions;

        public FileStorageService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _environment = environment;
            _configuration = configuration;

            // Read from configuration or use defaults
            _uploadPath = configuration["FileStorage:UploadPath"] ?? "uploads";
            _maxFileSizeInBytes = (configuration.GetValue<int?>("FileStorage:MaxFileSizeInMB") ?? 50) * 1024 * 1024;
            _allowedExtensions = configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
                ?? new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip", ".rar", ".csv", ".svg" };
        }

        public async Task<(string FilePath, long FileSize)> SaveFileAsync(IFormFile file, string entityType, long entityId)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("الملف فارغ");

            // Validate file
            if (!ValidateFile(file, out string errorMessage))
                throw new InvalidOperationException(errorMessage);

            // Create directory structure: uploads/{entityType}/{year}/{month}/
            var year = DateTime.Now.Year.ToString();
            var month = DateTime.Now.Month.ToString("00");
            var relativePath = Path.Combine(_uploadPath, entityType, year, month);
            var fullPath = Path.Combine(_environment.ContentRootPath, relativePath);

            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            // Generate unique filename: {guid}_{originalname}
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}{fileExtension}";
            var filePath = Path.Combine(fullPath, uniqueFileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Return relative path for database storage
            var relativeFilePath = Path.Combine(relativePath, uniqueFileName).Replace("\\", "/");

            return (relativeFilePath, file.Length);
        }

        public Task<bool> DeleteFileAsync(string filePath)
        {
            try
            {
                var fullPath = GetFullPath(filePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public bool ValidateFile(IFormFile file, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (file == null || file.Length == 0)
            {
                errorMessage = "الملف فارغ";
                return false;
            }

            // Check file size
            if (file.Length > _maxFileSizeInBytes)
            {
                var maxSizeMB = _maxFileSizeInBytes / 1024 / 1024;
                errorMessage = $"حجم الملف يتجاوز الحد المسموح ({maxSizeMB} MB)";
                return false;
            }

            // Check extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
            {
                errorMessage = $"امتداد الملف غير مسموح. الامتدادات المسموحة: {string.Join(", ", _allowedExtensions)}";
                return false;
            }

            return true;
        }

        //public string GetFullPath(string relativePath)
        //{
        //    return Path.Combine(_environment.ContentRootPath, relativePath.Replace("/", "\\"));
        //}

        public string GetFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return _environment.ContentRootPath;

            // خلي الفواصل حسب نظام التشغيل
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                       .Replace('\\', Path.DirectorySeparatorChar);

            return Path.Combine(_environment.ContentRootPath, relativePath);
        }

    }

}
