using Application.Attributes;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Application.Dtos.DocumentDtos;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Entities.Auth;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using FieldPermissionEntityAttribute = Domain.Attributes.FieldPermissionEntityAttribute;

namespace Infrastructure.Services.AuthServices
{
    public class FieldPermissionService : IFieldPermissionService
    {
        private static readonly Assembly DomainEntitiesAssembly = typeof(Document).Assembly;
        private static readonly Assembly ApplicationDtosAssembly = typeof(DocumentDto).Assembly;
        private static Dictionary<string, string>? _dtoFieldDisplayCache;

        private static Dictionary<string, Dictionary<string, string>>? _entityDtoFieldsCache;

        private readonly IFieldPermissionRepository _fieldPermissionRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<User> _userManager;

        public FieldPermissionService(
            IFieldPermissionRepository fieldPermissionRepository,
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            UserManager<User> userManager)
        {
            _fieldPermissionRepository = fieldPermissionRepository;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        // =====================================================
        // CRUD للـ FieldPermissions
        // =====================================================

        public async Task<Result<PaginatedList<FieldPermissionDto>>> GetFieldPermissionsAsync(int page, int size, FieldPermissionFilterDto? filter = null)
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
                rolesDict = await _roleRepository.QueryRoles()
                    .Where(r => allRoleIds.Contains(r.Id))
                    .ToDictionaryAsync(r => r.Id, r => new RoleInfoDto
                    {
                        Id = r.Id,
                        Name = r.Name ?? string.Empty
                    });
            }

            var usersDict = new Dictionary<string, UserInfoDto>();
            if (allUserIds.Any())
            {
                usersDict = await _userRepository.QueryUsers()
                    .Where(u => allUserIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => new UserInfoDto
                    {
                        Id = u.Id,
                        Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.UserName ?? string.Empty)
                    });
            }

            var result = baseData.Select(d => new FieldPermissionDto
            {
                Id = d.Id,
                Description = d.Description ?? string.Empty,
                EntityName = d.EntityName,
                FieldName = d.FieldName,
                AccessType = d.AccessType,
                AccessTypeDescription = EnumHelper.GetEnumDescription(d.AccessType),
                Roles = d.RoleIds
                    .Distinct()
                    .Where(rid => rolesDict.ContainsKey(rid))
                    .Select(rid => rolesDict[rid])
                    .ToList(),
                Users = d.UserIds
                    .Distinct()
                    .Where(uid => usersDict.ContainsKey(uid))
                    .Select(uid => usersDict[uid])
                    .ToList()
            }).ToList();

            var list = new PaginatedList<FieldPermissionDto>(result, totalCount, page, size);
            return Result<PaginatedList<FieldPermissionDto>>.Success(list);
        }

        public async Task<Result<FieldPermissionDto>> GetByIdAsync(Guid id)
        {
            var baseData = await _fieldPermissionRepository.Query(asNoTracking: true)
                .Where(x => x.Id == id)
                .Select(fp => new
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
                })
                .FirstOrDefaultAsync();

            if (baseData == null)
                return Result<FieldPermissionDto>.Failure(new[] { "Not found" });

            var rolesDict = new Dictionary<string, RoleInfoDto>();
            if (baseData.RoleIds.Any())
            {
                rolesDict = await _roleRepository.QueryRoles()
                    .Where(r => baseData.RoleIds.Contains(r.Id))
                    .ToDictionaryAsync(r => r.Id, r => new RoleInfoDto
                    {
                        Id = r.Id,
                        Name = r.Name ?? string.Empty
                    });
            }

            var usersDict = new Dictionary<string, UserInfoDto>();
            if (baseData.UserIds.Any())
            {
                usersDict = await _userRepository.QueryUsers()
                    .Where(u => baseData.UserIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => new UserInfoDto
                    {
                        Id = u.Id,
                        Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.UserName ?? string.Empty)
                    });
            }

            var item = new FieldPermissionDto
            {
                Id = baseData.Id,
                Description = baseData.Description ?? string.Empty,
                EntityName = baseData.EntityName,
                FieldName = baseData.FieldName,
                AccessType = baseData.AccessType,
                AccessTypeDescription = EnumHelper.GetEnumDescription(baseData.AccessType),
                Roles = baseData.RoleIds
                    .Distinct()
                    .Where(rid => rolesDict.ContainsKey(rid))
                    .Select(rid => rolesDict[rid])
                    .ToList(),
                Users = baseData.UserIds
                    .Distinct()
                    .Where(uid => usersDict.ContainsKey(uid))
                    .Select(uid => usersDict[uid])
                    .ToList()
            };

            return Result<FieldPermissionDto>.Success(item);
        }

        public async Task<Result<CreateFieldPermissionsResultDto>> CreateAsync(CreateFieldPermissionDto dto)
        {
            var roleIds = NormalizeIds(dto.RoleIds);
            var userIds = NormalizeIds(dto.UserIds);

            var fields = (dto.FieldNames ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .ToList();

            if (!fields.Any())
                return Result<CreateFieldPermissionsResultDto>.Failure(new[] { "يجب تحديد حقل واحد على الأقل." });

            var entityName = dto.EntityName.Trim();

            var existingPermissions = await _fieldPermissionRepository.GetExistingFieldNamesAsync(entityName);

            var duplicateFields = fields
                .Where(f => existingPermissions.Any(ep => ep.Equals(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (duplicateFields.Any())
            {
                var existingFields = string.Join(", ", duplicateFields);
                return Result<CreateFieldPermissionsResultDto>.Failure(
                    new[] { $"صلاحيات الحقول التالية موجودة مسبقاً في كيان {entityName}: {existingFields}" });
            }

            var metadata = GetEntityMetadata(entityName);
            var entityDisplayName = metadata.DisplayName;
            var fieldDisplayMap = metadata.FieldDisplayMap;

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

                    var rolesInfo = await _roleRepository.QueryRoles()
                        .Where(r => roleIds.Contains(r.Id))
                        .Select(r => new RoleInfoDto
                        {
                            Id = r.Id,
                            Name = r.Name ?? string.Empty
                        })
                        .ToListAsync();

                    var usersInfo = await _userRepository.QueryUsers()
                        .Where(u => userIds.Contains(u.Id))
                        .Select(u => new UserInfoDto
                        {
                            Id = u.Id,
                            Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.UserName ?? string.Empty)
                        })
                        .ToListAsync();

                    createdDtos.Add(new FieldPermissionDto
                    {
                        Id = entity.Id,
                        Description = entity.Description,
                        EntityName = entity.EntityName,
                        FieldName = entity.FieldName,
                        AccessType = entity.AccessType,
                        AccessTypeDescription = EnumHelper.GetEnumDescription(entity.AccessType),
                        Roles = rolesInfo,
                        Users = usersInfo
                    });
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

                var resultDto = new CreateFieldPermissionsResultDto
                {
                    EntityName = entityName,
                    AccessType = dto.AccessType,
                    Permissions = createdDtos
                };

                return Result<CreateFieldPermissionsResultDto>.Success(resultDto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Result<CreateFieldPermissionsResultDto>.Failure(
                    new[] { $"حدث خطأ أثناء إنشاء صلاحيات الحقول: {ex.Message}" });
            }
        }

        public async Task<Result<string>> UpdateAsync(Guid id, UpdateFieldPermissionDto dto)
        {
            var entity = await _fieldPermissionRepository.FindByIdAsync(id);
            if (entity == null)
                return Result<string>.Failure(new[] { "Not found" });

            var oldUserLinks = await _fieldPermissionRepository.GetUserLinksAsync(id);
            var oldUserIds = oldUserLinks.Select(ufp => ufp.UserId).Distinct().ToList();

            var oldRoleLinks = await _fieldPermissionRepository.GetRoleLinksAsync(id);
            var oldRoleIds = oldRoleLinks.Select(rfp => rfp.RoleId).Distinct().ToList();

            entity.AccessType = dto.AccessType;
            var metadata = GetEntityMetadata(entity.EntityName);
            entity.Description = BuildDescription(metadata.DisplayName, metadata.FieldDisplayMap, entity.FieldName, entity.AccessType);

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

            return Result<string>.Success("تم التعديل بنجاح");
        }

        public async Task<Result<string>> DeleteAsync(Guid id)
        {
            var entity = await _fieldPermissionRepository.FindByIdAsync(id);
            if (entity == null)
                return Result<string>.Failure(new[] { "Not found" });

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

            return Result<string>.Success("تم الحذف بنجاح");
        }

        public async Task<Result<string>> ChangeAccessTypeAsync(Guid id, ChangeFieldPermissionAccessTypeDto dto)
        {
            var entity = await _fieldPermissionRepository.FindByIdAsync(id);
            if (entity == null)
                return Result<string>.Failure(new[] { "صلاحية الحقل غير موجودة" });

            var oldAccessType = entity.AccessType;
            if (oldAccessType == dto.AccessType)
                return Result<string>.Failure(new[] { "نوع الصلاحية الحالي مطابق للنوع الجديد" });

            entity.AccessType = dto.AccessType;
            var metadata = GetEntityMetadata(entity.EntityName);
            entity.Description = BuildDescription(metadata.DisplayName, metadata.FieldDisplayMap, entity.FieldName, dto.AccessType);

            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUsersWithFieldPermissionAsync(
                _fieldPermissionRepository, _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id);

            var oldAccessDescription = EnumHelper.GetEnumDescription(oldAccessType);
            var newAccessDescription = EnumHelper.GetEnumDescription(dto.AccessType);
            var message = $"تم تغيير نوع الصلاحية من {oldAccessDescription} إلى {newAccessDescription} بنجاح";

            return Result<string>.Success(message);
        }

        // =====================================================
        // Reflection: جلب الكيانات والحقول
        // =====================================================

        public Task<Result<List<EntityInfoDto>>> GetEntitiesAsync()
        {
            var types = GetAllEntityTypes();
            var entities = new Dictionary<string, EntityInfoDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in types)
            {
                var name = type.Name;
                var displayName = GetEntityDisplayName(type) ?? name;

                if (!entities.ContainsKey(name))
                {
                    entities[name] = new EntityInfoDto
                    {
                        Name = name,
                        DisplayName = displayName
                    };
                }
            }

            var ordered = entities.Values
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(Result<List<EntityInfoDto>>.Success(ordered));
        }

        public Task<Result<List<FieldInfoDto>>> GetFieldsAsync(string entityName)
        {
            var types = GetEntityTypes(entityName);
            if (types.Count == 0)
                return Task.FromResult(Result<List<FieldInfoDto>>.Failure(new[] { "Entity not found" }));

            var fieldMap = BuildFieldDisplayMap(types);

            var list = fieldMap
                .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new FieldInfoDto
                {
                    Name = kvp.Key,
                    DisplayName = kvp.Value
                })
                .ToList();

            return Task.FromResult(Result<List<FieldInfoDto>>.Success(list));
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

        private static string BuildDescription(string entityDisplayName, Dictionary<string, string> fieldDisplayMap, string fieldName, FieldAccessType accessType)
        {
            var accessDescription = EnumHelper.GetEnumDescription(accessType);
            var fieldDisplay = fieldDisplayMap.TryGetValue(fieldName, out var disp) ? disp : fieldName;
            return $"حقل {fieldDisplay} في كيان {entityDisplayName}: ({accessDescription})";
        }

        private EntityMetadata GetEntityMetadata(string entityName)
        {
            var types = GetEntityTypes(entityName);
            var displayName = types
                .Select(GetEntityDisplayName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? entityName;

            var fieldDisplayMap = BuildFieldDisplayMap(types);
            return new EntityMetadata
            {
                DisplayName = displayName,
                FieldDisplayMap = fieldDisplayMap
            };
        }

        private static List<Type> GetEntityTypes(string entityName)
        {
            return DomainEntitiesAssembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            t.GetCustomAttribute<FieldPermissionEntityAttribute>() != null &&
                            t.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static IEnumerable<Type> GetAllEntityTypes()
        {
            try
            {
                return DomainEntitiesAssembly
                    .GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<FieldPermissionEntityAttribute>() != null)
                    .ToList();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string? GetEntityDisplayName(Type type)
        {
            var domainAttr = type.GetCustomAttribute<FieldPermissionEntityAttribute>();
            if (!string.IsNullOrWhiteSpace(domainAttr?.DisplayName))
                return domainAttr!.DisplayName;

            return null;
        }

        private static Dictionary<string, string> BuildFieldDisplayMap(IEnumerable<Type> entityTypes)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dtoFieldMap = GetDtoFieldDisplayMap();

            var entityNames = entityTypes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var type in entityTypes)
            {
                var properties = type.GetProperties()
                    .Where(p => !EntityHelper.IsNavigationProperty(p) && !EntityHelper.IsKeyProperty(p));

                foreach (var property in properties)
                {
                    var fieldName = property.Name;
                    if (!map.ContainsKey(fieldName))
                    {
                        if (dtoFieldMap.TryGetValue(fieldName, out var display))
                        {
                            map[fieldName] = display;
                        }
                        else
                        {
                            map[fieldName] = fieldName;
                        }
                    }
                }
            }

            var relatedDtoFields = GetRelatedDtoFieldsCached(entityNames);
            foreach (var kvp in relatedDtoFields)
            {
                if (!map.ContainsKey(kvp.Key))
                {
                    map[kvp.Key] = kvp.Value;
                }
            }

            return map;
        }

        private static Dictionary<string, string> GetRelatedDtoFieldsCached(HashSet<string> entityNames)
        {
            var cacheKey = string.Join("|", entityNames.OrderBy(x => x));

            if (_entityDtoFieldsCache != null && _entityDtoFieldsCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var result = GetRelatedDtoFields(entityNames);

            if (_entityDtoFieldsCache == null)
            {
                _entityDtoFieldsCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
            _entityDtoFieldsCache[cacheKey] = result;

            return result;
        }

        private static Dictionary<string, string> GetRelatedDtoFields(HashSet<string> entityNames)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dtoTypes = ApplicationDtosAssembly
                    .GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract)
                    .ToList();

                var entityNameLookup = entityNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var dtoType in dtoTypes)
                {
                    var dtoName = dtoType.Name;

                    var isRelated = entityNameLookup.Any(entityName =>
                        dtoName.Contains(entityName, StringComparison.OrdinalIgnoreCase));

                    if (isRelated)
                    {
                        var properties = dtoType.GetProperties();
                        foreach (var property in properties)
                        {
                            var attr = property.GetCustomAttribute<FieldPermissionAttribute>();
                            if (attr == null) continue;

                            var fieldName = string.IsNullOrWhiteSpace(attr.FieldName)
                                ? property.Name
                                : attr.FieldName;
                            var displayName = string.IsNullOrWhiteSpace(attr.DisplayName)
                                ? fieldName
                                : attr.DisplayName!;

                            if (!result.ContainsKey(fieldName) || result[fieldName] == fieldName)
                            {
                                result[fieldName] = displayName;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore reflection issues
            }

            return result;
        }

        private static Dictionary<string, string> GetDtoFieldDisplayMap()
        {
            if (_dtoFieldDisplayCache != null)
                return _dtoFieldDisplayCache;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dtoTypes = ApplicationDtosAssembly
                    .GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract);

                foreach (var type in dtoTypes)
                {
                    foreach (var property in type.GetProperties())
                    {
                        var attr = property.GetCustomAttribute<FieldPermissionAttribute>();
                        if (attr == null) continue;

                        var fieldName = string.IsNullOrWhiteSpace(attr.FieldName)
                            ? property.Name
                            : attr.FieldName;
                        var displayName = string.IsNullOrWhiteSpace(attr.DisplayName)
                            ? fieldName
                            : attr.DisplayName!;

                        if (!result.ContainsKey(fieldName) || result[fieldName] == fieldName)
                        {
                            result[fieldName] = displayName;
                        }
                    }
                }
            }
            catch
            {
                // ignore reflection issues
            }

            _dtoFieldDisplayCache = result;
            return result;
        }

        private class EntityMetadata
        {
            public string DisplayName { get; set; } = string.Empty;
            public Dictionary<string, string> FieldDisplayMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
