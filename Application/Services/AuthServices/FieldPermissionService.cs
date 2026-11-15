using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Utilities;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Domain.Enums;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices
{
    /// <summary>
    /// إدارة صلاحيات الحقول (CRUD + كتالوج). الإنفاذ التشغيلي يتم عبر
    /// إنفاذ الأعمال على MotherName عبر <see cref="Application.Services.Members.MemberFieldPermissionGuard"/>
    /// عندما يكون <c>FieldPermissions:EnforcementEnabled</c> مفعّلاً.
    /// </summary>
    public class FieldPermissionService : IFieldPermissionService
    {
        private readonly IFieldPermissionRepository _fieldPermissionRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<User> _userManager;
        private readonly IMapper _mapper;
        private readonly IPermissionGate _permissionGate;
        private readonly IFieldPermissionCatalog _catalog;

        public FieldPermissionService(
            IFieldPermissionRepository fieldPermissionRepository,
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            UserManager<User> userManager,
            IMapper mapper,
            IPermissionGate permissionGate,
            IFieldPermissionCatalog catalog)
        {
            _fieldPermissionRepository = fieldPermissionRepository;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _mapper = mapper;
            _permissionGate = permissionGate;
            _catalog = catalog;
        }

        #region CRUD للـ FieldPermissions

        public async Task<PaginatedList<FieldPermissionDto>> GetFieldPermissionsAsync(int page, int size, FieldPermissionFilterDto? filter = null)
        {
            var query = _fieldPermissionRepository.Query(asNoTracking: true);

            if (filter != null)
            {
                if (!string.IsNullOrWhiteSpace(filter.EntityName))
                {
                    query = query.Where(fp => fp.EntityName.ToLower() == filter.EntityName.Trim().ToLower());
                }

                if (!string.IsNullOrWhiteSpace(filter.FieldName))
                {
                    query = query.Where(fp => fp.FieldName.ToLower() == filter.FieldName.Trim().ToLower());
                }

                if (filter.AccessType.HasValue)
                {
                    query = query.Where(fp => fp.AccessType == filter.AccessType.Value);
                }

                if (!string.IsNullOrWhiteSpace(filter.RoleId))
                {
                    query = query.Where(fp => fp.RoleFieldPermissions.Any(rfp => rfp.RoleId == filter.RoleId));
                }

                if (!string.IsNullOrWhiteSpace(filter.UserId))
                {
                    query = query.Where(fp => fp.UserFieldPermissions.Any(ufp => ufp.UserId == filter.UserId));
                }
            }

            var totalCount = await query.CountAsync();

            var paginatedQuery = query
                .Skip((page - 1) * size)
                .Take(size);

            var baseData = await paginatedQuery.Select(fp => new
            {
                fp.Id,
                fp.Description,
                fp.EntityName,
                fp.FieldName,
                fp.AccessType,
                RoleIds = fp.RoleFieldPermissions
                    .Where(rfp => rfp.Role != null)
                    .Select(rfp => rfp.RoleId)
                    .ToList(),
                UserIds = fp.UserFieldPermissions
                    .Where(ufp => ufp.User != null)
                    .Select(ufp => ufp.UserId)
                    .ToList()
            }).ToListAsync();

            var allRoleIds = baseData.SelectMany(d => d.RoleIds).Distinct().ToList();
            var allUserIds = baseData.SelectMany(d => d.UserIds).Distinct().ToList();

            var rolesDict = new Dictionary<string, RoleInfoDto>();
            if (allRoleIds.Any())
            {
                var roles = await _roleRepository.QueryRoles()
                    .Where(r => allRoleIds.Contains(r.Id))
                    .ToListAsync();
                rolesDict = roles.ToDictionary(r => r.Id, r => _mapper.Map<RoleInfoDto>(r));
            }

            var usersDict = new Dictionary<string, UserInfoDto>();
            if (allUserIds.Any())
            {
                var users = await _userRepository.QueryUsers()
                    .Where(u => allUserIds.Contains(u.Id))
                    .ToListAsync();
                usersDict = users.ToDictionary(u => u.Id, u => _mapper.Map<UserInfoDto>(u));
            }

            var result = baseData.Select(d =>
            {
                var dto = _mapper.Map<FieldPermissionDto>(new FieldPermission
                {
                    Id = d.Id,
                    Description = d.Description,
                    EntityName = d.EntityName,
                    FieldName = d.FieldName,
                    AccessType = d.AccessType
                });
                dto.Roles = d.RoleIds
                    .Distinct()
                    .Where(rid => rolesDict.ContainsKey(rid))
                    .Select(rid => rolesDict[rid])
                    .ToList();
                dto.Users = d.UserIds
                    .Distinct()
                    .Where(uid => usersDict.ContainsKey(uid))
                    .Select(uid => usersDict[uid])
                    .ToList();
                return dto;
            }).ToList();

            return new PaginatedList<FieldPermissionDto>(result, totalCount, page, size);
        }

        public async Task<FieldPermissionDto> GetByIdAsync(Guid id)
        {
            var entity = await _fieldPermissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("صلاحية الحقل غير موجودة.");

            var roleLinks = await _fieldPermissionRepository.GetRoleLinksAsync(id);
            var userLinks = await _fieldPermissionRepository.GetUserLinksAsync(id);
            var roleIds = roleLinks.Select(r => r.RoleId).Distinct().ToList();
            var userIds = userLinks.Select(u => u.UserId).Distinct().ToList();

            var rolesDict = new Dictionary<string, RoleInfoDto>();
            if (roleIds.Any())
            {
                var roles = await _roleRepository.QueryRoles()
                    .Where(r => roleIds.Contains(r.Id))
                    .ToListAsync();
                rolesDict = roles.ToDictionary(r => r.Id, r => _mapper.Map<RoleInfoDto>(r));
            }

            var usersDict = new Dictionary<string, UserInfoDto>();
            if (userIds.Any())
            {
                var users = await _userRepository.QueryUsers()
                    .Where(u => userIds.Contains(u.Id))
                    .ToListAsync();
                usersDict = users.ToDictionary(u => u.Id, u => _mapper.Map<UserInfoDto>(u));
            }

            var dto = _mapper.Map<FieldPermissionDto>(entity);
            dto.Roles = roleIds
                .Where(rid => rolesDict.ContainsKey(rid))
                .Select(rid => rolesDict[rid])
                .ToList();
            dto.Users = userIds
                .Where(uid => usersDict.ContainsKey(uid))
                .Select(uid => usersDict[uid])
                .ToList();
            return dto;
        }

        public async Task<CreateFieldPermissionsResultDto> CreateAsync(CreateFieldPermissionDto dto)
        {
            _permissionGate.EnsureAll(SystemPermissions.FieldPermissionManagement.Create);

            if (!Enum.IsDefined(typeof(FieldAccessType), dto.AccessType))
                throw new InvalidRoleDataException("نوع الوصول غير صالح.");

            var roleIds = NormalizeIds(dto.RoleIds);
            var userIds = NormalizeIds(dto.UserIds);

            var fields = (dto.FieldNames ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!fields.Any())
                throw new InvalidRoleDataException("يجب تحديد حقل واحد على الأقل.");

            var entityName = dto.EntityName.Trim();
            EnsureCatalogAllows(entityName, fields);

            var existingPermissions = await _fieldPermissionRepository.GetExistingFieldNamesAsync(entityName);

            var duplicateFields = fields
                .Where(f => existingPermissions.Any(ep => ep.Equals(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (duplicateFields.Any())
            {
                var existingFields = string.Join(", ", duplicateFields);
                throw new InvalidRoleDataException($"صلاحيات الحقول التالية موجودة مسبقاً في كيان {entityName}: {existingFields}");
            }

            var entityDisplayName = _catalog.GetEntityDisplayName(entityName);
            var fieldDisplayMap = _catalog.GetFieldDisplayMap(entityName);

            var createdDtos = new List<FieldPermissionDto>();

            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                foreach (var field in fields)
                {
                    var entity = new FieldPermission
                    {
                        Id = Guid.NewGuid(),
                        EntityName = entityName,
                        FieldName = field,
                        AccessType = dto.AccessType,
                        Description = BuildDescription(entityDisplayName, fieldDisplayMap, field, dto.AccessType)
                    };

                    _fieldPermissionRepository.Add(entity);
                    await _unitOfWork.SaveChangesAsync();

                    await SetRoleAssignmentsAsync(entity, roleIds);
                    await SetUserAssignmentsAsync(entity, userIds);

                    var roles = await _roleRepository.QueryRoles()
                        .Where(r => roleIds.Contains(r.Id))
                        .ToListAsync();
                    var rolesInfo = _mapper.Map<List<RoleInfoDto>>(roles);

                    var users = await _userRepository.QueryUsers()
                        .Where(u => userIds.Contains(u.Id))
                        .ToListAsync();
                    var usersInfo = _mapper.Map<List<UserInfoDto>>(users);

                    var createdDto = _mapper.Map<FieldPermissionDto>(entity);
                    createdDto.Roles = rolesInfo;
                    createdDto.Users = usersInfo;
                    createdDtos.Add(createdDto);
                }

                await _unitOfWork.SaveChangesAsync();

                var affectedUserIds = createdDtos
                    .SelectMany(d => d.Users ?? new List<UserInfoDto>())
                    .Select(u => u.Id)
                    .Distinct()
                    .ToList();

                var affectedRoleIds = createdDtos
                    .SelectMany(d => d.Roles ?? new List<RoleInfoDto>())
                    .Select(r => r.Id)
                    .Distinct()
                    .ToList();

                foreach (var userId in affectedUserIds)
                {
                    await SecurityStampHelper.InvalidateUserTokensAsync(
                        _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, userId);
                }

                foreach (var roleId in affectedRoleIds)
                {
                    await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                        _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
                }

                await transaction.CommitAsync();

                return new CreateFieldPermissionsResultDto
                {
                    EntityName = entityName,
                    AccessType = dto.AccessType,
                    Permissions = createdDtos
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateAsync(Guid id, UpdateFieldPermissionDto dto)
        {
            _permissionGate.EnsureAll(SystemPermissions.FieldPermissionManagement.Edit);

            if (!Enum.IsDefined(typeof(FieldAccessType), dto.AccessType))
                throw new InvalidRoleDataException("نوع الوصول غير صالح.");

            var entity = await _fieldPermissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("صلاحية الحقل غير موجودة.");

            var oldUserLinks = await _fieldPermissionRepository.GetUserLinksAsync(id);
            var oldUserIds = oldUserLinks.Select(ufp => ufp.UserId).Distinct().ToList();

            var oldRoleLinks = await _fieldPermissionRepository.GetRoleLinksAsync(id);
            var oldRoleIds = oldRoleLinks.Select(rfp => rfp.RoleId).Distinct().ToList();

            _mapper.Map(dto, entity);
            entity.Description = BuildDescription(
                _catalog.GetEntityDisplayName(entity.EntityName),
                _catalog.GetFieldDisplayMap(entity.EntityName),
                entity.FieldName,
                entity.AccessType);

            await SetRoleAssignmentsAsync(entity, dto.RoleIds);
            await SetUserAssignmentsAsync(entity, dto.UserIds);

            await _unitOfWork.SaveChangesAsync();

            var allAffectedUserIds = oldUserIds
                .Union(dto.UserIds ?? new List<string>())
                .Distinct()
                .ToList();

            var allAffectedRoleIds = oldRoleIds
                .Union(dto.RoleIds ?? new List<string>())
                .Distinct()
                .ToList();

            foreach (var userId in allAffectedUserIds)
            {
                await SecurityStampHelper.InvalidateUserTokensAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, userId);
            }

            foreach (var roleId in allAffectedRoleIds)
            {
                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            _permissionGate.EnsureAll(SystemPermissions.FieldPermissionManagement.Delete);

            var entity = await _fieldPermissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("صلاحية الحقل غير موجودة.");

            var roleLinks = await _fieldPermissionRepository.GetRoleLinksAsync(id);
            var userLinks = await _fieldPermissionRepository.GetUserLinksAsync(id);

            var affectedUserIds = userLinks.Select(ufp => ufp.UserId).Distinct().ToList();
            var affectedRoleIds = roleLinks.Select(rfp => rfp.RoleId).Distinct().ToList();

            _fieldPermissionRepository.RemoveRoleLinks(roleLinks);
            _fieldPermissionRepository.RemoveUserLinks(userLinks);

            _fieldPermissionRepository.Remove(entity);
            await _unitOfWork.SaveChangesAsync();

            foreach (var userId in affectedUserIds)
            {
                await SecurityStampHelper.InvalidateUserTokensAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, userId);
            }

            foreach (var roleId in affectedRoleIds)
            {
                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
            }
        }

        public async Task ChangeAccessTypeAsync(Guid id, ChangeFieldPermissionAccessTypeDto dto)
        {
            _permissionGate.EnsureAll(SystemPermissions.FieldPermissionManagement.ChangeAccessType);

            var entity = await _fieldPermissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("صلاحية الحقل غير موجودة.");

            var oldAccessType = entity.AccessType;
            if (!Enum.IsDefined(typeof(FieldAccessType), dto.AccessType))
                throw new InvalidRoleDataException("نوع الوصول غير صالح.");

            if (oldAccessType == dto.AccessType)
                throw new InvalidRoleDataException("نوع الصلاحية الحالي مطابق للنوع الجديد.");

            entity.AccessType = dto.AccessType;
            entity.Description = BuildDescription(
                _catalog.GetEntityDisplayName(entity.EntityName),
                _catalog.GetFieldDisplayMap(entity.EntityName),
                entity.FieldName,
                dto.AccessType);

            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUsersWithFieldPermissionAsync(
                _fieldPermissionRepository, _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id);
        }

        #endregion

        #region الكتالوج

        public Task<List<EntityInfoDto>> GetEntitiesAsync()
            => Task.FromResult(_catalog.GetEntities().ToList());

        public Task<List<FieldInfoDto>> GetFieldsAsync(string entityName)
            => Task.FromResult(_catalog.GetFields(entityName).ToList());

        /// <summary>
        /// يرفض الإنشاء إن كان الكيان أو أي حقل خارج الكتالوج المعتمد (حتى لو كان الكتالوج فارغاً).
        /// </summary>
        private void EnsureCatalogAllows(string entityName, IReadOnlyList<string> fieldNames)
        {
            if (!_catalog.IsEntityRegistered(entityName))
            {
                throw new InvalidRoleDataException(
                    $"الكيان '{entityName}' غير معتمد في كتالوج صلاحيات الحقول.");
            }

            var unknown = fieldNames
                .Where(f => !_catalog.IsFieldRegistered(entityName, f))
                .ToList();

            if (unknown.Count > 0)
            {
                throw new InvalidRoleDataException(
                    $"الحقول التالية غير معتمدة لكيان '{entityName}': {string.Join(", ", unknown)}");
            }
        }

        private static List<string> NormalizeIds(IEnumerable<string>? ids)
        {
            return ids?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private async Task SetRoleAssignmentsAsync(FieldPermission entity, IEnumerable<string>? roleIds)
        {
            var normalized = NormalizeIds(roleIds);

            if (entity.Id != Guid.Empty)
            {
                var existing = await _fieldPermissionRepository.GetRoleLinksAsync(entity.Id);
                if (existing.Count > 0)
                {
                    _fieldPermissionRepository.RemoveRoleLinks(existing);
                }
            }

            if (!normalized.Any())
                return;

            foreach (var roleId in normalized)
            {
                _fieldPermissionRepository.AddRoleLink(new RoleFieldPermission
                {
                    RoleId = roleId,
                    FieldPermissionId = entity.Id
                });
            }
        }

        private async Task SetUserAssignmentsAsync(FieldPermission entity, IEnumerable<string>? userIds)
        {
            var normalized = NormalizeIds(userIds);

            if (entity.Id != Guid.Empty)
            {
                var existing = await _fieldPermissionRepository.GetUserLinksAsync(entity.Id);
                if (existing.Count > 0)
                {
                    _fieldPermissionRepository.RemoveUserLinks(existing);
                }
            }

            if (!normalized.Any())
                return;

            foreach (var userId in normalized)
            {
                _fieldPermissionRepository.AddUserLink(new UserFieldPermission
                {
                    UserId = userId,
                    FieldPermissionId = entity.Id
                });
            }
        }

        private static string BuildDescription(
            string entityDisplayName,
            IReadOnlyDictionary<string, string> fieldDisplayMap,
            string fieldName,
            FieldAccessType accessType)
        {
            var accessDescription = EnumHelper.GetEnumDescription(accessType);
            var fieldDisplay = fieldDisplayMap.TryGetValue(fieldName, out var disp) ? disp : fieldName;
            return $"حقل {fieldDisplay} في كيان {entityDisplayName}: ({accessDescription})";
        }

        #endregion
    }
}
