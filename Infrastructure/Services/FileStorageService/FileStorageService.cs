using Application.Interfaces.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace Infrastructure.Services.FileStorageService
{
    public class FileStorageService : IFileStorageService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly string _rootPath;        // storage (خارج wwwroot)
        private readonly string _uploadPath;       // uploads
        private readonly long _maxFileSizeInBytes;
        private readonly HashSet<string> _allowedExtensions;
        private readonly bool _allowArchives;

        private static readonly HashSet<string> _riskySecondExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".ps1", ".js", ".vbs", ".msi", ".com", ".scr", ".jar", ".sh"
        };

        public FileStorageService(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _environment = environment;

            _rootPath = configuration["FileStorage:RootPath"] ?? "storage";
            _uploadPath = configuration["FileStorage:UploadPath"] ?? "uploads";
            _allowArchives = configuration.GetValue<bool?>("FileStorage:AllowArchives") ?? true;

            _maxFileSizeInBytes = (configuration.GetValue<int?>("FileStorage:MaxFileSizeInMB") ?? 50) * 1024 * 1024;

            var exts = configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
                ?? new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip", ".rar", ".csv", ".svg" };

            _allowedExtensions = new HashSet<string>(
                exts.Select(e => (e ?? "").Trim().ToLowerInvariant()).Where(e => !string.IsNullOrWhiteSpace(e)),
                StringComparer.OrdinalIgnoreCase
            );
        }

        public async Task<(string FilePath, long FileSize)> SaveFileAsync(IFormFile file, string entityType, long entityId)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("الملف فارغ");

            if (!ValidateFile(file, out var errorMessage))
                throw new InvalidOperationException(errorMessage);

            var safeEntityType = SanitizeFolderName(entityType);

            // storage/uploads/{entityType}/{year}/{month}/
            var year = DateTime.UtcNow.Year.ToString();
            var month = DateTime.UtcNow.Month.ToString("00");

            var relativeDir = Path.Combine(_rootPath, _uploadPath, safeEntityType, year, month);
            var fullDir = Path.Combine(_environment.ContentRootPath, relativeDir);
            Directory.CreateDirectory(fullDir);

            // ✅ اسم آمن (لا نستخدم اسم الملف الأصلي)
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var uniqueFileName = $"{Guid.NewGuid():N}{extension}";

            var fullFilePath = Path.Combine(fullDir, uniqueFileName);

            await using (var stream = new FileStream(fullFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            var relativeFilePath = Path.Combine(relativeDir, uniqueFileName).Replace("\\", "/");
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

            if (file.Length > _maxFileSizeInBytes)
            {
                var maxSizeMB = _maxFileSizeInBytes / 1024 / 1024;
                errorMessage = $"حجم الملف يتجاوز الحد المسموح ({maxSizeMB} MB)";
                return false;
            }

            var originalName = Path.GetFileName(file.FileName ?? string.Empty);
            var extension = Path.GetExtension(originalName).ToLowerInvariant();

            if (!_allowedExtensions.Contains(extension))
            {
                errorMessage = $"امتداد الملف غير مسموح. الامتدادات المسموحة: {string.Join(", ", _allowedExtensions)}";
                return false;
            }

            if (!_allowArchives && (extension == ".zip" || extension == ".rar"))
            {
                errorMessage = "رفع ملفات الأرشيف غير مسموح حالياً.";
                return false;
            }

            // ✅ منع الامتدادين مثل test.pdf.pdf و test.jpg.exe
            if (HasDoubleExtension(originalName))
            {
                var secondExt = GetSecondExtension(originalName);
                if (secondExt != null && _riskySecondExtensions.Contains(secondExt))
                {
                    errorMessage = "اسم الملف يحتوي على امتدادين غير مسموح (امتداد خطير).";
                    return false;
                }

                errorMessage = "اسم الملف يحتوي على امتدادين (مثل test.pdf.pdf) وهذا غير مسموح.";
                return false;
            }

            // ✅ Magic bytes
            if (!IsFileSignatureValid(file, extension))
            {
                errorMessage = "نوع الملف الحقيقي لا يطابق امتداده (ملف غير صالح).";
                return false;
            }

            return true;
        }

        public string GetFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return _environment.ContentRootPath;

            relativePath = NormalizeRelative(relativePath);

            var combined = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, relativePath));
            var root = Path.GetFullPath(_environment.ContentRootPath);

            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid file path.");

            return combined;
        }

        #region Helpers
        // ===== Helpers =====
        private static string NormalizeRelative(string relativePath)
        {
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                       .Replace('\\', Path.DirectorySeparatorChar);

            return relativePath.TrimStart(Path.DirectorySeparatorChar);
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Entity";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    sb.Append(ch);
            }
            var s = sb.ToString();
            return string.IsNullOrWhiteSpace(s) ? "Entity" : s;
        }

        private static bool HasDoubleExtension(string fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3;
        }

        private static string? GetSecondExtension(string fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;
            return "." + parts[^2];
        }

        private static bool IsFileSignatureValid(IFormFile file, string extension)
        {
            extension = (extension ?? "").ToLowerInvariant();

            using var stream = file.OpenReadStream();
            Span<byte> header = stackalloc byte[16];
            var read = stream.Read(header);
            if (read <= 0) return false;

            if (extension == ".pdf")
                return read >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;

            if (extension == ".jpg" || extension == ".jpeg")
                return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

            if (extension == ".png")
                return read >= 8 &&
                       header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                       header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

            if (extension == ".zip" || extension == ".docx" || extension == ".xlsx")
                return read >= 2 && header[0] == 0x50 && header[1] == 0x4B;

            if (extension == ".doc" || extension == ".xls")
                return read >= 4 && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;

            if (extension == ".rar")
                return read >= 4 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21;

            if (extension == ".txt" || extension == ".csv" || extension == ".svg")
                return true;

            return false;
        }

        #endregion
    }
}
