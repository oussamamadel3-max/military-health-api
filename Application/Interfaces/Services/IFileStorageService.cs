using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services
{
    /// <summary>
    /// خدمة تخزين الملفات
    /// </summary>
    public interface IFileStorageService
    {
        /// رفع ملف وحفظه
        Task<(string FilePath, long FileSize)> SaveFileAsync(IFormFile file, string entityType, long entityId);

        /// حذف ملف
        Task<bool> DeleteFileAsync(string filePath);

        /// التحقق من صلاحية الملف
        bool ValidateFile(IFormFile file, out string errorMessage);

        /// الحصول على المسار الكامل للملف
        string GetFullPath(string relativePath);
    }

}
