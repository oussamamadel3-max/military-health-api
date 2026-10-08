using System.Security.Cryptography;
using Application.Dtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;

namespace Application.Services.AttachmentService
{
    public class AttachmentService : IAttachmentService
    {
        public const string Member = "Member";
        public const string Consultation = "Consultation";
        public const string Investigation = "Investigation";
        public const string UserProfile = "UserProfile";

        private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        {
            Member, Consultation, Investigation, UserProfile
        };

        private readonly IAttachmentRepository _files;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICurrentUserService _currentUser;
        private readonly ICurrentCenterService _center;
        private readonly IAccessContextRepository _access;

        public AttachmentService(
            IAttachmentRepository files,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IFileStorageService fileStorage,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserService currentUser,
            ICurrentCenterService center,
            IAccessContextRepository access)
        {
            _files = files;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _fileStorage = fileStorage;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _center = center;
            _access = access;
        }

        public async Task<AttachmentReadDto> UploadAsync(string entityId, string entityType, IFormFile file, string? description = null)
        {
            if (file == null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ.");
            RequireSupported(entityType);
            var centerId = entityType.Equals(UserProfile, StringComparison.OrdinalIgnoreCase)
                ? await ResolveProfilePhotoCenterAsync(entityId)
                : await RequireActiveCenterAsync();
            if (!_fileStorage.ValidateFile(file, out string err))
                throw new InvalidUserDataException(err);

            var (path, size) = await _fileStorage.SaveFileAsync(file, entityType, entityId);
            var asset = new FileAsset
            {
                FileAssetId = Guid.NewGuid(),
                CenterId = centerId,
                StorageKey = path,
                OriginalName = SanitizeDisplayFileName(file.FileName),
                Extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "",
                MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = size,
                Sha256 = await ComputeSha256Async(file),
                UploadedByUserId = RequireUser(),
                CreatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            };

            _files.Add(asset);
            try
            {
                await LinkCoreAsync(asset, entityId, entityType, centerId);
                await _unitOfWork.SaveChangesAsync();
            }
            catch
            {
                await _fileStorage.DeleteFileAsync(path);
                throw;
            }

            return MapToDto(asset);
        }

        public async Task<AttachmentReadDto> GetByIdAsync(Guid id)
        {
            var entity = await RequireReadableAsync(id);
            return MapToDto(entity);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _files.GetTrackedByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");
            if (!await _files.IsProfilePhotoAsync(RequireUser(), id))
                await EnsureSameCenterAsync(entity.CenterId);
            if (await _files.HasMedicalLinkAsync(id))
                throw new InvalidUserDataException("لا يمكن حذف ملف مرتبط بسجل طبي.");

            await _files.ClearProfilePhotoAsync(id);
            entity.IsDeleted = true;
            _files.Update(entity);
            await _unitOfWork.SaveChangesAsync();
            await _fileStorage.DeleteFileAsync(entity.StorageKey);
            return true;
        }

        public async Task<List<AttachmentReadDto>> GetAttachmentsForEntityAsync(string entityId, params string[] entityTypes)
        {
            var type = SingleType(entityTypes);
            var centerId = await RequireActiveCenterAsync();
            var files = await LoadFilesAsync(entityId, type, centerId);
            return files.Select(MapToDto).ToList();
        }

        public async Task LinkToEntityAsync(Guid attachmentId, string entityId, string entityType)
        {
            RequireSupported(entityType);
            var centerId = await RequireActiveCenterAsync();
            var asset = await _files.GetTrackedByIdAsync(attachmentId)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");
            if (asset.CenterId != centerId)
                throw new InvalidUserDataException("لا يمكن ربط ملف من مركز بكيان في مركز آخر.");
            await LinkCoreAsync(asset, entityId, entityType, centerId);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAsync(Guid id)
        {
            var file = await RequireReadableAsync(id);
            byte[] bytes;
            try
            {
                bytes = _fileStorage.ReadFileBytes(file.StorageKey);
            }
            catch (InvalidFileException)
            {
                throw new RoleNotFoundException($"الملف الفيزيائي غير موجود: {file.OriginalName}");
            }

            var name = string.IsNullOrWhiteSpace(file.OriginalName)
                ? $"{file.FileAssetId}{file.Extension}"
                : file.OriginalName;
            return (bytes, file.MimeType, name);
        }

        public async Task<(byte[] Bytes, string ZipName)> DownloadZipForEntityAsync(string entityId, params string[] entityTypes)
        {
            var type = SingleType(entityTypes);
            var centerId = await RequireActiveCenterAsync();
            var files = await LoadFilesAsync(entityId, type, centerId);
            if (files.Count == 0)
                throw new RoleNotFoundException("لا توجد مرفقات لهذا الكيان.");

            await using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = _fileStorage.ReadFileBytes(file.StorageKey);
                    }
                    catch (InvalidFileException)
                    {
                        throw new InvalidUserDataException($"الملف مفقود على القرص: {file.OriginalName}");
                    }

                    var entryName = UniqueName(used, file.OriginalName, file.FileAssetId);
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes);
                }
            }

            return (memory.ToArray(), $"{type}-{entityId}.zip");
        }

        private async Task LinkCoreAsync(FileAsset asset, string entityId, string entityType, Guid? centerId)
        {
            if (entityType.Equals(UserProfile, StringComparison.OrdinalIgnoreCase))
            {
                var actorId = RequireUser();
                if (!string.Equals(entityId, actorId, StringComparison.Ordinal))
                    throw new InvalidUserDataException("لا يمكن ربط صورة مستخدم آخر.");

                var profile = await _files.FindTrackedProfileAsync(entityId);
                if (profile == null)
                {
                    var now = DateTimeOffset.UtcNow;
                    profile = new UserProfile
                    {
                        UserId = entityId,
                        EmployeeNumber = entityId,
                        RankOrTitle = "غير محدد",
                        UpdatedByUserId = actorId,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _files.AddProfile(profile);
                }

                profile.ProfileFileAssetId = asset.FileAssetId;
                profile.UpdatedAt = DateTimeOffset.UtcNow;
                profile.UpdatedByUserId = actorId;
                return;
            }

            if (entityType.Equals(Member, StringComparison.OrdinalIgnoreCase))
                throw new InvalidUserDataException("ربط الملف بالمنتسب يتم عبر الاستشارة أو الاستقصاء، لأن المرفق الطبي يتطلب أحدهما.");

            if (!Guid.TryParse(entityId, out var id))
                throw new InvalidUserDataException("معرف الكيان غير صالح.");

            var target = entityType.Equals(Consultation, StringComparison.OrdinalIgnoreCase)
                ? await _files.FindConsultationAsync(id)
                : await _files.FindInvestigationAsync(id);
            if (target == null)
                throw new RoleNotFoundException("الكيان المطلوب غير موجود.");
            if (target.CenterId != centerId || asset.CenterId != target.CenterId)
                throw new InvalidUserDataException("لا يمكن ربط ملف من مركز بكيان تابع لمركز آخر.");

            _files.AddMedicalAttachment(new MedicalAttachment
            {
                AttachmentId = Guid.NewGuid(),
                MemberId = target.MemberId,
                CenterId = target.CenterId,
                FileAssetId = asset.FileAssetId,
                ConsultationId = entityType.Equals(Consultation, StringComparison.OrdinalIgnoreCase) ? target.Id : null,
                InvestigationId = entityType.Equals(Investigation, StringComparison.OrdinalIgnoreCase) ? target.Id : null,
                UploadedByUserId = RequireUser(),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        private async Task<List<FileAsset>> LoadFilesAsync(string entityId, string entityType, Guid centerId)
        {
            if (entityType.Equals(UserProfile, StringComparison.OrdinalIgnoreCase))
            {
                var profile = await _files.FindTrackedProfileAsync(entityId);
                if (profile?.ProfileFileAssetId is not Guid fileId)
                    return new List<FileAsset>();
                var file = await _files.FindByIdAsync(fileId);
                return file == null || file.CenterId != centerId ? new List<FileAsset>() : new List<FileAsset> { file };
            }

            if (!Guid.TryParse(entityId, out var id))
                throw new InvalidUserDataException("معرف الكيان غير صالح.");

            if (entityType.Equals(Member, StringComparison.OrdinalIgnoreCase))
            {
                var member = await _files.FindMemberAsync(id) ?? throw new RoleNotFoundException("المنتسب غير موجود.");
                if (member.CenterId != centerId)
                    throw new InvalidUserDataException("المنتسب لا يتبع المركز النشط.");
                return await _files.ListMemberFilesAsync(id, centerId);
            }

            if (entityType.Equals(Consultation, StringComparison.OrdinalIgnoreCase))
                return await _files.ListConsultationFilesAsync(id, centerId);
            if (entityType.Equals(Investigation, StringComparison.OrdinalIgnoreCase))
                return await _files.ListInvestigationFilesAsync(id, centerId);

            throw new InvalidUserDataException("نوع الكيان غير مدعوم.");
        }

        private async Task<FileAsset> RequireReadableAsync(Guid id)
        {
            var file = await _files.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود أو محذوف.");
            var userId = RequireUser();
            if (await _files.IsProfilePhotoAsync(userId, id))
                return file;
            await EnsureSameCenterAsync(file.CenterId);
            return file;
        }

        private async Task<Guid?> ResolveProfilePhotoCenterAsync(string entityId)
        {
            var userId = RequireUser();
            if (!string.Equals(entityId, userId, StringComparison.Ordinal))
                throw new InvalidUserDataException("لا يمكن رفع صورة مستخدم آخر.");

            if (!_center.IsCentralContext && _center.CenterId is Guid activeCenterId
                && await _access.IsActivelyAssignedAsync(userId, activeCenterId))
                return activeCenterId;

            return await _files.FindActiveAssignmentCenterIdAsync(userId)
                ?? await _files.FindFirstActiveCenterIdAsync();
        }

        private async Task<Guid> RequireActiveCenterAsync()
        {
            if (_center.IsCentralContext || _center.CenterId is not Guid centerId)
                throw new InvalidUserDataException("العملية المحلية تتطلب مركزاً نشطاً.");
            var userId = RequireUser();
            if (!await _access.IsActivelyAssignedAsync(userId, centerId))
                throw new ForbiddenPermissionException("المستخدم غير مسند إلى المركز النشط.");
            return centerId;
        }

        private async Task EnsureSameCenterAsync(Guid? fileCenterId)
        {
            var centerId = await RequireActiveCenterAsync();
            if (fileCenterId != centerId)
                throw new ForbiddenPermissionException("الملف لا يتبع المركز النشط.");
        }

        private string RequireUser()
        {
            if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
                throw new UnauthenticatedUserException();
            return _currentUser.UserId;
        }

        private static void RequireSupported(string? entityType)
        {
            if (string.IsNullOrWhiteSpace(entityType) || !Supported.Contains(entityType))
                throw new InvalidUserDataException("نوع الكيان غير مدعوم. الأنواع المسموحة: Member, Consultation, Investigation, UserProfile.");
        }

        private static string SingleType(string[] entityTypes)
        {
            if (entityTypes == null || entityTypes.Length != 1)
                throw new InvalidUserDataException("يجب تحديد نوع كيان واحد مدعوم.");
            RequireSupported(entityTypes[0]);
            return entityTypes[0];
        }

        private AttachmentReadDto MapToDto(FileAsset file)
        {
            var dto = _mapper.Map<AttachmentReadDto>(file);
            dto.FileSizeFormatted = FormatFileSize(file.SizeBytes);
            dto.FilePath = null;
            dto.FileUrl = BuildProtectedDownloadUrl(file.FileAssetId);
            dto.FileAssetId = file.FileAssetId;
            dto.CenterId = file.CenterId;
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

        private static string SanitizeDisplayFileName(string name)
        {
            name = Path.GetFileName(name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return "file";
            if (name.Length > 200) name = name[..200];
            return name;
        }

        private static async Task<string> ComputeSha256Async(IFormFile file)
        {
            await using var stream = file.OpenReadStream();
            var hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string UniqueName(HashSet<string> used, string? original, Guid id)
        {
            var name = string.IsNullOrWhiteSpace(original) ? $"{id}" : Path.GetFileName(original);
            if (used.Add(name))
                return name;
            var unique = $"{id}-{name}";
            used.Add(unique);
            return unique;
        }
    }
}
