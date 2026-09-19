using Application.Common;
using Application.Common.Results;
using Application.Dtos.CoreDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using MapsterMapper;
using Domain.Entities.Core;

namespace Application.Services.Core;

public class UserOrgScopeService : IUserOrgScopeService
{
    private readonly IUserOrgScopeRepository _scopes;
    private readonly IOrgUnitRepository _units;
    private readonly IPermissionRepository _permissions;
    private readonly IUserRepository _users;
    private readonly IUserPermissionService _userPermissions;
    private readonly IOrgScopeResolver _resolver;
    private readonly IUserContextService _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UserOrgScopeService(IUserOrgScopeRepository scopes, IOrgUnitRepository units,
        IPermissionRepository permissions, IUserRepository users, IUserPermissionService userPermissions,
        IOrgScopeResolver resolver, IUserContextService userContext, IUnitOfWork unitOfWork, IMapper mapper) =>
        (_scopes, _units, _permissions, _users, _userPermissions, _resolver, _userContext, _unitOfWork, _mapper) =
        (scopes, units, permissions, users, userPermissions, resolver, userContext, unitOfWork, mapper);

    public async Task<UserOrgScopeDto> GetByIdAsync(Guid id) =>
        _mapper.Map<UserOrgScopeDto>(await _scopes.GetDetailedAsync(id) ?? throw new UserOrgScopeNotFoundException());

    public async Task<PaginatedList<UserOrgScopeDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, string? status = null)
    {
        (pageNumber, pageSize) = (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100));
        var page = await _scopes.GetPagedAsync(pageNumber, pageSize, q, status);
        return new(_mapper.Map<List<UserOrgScopeDto>>(page.Items), page.TotalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<UserOrgScopeDto>> GetByUserIdAsync(string userId)
    {
        if (await _users.GetByIdAsync(userId, true) is null)
            throw new InvalidOrgStructureDataException("المستخدم غير موجود.");
        return _mapper.Map<List<UserOrgScopeDto>>(await _scopes.GetByUserIdAsync(userId));
    }

    public async Task<IReadOnlyList<UserOrgScopeDto>> GetMyScopesAsync()
    {
        var userId = RequireCurrentUser();
        var scopes = await _scopes.GetByUserIdAsync(userId, true);
        return _mapper.Map<List<UserOrgScopeDto>>(scopes.Where(s =>
            !string.IsNullOrWhiteSpace(s.Permission?.Name) && _userPermissions.HasPermission(userId, s.Permission.Name)));
    }

    public async Task<IReadOnlyList<AccessibleOrgUnitDto>> GetMyAccessibleOrgUnitsAsync(string permissionName)
    {
        var userId = RequireCurrentUser();
        if (string.IsNullOrWhiteSpace(permissionName))
            throw new InvalidOrgStructureDataException("اسم الصلاحية مطلوب.");
        var permission = permissionName.Trim();
        if (!_resolver.UserHasBasePermission(userId, permission))
            throw new InvalidOrgStructureDataException("ليس لديك الصلاحية المطلوبة. نطاق المستخدم التنظيمي لا يمنح صلاحيات إضافية.");
        var ids = await _resolver.GetAccessibleOrgUnitIdsAsync(userId, permission);
        if (ids.Count == 0) return Array.Empty<AccessibleOrgUnitDto>();
        var permissionId = await _permissions.FindActivePermissionIdByNameAsync(permission);
        var directScopes = permissionId.HasValue
            ? await _scopes.GetActiveScopesForPermissionAsync(userId, permissionId.Value)
            : Array.Empty<OrgScopeLink>();
        var directModes = directScopes.GroupBy(x => x.OrgUnitId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.ScopeMode == ScopeModes.Subtree) ? ScopeModes.Subtree : ScopeModes.Self);
        return (await _units.GetByIdsAsync(ids)).Select(unit => new AccessibleOrgUnitDto
        {
            OrgUnitId = unit.OrgUnitId,
            NameAr = unit.NameAr,
            Code = unit.Code,
            ScopeMode = directModes.TryGetValue(unit.OrgUnitId, out var mode) ? mode : ScopeModes.Subtree
        }).ToList();
    }

    public async Task<UserOrgScopeDto> CreateAsync(CreateUserOrgScopeDto dto)
    {
        await ValidateAsync(dto.UserId, dto.PermissionId, dto.OrgUnitId, dto.ScopeMode, dto.Status, null);
        var entity = _mapper.Map<UserOrgScope>(dto);
        entity.UserOrgScopeId = Guid.NewGuid();
        entity.AssignedAt = DateTime.Now;
        entity.AssignedByUserId = _userContext.UserId;
        _scopes.Add(entity);
        await _unitOfWork.SaveChangesAsync();
        return await GetByIdAsync(entity.UserOrgScopeId);
    }

    public async Task UpdateAsync(Guid id, UpdateUserOrgScopeDto dto)
    {
        var entity = await _scopes.GetByIdAsync(id, true) ?? throw new UserOrgScopeNotFoundException();
        await ValidateAsync(entity.UserId, dto.PermissionId, dto.OrgUnitId, dto.ScopeMode, dto.Status, id);
        _mapper.Map(dto, entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _scopes.GetByIdAsync(id, true) ?? throw new UserOrgScopeNotFoundException();
        _scopes.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task ValidateAsync(string userId, Guid permissionId, Guid orgUnitId, string scopeMode, string status, Guid? excludeId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(userId)) errors.Add("معرف المستخدم مطلوب.");
        else if (await _users.GetByIdAsync(userId, true) is null) errors.Add("المستخدم غير موجود.");
        var permission = await _permissions.FindByIdAsync(permissionId);
        if (permission is null || !permission.IsActive) errors.Add("الصلاحية غير موجودة أو غير نشطة.");
        else if (!string.IsNullOrWhiteSpace(userId) && !_userPermissions.HasPermission(userId, permission.Name!))
            errors.Add("المستخدم لا يمتلك الصلاحية المحددة عبر الأدوار أو الصلاحيات المباشرة. سجل نطاق المستخدم لا يمنح صلاحيات جديدة.");
        if (!await _units.ExistsAsync(orgUnitId, true)) errors.Add("الوحدة التنظيمية غير موجودة أو مؤرشفة.");
        var normalizedScope = (scopeMode ?? "").Trim().ToUpperInvariant();
        if (!ScopeModes.All.Contains(normalizedScope)) errors.Add("نمط النطاق غير صالح.");
        var normalizedStatus = (status ?? "").Trim().ToUpperInvariant();
        if (!CoreStatuses.UserOrgScopeStatuses.Contains(normalizedStatus)) errors.Add("حالة نطاق المستخدم غير صالحة.");
        if (errors.Count == 0 && await _scopes.DuplicateExistsAsync(userId, permissionId, orgUnitId, normalizedScope, excludeId))
            errors.Add("يوجد نطاق مطابق للمستخدم والصلاحية والوحدة التنظيمية مسبقاً.");
        if (errors.Count > 0) throw new InvalidOrgStructureDataException(string.Join("، ", errors));
    }

    private string RequireCurrentUser() =>
        string.IsNullOrWhiteSpace(_userContext.UserId)
            ? throw new InvalidOrgStructureDataException("المستخدم الحالي غير معروف.")
            : _userContext.UserId;
}
