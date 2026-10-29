using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;

namespace Application.Services.AttachmentService
{
    /// <summary>
    /// رفع وربط وتنزيل المرفقات عبر FileAsset الحالي، مع صلاحيات طبية وعزل مراكز وتدقيق بدون محتوى ثنائي (FR-048 / FR-049).
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        public const string Member = "Member";
        public const string Consultation = "Consultation";
        public const string Investigation = "Investigation";
        public const string UserProfile = "UserProfile";

        public const string MedicalAttachmentUploadedEventType = "MedicalAttachmentUploaded";
        public const string MedicalAttachmentDownloadedEventType = "MedicalAttachmentDownloaded";

        private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        {
            Member, Consultation, Investigation, UserProfile
        };

        private static readonly HashSet<string> MedicalWriteTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            Consultation, Investigation
        };

        private readonly IAttachmentRepository _files;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICurrentUserService _currentUser;
        private readonly ICurrentCenterService _center;
        private readonly IAccessContextRepository _access;
        private readonly IActiveCenterService _activeCenters;
        private readonly IPermissionGate _permissions;
        private readonly IAuditLogRepository _audit;

        public AttachmentService(
            IAttachmentRepository files,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IFileStorageService fileStorage,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserService currentUser,
            ICurrentCenterService center,
            IAccessContextRepository access,
            IActiveCenterService activeCenters,
            IPermissionGate permissions,
            IAuditLogRepository audit)
        {
            _files = files;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _fileStorage = fileStorage;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _center = center;
            _access = access;
            _activeCenters = activeCenters;
            _permissions = permissions;
            _audit = audit;
        }

        /// <inheritdoc />
        public async Task<AttachmentReadDto> UploadAsync(string entityId, string entityType, IFormFile file, string? description = null)
        {
            if (file == null || file.Length == 0)
                throw new InvalidUserDataException("الملف فارغ.");
            RequireSupported(entityType);

            var isMedicalWrite = MedicalWriteTypes.Contains(entityType);
            Guid? centerId;
            if (entityType.Equals(UserProfile, StringComparison.OrdinalIgnoreCase))
            {
                centerId = await ResolveProfilePhotoCenterAsync(entityId);
            }
            else if (isMedicalWrite)
            {
                centerId = await RequireMedicalWriteAsync();
            }
            else
            {
                // Member list/upload path remains blocked in LinkCore; still requires operational center.
                centerId = await RequireActiveCenterAsync();
            }

            if (!_fileStorage.ValidateFile(file, out string err))
                throw new InvalidUserDataException(err);

            var (path, size) = await _fileStorage.SaveFileAsync(file, entityType, entityId);
            var userId = RequireUser();
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
                UploadedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            };

            _files.Add(asset);
            try
            {
                await LinkCoreAsync(asset, entityId, entityType, centerId);
                if (isMedicalWrite && centerId.HasValue)
                {
                    RecordAudit(
                        userId,
                        centerId.Value,
                        MedicalAttachmentUploadedEventType,
                        "SUCCESS",
                        asset.FileAssetId.ToString(),
                        $"رفع مرفق طبي وربطه بـ {entityType}",
                        oldValues: null,
                        newValues: JsonSerializer.Serialize(new
                        {
                            asset.FileAssetId,
                            EntityType = entityType,
                            EntityId = entityId,
                            asset.OriginalName,
                            asset.Extension,
                            asset.MimeType,
                            asset.SizeBytes,
                            asset.Sha256,
                            asset.CenterId
                            // لا يُسجَّل StorageKey ولا محتوى الملف.
                        }));
                }

                await _unitOfWork.SaveChangesAsync();
            }
            catch
            {
                await _fileStorage.DeleteFileAsync(path);
                throw;
            }

            return MapToDto(asset);
        }

        /// <inheritdoc />
        public async Task<AttachmentReadDto> GetByIdAsync(Guid id)
        {
            var entity = await RequireReadableAsync(id);
            return MapToDto(entity);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _files.GetTrackedByIdAsync(id)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");
            if (!await _files.IsProfilePhotoAsync(RequireUser(), id))
                await EnsureSameCenterOrNotFoundAsync(entity.CenterId);
            if (await _files.HasMedicalLinkAsync(id))
                throw new InvalidUserDataException("لا يمكن حذف ملف مرتبط بسجل طبي.");

            await _files.ClearProfilePhotoAsync(id);
            entity.IsDeleted = true;
            _files.Update(entity);
            await _unitOfWork.SaveChangesAsync();
            await _fileStorage.DeleteFileAsync(entity.StorageKey);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<AttachmentReadDto>> GetAttachmentsForEntityAsync(string entityId, params string[] entityTypes)
        {
            var type = SingleType(entityTypes);
            if (!IsOwnProfile(entityId, type))
                await RequireMedicalReadAsync();
            var centerId = await RequireActiveCenterAsync();
            var files = await LoadFilesAsync(entityId, type, centerId);
            return files.Select(MapToDto).ToList();
        }

        /// <inheritdoc />
        public async Task LinkToEntityAsync(Guid attachmentId, string entityId, string entityType)
        {
            RequireSupported(entityType);
            var centerId = MedicalWriteTypes.Contains(entityType)
                ? await RequireMedicalWriteAsync()
                : await RequireActiveCenterAsync();

            var asset = await _files.GetTrackedByIdAsync(attachmentId)
                ?? throw new RoleNotFoundException("المرفق غير موجود.");
            if (asset.CenterId != centerId)
                throw new RoleNotFoundException("المرفق غير موجود.");

            await LinkCoreAsync(asset, entityId, entityType, centerId);
            await _unitOfWork.SaveChangesAsync();
        }

        /// <inheritdoc />
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
                throw new RoleNotFoundException("الملف غير موجود أو تالف.");
            }

            if (await _files.HasMedicalLinkAsync(id) && file.CenterId.HasValue)
            {
                RecordAudit(
                    RequireUser(),
                    file.CenterId.Value,
                    MedicalAttachmentDownloadedEventType,
                    "SUCCESS",
                    file.FileAssetId.ToString(),
                    "تنزيل مرفق طبي عبر واجهة محمية",
                    oldValues: null,
                    newValues: JsonSerializer.Serialize(new
                    {
                        file.FileAssetId,
                        file.OriginalName,
                        file.Extension,
                        file.MimeType,
                        file.SizeBytes,
                        file.CenterId
                    }));
                await _unitOfWork.SaveChangesAsync();
            }

            var name = string.IsNullOrWhiteSpace(file.OriginalName)
                ? $"{file.FileAssetId}{file.Extension}"
                : file.OriginalName;
            return (bytes, file.MimeType, name);
        }

        /// <inheritdoc />
        public async Task<(byte[] Bytes, string ZipName)> DownloadZipForEntityAsync(string entityId, params string[] entityTypes)
        {
            var type = SingleType(entityTypes);
            if (!IsOwnProfile(entityId, type))
                await RequireMedicalReadAsync();
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
                        throw new RoleNotFoundException("أحد المرفقات غير موجود أو تالف.");
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

                var profile = await _files.FindTrackedProfileAsync(entityId)
                    ?? throw new InvalidUserDataException(
                        "لا يمكن رفع صورة الملف الشخصي قبل حفظ البيانات الوظيفية (الرقم الوظيفي والرتبة أو المسمى).");

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

            // FR-049: لا يُكشَف وجود سجل في مركز آخر.
            if (target.CenterId != centerId || asset.CenterId != target.CenterId)
                throw new RoleNotFoundException("الكيان المطلوب غير موجود.");

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
                var member = await _files.FindMemberAsync(id)
                    ?? throw new RoleNotFoundException("المنتسب غير موجود.");
                if (member.CenterId != centerId)
                    throw new RoleNotFoundException("المنتسب غير موجود.");
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

            await EnsureSameCenterOrNotFoundAsync(file.CenterId);
            await RequireMedicalReadAsync();
            return file;
        }

        private bool IsOwnProfile(string entityId, string entityType)
            => entityType.Equals(UserProfile, StringComparison.OrdinalIgnoreCase)
               && string.Equals(entityId, RequireUser(), StringComparison.Ordinal);

        private Task RequireMedicalReadAsync()
            => _permissions.EnsureLocalAsync(MilitaryHealthPermissions.Medical.AttachmentsRead);

        private Task<Guid> RequireMedicalWriteAsync()
            => _permissions.EnsureLocalAsync(MilitaryHealthPermissions.Medical.ConsultationsInvestigations);

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

        private Task<Guid> RequireActiveCenterAsync()
            => _activeCenters.RequireOperationalCenterAsync();

        /// <summary>FR-049: مركز آخر يُعامل كغير موجود دون كشف الوجود.</summary>
        private async Task EnsureSameCenterOrNotFoundAsync(Guid? fileCenterId)
        {
            var centerId = await RequireActiveCenterAsync();
            if (fileCenterId != centerId)
                throw new RoleNotFoundException("المرفق غير موجود أو محذوف.");
        }

        private void RecordAudit(
            string userId,
            Guid centerId,
            string eventType,
            string result,
            string entityId,
            string reasonText,
            string? oldValues,
            string? newValues)
        {
            _audit.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = userId,
                ContextScope = CenterClaimTypes.Center,
                CenterId = centerId,
                EventType = eventType,
                Result = result,
                EntityType = nameof(FileAsset),
                EntityId = entityId,
                ReasonText = reasonText,
                OldValues = oldValues,
                NewValues = newValues,
                UserAgent = _currentUser.UserAgent,
                CorrelationId = _currentUser.CorrelationId == Guid.Empty
                    ? Guid.NewGuid()
                    : _currentUser.CorrelationId,
                IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
            });
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
