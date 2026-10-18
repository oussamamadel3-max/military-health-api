using Application.Common.Security;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Common.Localization;
using Application.Common.Results;
using Application.Common.Permissions;
using Application.Exceptions;
using Application.Dtos.AuthDtos;
using Domain.Entities.Auth;
using PasswordPolicyRules = Application.Common.PasswordPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MapsterMapper;

namespace Application.Services.AuthServices;

public class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IFieldPermissionRepository _fieldPermissionRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAccessContextRepository _access;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly JwtSettings _jwtSettings;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITextInputSanitizer _textSanitizer;
    private readonly IMapper _mapper;
    private readonly IMedicalCenterRepository _centers;
    private readonly ICurrentCenterService _currentCenter;

    public UserService(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IFieldPermissionRepository fieldPermissionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IAccessContextRepository access,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        JwtSettings jwtSettings,
        ICurrentUserService currentUserService,
        ITextInputSanitizer textSanitizer,
        IMapper mapper,
        IMedicalCenterRepository centers,
        ICurrentCenterService currentCenter)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _fieldPermissionRepository = fieldPermissionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _access = access;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _jwtSettings = jwtSettings;
        _currentUserService = currentUserService;
        _textSanitizer = textSanitizer;
        _mapper = mapper;
        _centers = centers;
        _currentCenter = currentCenter;
    }

    #region الانشاء 
    public async Task<CreatedUserResponseDto> Create(CreateUserDto createdUser)
    {
        var errors = new List<string>();

        // تحقّقات أساسية
        if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
        if (!string.Equals(createdUser.password, createdUser.confirmPassword, StringComparison.Ordinal))
            errors.Add(PasswordPolicyRules.ConfirmationMismatchMessage);
        if (!PasswordPolicyRules.IsCompliant(createdUser.password))
            errors.Add(PasswordPolicyRules.ComplexityErrorMessage);
        if (errors.Count > 0) throw new InvalidUserDataException("بيانات المستخدم غير صالحة.", new { errors });

        // منع التكرار (رمي استثناءات مخصصة)
        if (await _userManager.FindByNameAsync(createdUser.username) is not null)
            throw new UsernameAlreadyExistsException(createdUser.username);
        if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
            throw new EmailAlreadyExistsException(createdUser.email);

        var rawRoles = (createdUser.roles ?? Enumerable.Empty<string>())
                          .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (rawRoles.Count == 0)
            throw new InvalidUserDataException("يجب اختيار دور واحد على الأقل.");

        _permissionGate.EnsureAll(
            SystemPermissions.UserManagement.Create,
            SystemPermissions.UserManagement.AssignRoles,
            MilitaryHealthPermissions.UserCenters.Manage);

        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        var actorIsCentral = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(_access, actorId);
        var rolesFromDb = await LoadRequestedRolesAsync(rawRoles);
        foreach (var role in rolesFromDb)
            AssignableRolePolicy.EnsureAssignable(actorIsCentral, role.Name, role.IsActive);

        var roleNames = rolesFromDb.Select(r => r.Name!).ToList();
        if (roleNames.Any(AssignableRolePolicy.IsCentralScopeRole) &&
            roleNames.Any(name => !AssignableRolePolicy.IsCentralScopeRole(name)))
            throw new InvalidUserDataException("لا يمكن الجمع بين دور مركزي ودور مقيد بمركز في الطلب نفسه.");

        var membershipCenterId = await ResolveMembershipCenterAsync(actorIsCentral, createdUser.centerId, actorId);

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var user = _mapper.Map<User>(createdUser);
            user.UserName = _textSanitizer.SanitizePlainText(createdUser.username) ?? createdUser.username;
            user.Email = _textSanitizer.SanitizePlainText(createdUser.email) ?? createdUser.email;
            user.PhoneNumber = string.IsNullOrEmpty(createdUser.phone) ? null : _textSanitizer.SanitizePlainText(createdUser.phone);
            user.IsActive = createdUser.isActive;
            user.MustChangePassword = createdUser.forcePasswordChange;
            user.CreationDate = DateTime.Now;
            user.FullName = _textSanitizer.SanitizePlainText(createdUser.fullName) ?? string.Empty;

            var createRes = await _userManager.CreateAsync(user, createdUser.password);
            if (!createRes.Succeeded)
            {
                if (createRes.Errors.Any(e =>
                        string.Equals(e.Code, "DuplicateUserName", StringComparison.OrdinalIgnoreCase)))
                    throw new UsernameAlreadyExistsException(createdUser.username);

                var createErrors = createRes.Errors.Select(e => e.Description);
                throw new InvalidUserDataException(string.Join(", ", createErrors));
            }

            var assignedAt = DateTimeOffset.UtcNow;
            _userRepository.AddUserCenters(new[]
            {
                new UserCenter
                {
                    UserId = user.Id,
                    CenterId = membershipCenterId,
                    IsActive = true,
                    AssignedByUserId = actorId,
                    AssignedAt = assignedAt
                }
            });

            var associations = rolesFromDb.Select(role =>
            {
                var roleCenterId = AssignableRolePolicy.IsCentralScopeRole(role.Name)
                    ? (Guid?)null
                    : membershipCenterId;
                return NewRoleLink(user.Id, role.Id, roleCenterId);
            }).ToList();
            _userRepository.AddRoleAssociations(associations);

            await _unitOfWork.SaveChangesAsync();
            await tx.CommitAsync();

            return new CreatedUserResponseDto
            {
                fullName = createdUser.fullName,
                username = createdUser.username,
                email = createdUser.email,
                phone = createdUser.phone,
                isActive = createdUser.isActive,
                mustChangePassword = user.MustChangePassword,
                centerId = membershipCenterId,
                roles = createdUser.roles?.ToList() ?? new List<string>()
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region التعديل الكامل    
    public async Task Update(string id, UpdateUserDto updatedUser)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        var required = new List<string> { SystemPermissions.UserManagement.Edit };
        var statusChanging = updatedUser.IsActive.HasValue && updatedUser.IsActive.Value != user.IsActive;
        if (statusChanging)
            required.Add(SystemPermissions.UserManagement.ChangeStatus);

        _permissionGate.EnsureAll(required.ToArray());
        await EnsureCanManageTargetAsync(user.Id);

        if (statusChanging && updatedUser.IsActive == false)
        {
            await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
                _userRepository, _roleRepository, user.Id);
        }

        if (!string.IsNullOrWhiteSpace(updatedUser.Email) &&
            !string.Equals(updatedUser.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailDup = await _userManager.FindByEmailAsync(updatedUser.Email);
            if (emailDup != null && emailDup.Id != user.Id)
                throw new EmailAlreadyExistsException(updatedUser.Email);

            var setEmail = await _userManager.SetEmailAsync(user, updatedUser.Email);
            if (!setEmail.Succeeded) throw new InvalidUserDataException(string.Join(", ", setEmail.Errors.Select(e => e.Description)));
        }

        if (!string.IsNullOrWhiteSpace(updatedUser.UserName) &&
            !string.Equals(updatedUser.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var userNameDup = await _userManager.FindByNameAsync(updatedUser.UserName);
            if (userNameDup != null && userNameDup.Id != user.Id)
                throw new UsernameAlreadyExistsException(updatedUser.UserName);

            var setUserName = await _userManager.SetUserNameAsync(user, updatedUser.UserName);
            if (!setUserName.Succeeded) throw new InvalidUserDataException(string.Join(", ", setUserName.Errors.Select(e => e.Description)));
        }

        if (!string.IsNullOrWhiteSpace(updatedUser.FullName))
            user.FullName = _textSanitizer.SanitizePlainText(updatedUser.FullName) ?? updatedUser.FullName;

        if (!string.IsNullOrWhiteSpace(updatedUser.Phone))
            user.PhoneNumber = _textSanitizer.SanitizePlainText(updatedUser.Phone) ?? updatedUser.Phone;

        bool invalidateTokens = false;
        if (statusChanging)
        {
            user.IsActive = updatedUser.IsActive!.Value;
            if (!user.IsActive) invalidateTokens = true;
        }

        var updateRes = await _userManager.UpdateAsync(user);
        if (!updateRes.Succeeded) throw new InvalidUserDataException(string.Join(", ", updateRes.Errors.Select(e => e.Description)));

        if (invalidateTokens)
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
    }
    #endregion

    #region الحذف
    public async Task Delete(string userId)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Delete);

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        await EnsureCanManageTargetAsync(user.Id);

        await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
            _userRepository, _roleRepository, user.Id);

        if (!user.IsActive)
            return;

        IamRetention.DeactivateUser(user);
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
    }
    #endregion

    #region جلب الكل 
    public async Task<PaginatedList<UserListItemDto>> GetUsersListAsync(
        int pageNumber,
        int pageSize,
        string? q = null,
        bool? isActive = null,
        string? roleId = null)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentUserId = _currentUserService.UserId ?? string.Empty;

        IQueryable<User> baseQuery = await BuildManageableUsersQueryAsync();

        baseQuery = baseQuery.Where(u => u.Id != currentUserId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = SqlLikePatternEscape.EscapeForSqlServerLike(q.Trim());
            baseQuery = baseQuery.Where(u =>
                EF.Functions.Like((u.FullName ?? u.UserName)!, $"%{s}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{s}%"))
            );
        }

        if (isActive.HasValue)
            baseQuery = baseQuery.Where(u => u.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(roleId))
        {
            var rid = roleId.Trim();
            baseQuery = baseQuery.Where(u =>
                u.UserRoleAssociations.Any(ura => ura.RoleId == rid));
        }

        baseQuery = baseQuery.OrderBy(u => u.UserName);

        var totalCount = await baseQuery.CountAsync();

        var usersPage = await baseQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        if (usersPage.Count == 0)
            return new PaginatedList<UserListItemDto>(new List<UserListItemDto>(), totalCount, pageNumber, pageSize);

        var userIds = usersPage.Select(u => u.Id).ToList();

        var roleAssociations = await _userRepository.QueryRoleAssociations()
            .Where(ura => userIds.Contains(ura.UserId))
            .Include(ura => ura.Role)
            .ToListAsync();

        var rolesLookup = roleAssociations
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => _mapper.Map<List<RoleLiteDto>>(g.ToList())
            );

        var roleIdsOnPage = roleAssociations.Select(x => x.RoleId).Distinct().ToList();

        var rolePermsLookup = await _roleRepository.GetRolePermissionsForRolesAsync(roleIdsOnPage);
        var rolePermsGrouped = rolePermsLookup
            .Where(rp => rp.Permission != null && rp.Permission.Name != null)
            .GroupBy(x => x.RoleId)
            .ToDictionary(
                g => g.Key,
                g => _mapper.Map<List<PermissionLiteDto>>(g.ToList())
            );

        var directPermEntities = await _userRepository.QueryUserPermissions()
            .Where(up => userIds.Contains(up.UserId))
            .Include(up => up.Permission)
            .ToListAsync();

        var directPermsLookup = directPermEntities
            .Where(up => up.Permission?.Name != null)
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => _mapper.Map<List<PermissionLiteDto>>(g.ToList())
            );

        var items = usersPage.Select(u =>
        {
            var userRoles = rolesLookup.TryGetValue(u.Id, out var rr) ? rr : new List<RoleLiteDto>();
            var permMap = new Dictionary<string, PermissionLiteDto>(StringComparer.OrdinalIgnoreCase);

            if (directPermsLookup.TryGetValue(u.Id, out var direct))
                foreach (var p in direct) permMap[p.PermissionId] = p;

            foreach (var r in userRoles)
                if (rolePermsGrouped.TryGetValue(r.RoleId, out var rperms))
                    foreach (var p in rperms) permMap[p.PermissionId] = p;

            var item = _mapper.Map<UserListItemDto>(u);
            item.rolesCount = userRoles.Count;
            item.permissionsCount = permMap.Count;
            item.roles = userRoles;
            item.Permissions = permMap.Values
                .OrderBy(p => p.PermissionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return item;
        }).ToList();

        return new PaginatedList<UserListItemDto>(items, totalCount, pageNumber, pageSize);
    }
    #endregion

    #region جلب حسب المعرف 
    public async Task<UserListItemDto> GetUserAsync(string id)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        var user = await _userRepository.QueryUsers()
            .FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new UserNotFoundException(id);

        await EnsureCanManageTargetAsync(user.Id);

        var roleAssociations = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id)
            .Include(ura => ura.Role)
            .ToListAsync();
        var roles = _mapper.Map<List<RoleLiteDto>>(roleAssociations);

        var roleIds = roles.Select(r => r.RoleId).ToList();

        var rolePermissionEntities = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Where(rp => rp.Permission != null && !string.IsNullOrWhiteSpace(rp.Permission.Name))
            .ToList();

        var directPermissionEntities = await _userRepository.QueryUserPermissions()
            .Where(up => up.UserId == user.Id)
            .Include(up => up.Permission)
            .Where(up => up.Permission != null && !string.IsNullOrWhiteSpace(up.Permission.Name))
            .ToListAsync();

        var permissionsLite = _mapper.Map<List<PermissionLiteDto>>(directPermissionEntities)
            .Concat(_mapper.Map<List<PermissionLiteDto>>(rolePermissionEntities))
            .GroupBy(p => p.PermissionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.PermissionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = _mapper.Map<UserListItemDto>(user);
        result.roles = roles;
        result.rolesCount = roles.Count;
        result.permissionsCount = permissionsLite.Count;
        result.Permissions = permissionsLite;
        return result;
    }

    #endregion

    #region التعديل الجزئي 
    public async Task<UserListItemDto> UpdateUserAsync(string id, UpdateUserPatchRequest dto)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        var statusChanging = dto.isActive.HasValue && dto.isActive.Value != user.IsActive;
        var rolesProvided = dto.roles != null;

        var required = new List<string> { SystemPermissions.UserManagement.Edit };
        if (statusChanging) required.Add(SystemPermissions.UserManagement.ChangeStatus);
        if (rolesProvided) required.Add(SystemPermissions.UserManagement.AssignRoles);
        _permissionGate.EnsureAll(required.ToArray());
        await EnsureCanManageTargetAsync(user.Id);

        if (statusChanging && dto.isActive == false)
        {
            await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
                _userRepository, _roleRepository, user.Id);
        }

        IDbContextTransaction? roleTx = null;
        if (rolesProvided)
            roleTx = await _unitOfWork.BeginTransactionAsync();

        try
        {
        if (!string.IsNullOrWhiteSpace(dto.username) &&
            !string.Equals(dto.username, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var existsUserName = await _userManager.FindByNameAsync(dto.username);
            if (existsUserName is not null && existsUserName.Id != user.Id)
                throw new UsernameAlreadyExistsException(dto.username);
        }

        if (!string.IsNullOrWhiteSpace(dto.email) &&
            !string.Equals(dto.email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existsEmail = await _userManager.FindByEmailAsync(dto.email);
            if (existsEmail is not null && existsEmail.Id != user.Id)
                throw new EmailAlreadyExistsException(dto.email);
        }

        if (dto.fullName is not null) dto.fullName = _textSanitizer.SanitizePlainText(dto.fullName) ?? dto.fullName;
        if (dto.username is not null) dto.username = _textSanitizer.SanitizePlainText(dto.username) ?? dto.username;
        if (dto.email is not null) dto.email = _textSanitizer.SanitizePlainText(dto.email) ?? dto.email;
        if (dto.phone is not null) dto.phone = _textSanitizer.SanitizePlainText(dto.phone) ?? dto.phone;

        var previousIsActive = user.IsActive;
        _mapper.Map(dto, user);
        if (!dto.isActive.HasValue)
            user.IsActive = previousIsActive;
        else if (!statusChanging)
            user.IsActive = previousIsActive;

        user.FullName = _textSanitizer.SanitizePlainText(user.FullName) ?? user.FullName;
        if (user.UserName is not null)
            user.UserName = _textSanitizer.SanitizePlainText(user.UserName) ?? user.UserName;
        if (user.Email is not null)
            user.Email = _textSanitizer.SanitizePlainText(user.Email) ?? user.Email;
        if (user.PhoneNumber is not null)
            user.PhoneNumber = _textSanitizer.SanitizePlainText(user.PhoneNumber) ?? user.PhoneNumber;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        if (statusChanging && !user.IsActive)
        {
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
        }

        if (rolesProvided)
        {
            var requestedTokens = (dto.roles ?? new())
                .Where(r => r is not null)
                .Select(r => !string.IsNullOrWhiteSpace(r.RoleId) ? r.RoleId! : r.RoleName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            await ApplyScopedRoleSyncAsync(user.Id, requestedTokens, dto.centerId);

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, user.Id);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, user.Id, afterPerms);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
        }

        var roleAssociationEntities = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id && ura.IsActive)
            .Include(ura => ura.Role)
            .ToListAsync();
        var rolesLite = _mapper.Map<List<RoleLiteDto>>(roleAssociationEntities);

        var roleIds = rolesLite.Select(r => r.RoleId).ToList();

        var rolePermEntities = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Where(rp => rp.Permission != null && rp.Permission.IsActive)
            .ToList();

        var directPermEntities2 = await _userRepository.QueryUserPermissions()
            .Where(up => up.UserId == user.Id && up.Permission != null && up.Permission.IsActive)
            .Include(up => up.Permission)
            .ToListAsync();

        var permissionsLite2 = _mapper.Map<List<PermissionLiteDto>>(directPermEntities2)
            .Concat(_mapper.Map<List<PermissionLiteDto>>(rolePermEntities))
            .GroupBy(p => p.PermissionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.PermissionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updated = _mapper.Map<UserListItemDto>(user);
        updated.roles = rolesLite.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList();
        updated.rolesCount = rolesLite.Count;
        updated.permissionsCount = permissionsLite2.Count;
        updated.Permissions = permissionsLite2;

        if (roleTx is not null)
            await roleTx.CommitAsync();

        return updated;
        }
        catch
        {
            if (roleTx is not null)
                await roleTx.RollbackAsync();
            throw;
        }
        finally
        {
            if (roleTx is not null)
                await roleTx.DisposeAsync();
        }
    }

    #endregion

    #region تعديل حالة المستخدم فقط
    public async Task<UpdateUserStatusRequest> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.ChangeStatus);

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        await EnsureCanManageTargetAsync(user.Id);

        if (user.IsActive && !request.isActive)
        {
            await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
                _userRepository, _roleRepository, user.Id);
        }

        var wasActive = user.IsActive;
        user.IsActive = request.isActive;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        if (wasActive && !user.IsActive)
        {
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id);
        }

        return new UpdateUserStatusRequest
        {
            isActive = user.IsActive,
        };
    }
    #endregion

    #region تغيير كلمة المرور (المستخدم نفسه)
    public async Task ChangeOwnPasswordAsync(ChangePasswordRequest request)
    {
        var user = await GetCurrentUserOrThrowAsync();

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new InvalidUserDataException(PasswordPolicyRules.ConfirmationMismatchMessage);

        if (!PasswordPolicyRules.IsCompliant(request.NewPassword))
            throw new InvalidUserDataException(PasswordPolicyRules.ComplexityErrorMessage);

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var clearFlag = await _userManager.UpdateAsync(user);
            if (!clearFlag.Succeeded)
                throw new InvalidUserDataException(string.Join(", ", clearFlag.Errors.Select(e => e.Description)));
        }

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
    }
    #endregion

    #region الملف الشخصي (المستخدم نفسه)
    public async Task UpdateMyProfileAsync(UpdateMyProfileRequest request)
    {
        var user = await GetCurrentUserOrThrowAsync();

        var fullName = _textSanitizer.SanitizePlainText(request.FullName)?.Trim() ?? string.Empty;
        var email = request.Email.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : _textSanitizer.SanitizePlainText(request.Phone)?.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
            throw new InvalidUserDataException("الاسم الكامل مطلوب.");

        var existingByEmail = await _userManager.FindByEmailAsync(email);
        if (existingByEmail is not null &&
            !string.Equals(existingByEmail.Id, user.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new EmailAlreadyExistsException(email);
        }

        user.FullName = fullName;
        user.Email = email;
        user.NormalizedEmail = _userManager.NormalizeEmail(email);
        user.PhoneNumber = phone;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private async Task<User> GetCurrentUserOrThrowAsync()
    {
        var userId = _currentUserService.UserId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthenticatedUserException();

        return await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);
    }
    #endregion

    #region إعادة تعيين كلمة المرور (الإداري)
    public async Task ResetUserPasswordAsync(AdminChangePasswordRequest request)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.ResetPassword);

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new InvalidUserDataException(PasswordPolicyRules.ConfirmationMismatchMessage);

        if (!PasswordPolicyRules.IsCompliant(request.NewPassword))
            throw new InvalidUserDataException(PasswordPolicyRules.ComplexityErrorMessage);

        var user = await _userManager.FindByIdAsync(request.UserId)
            ?? throw new UserNotFoundException(request.UserId);

        await EnsureCanManageTargetAsync(user.Id);

        if (!user.IsActive)
            throw new InvalidUserDataException("الحساب معطّل. يجب تفعيل الحساب قبل إعادة تعيين كلمة المرور.");

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", resetResult.Errors.Select(e => e.Description)));

        user.MustChangePassword = true;
        var flagResult = await _userManager.UpdateAsync(user);
        if (!flagResult.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", flagResult.Errors.Select(e => e.Description)));

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
    }
    #endregion

    #region اسناد وحذف ادوار الى مستخدم 
    public async Task ToggleRolesForUserAsync(AssignRolesDto dto)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignRoles);

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        await EnsureCanManageTargetAsync(user.Id);

        var requested = new List<string>();
        if (dto.RoleIds is { Count: > 0 })
            requested.AddRange(dto.RoleIds.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (dto.RoleNames is { Count: > 0 })
            requested.AddRange(dto.RoleNames.Where(n => !string.IsNullOrWhiteSpace(n))!);

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await ApplyScopedRoleSyncAsync(user.Id, requested, dto.CenterId);

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, user.Id);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, user.Id, afterPerms);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region اسناد وحذف صلاحيات الى مستخدم مباشرة
    public async Task TogglePermissionsForUserAsync(AssignPermissionToUserDto dto)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignPermissions);

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        await EnsureCanManageTargetAsync(user.Id);

        var incomingIds = new HashSet<Guid>();
        if (dto.PermissionId is { Count: > 0 })
            foreach (var pid in dto.PermissionId.Where(x => x != Guid.Empty))
                incomingIds.Add(pid);

        var activeIncomingIds = await _permissionRepository.GetActiveIdsAsync(incomingIds);
        var currentPermIds = await _userRepository.GetDirectPermissionIdsAsync(user.Id);

        var currentSet = new HashSet<Guid>(currentPermIds);

        // الواجهة ترسل المعرّفات المتغيّرة فقط (تبديل)، فيُحسب الناتج النهائي ثم يُتحقق منه
        var toAdd = activeIncomingIds.Where(id => !currentSet.Contains(id)).ToList();
        var toRemove = activeIncomingIds.Where(id => currentSet.Contains(id)).ToList();

        if (!toAdd.Any() && !toRemove.Any())
            return;

        var finalSet = new HashSet<Guid>(currentSet);
        foreach (var id in toRemove) finalSet.Remove(id);
        foreach (var id in toAdd) finalSet.Add(id);

        var finalPermissions = await _permissionRepository.GetActiveByIdsAsync(finalSet);
        PermissionDependencyRules.EnsureReadBeforeWriteActions(
            finalPermissions.Select(p => p.Name),
            message => new InvalidUserDataException(message));

        var grantingNames = (await _permissionRepository.GetActiveByIdsAsync(toAdd))
            .Select(p => p.Name);
        IamAdminGuard.EnsureAssignableWithinActorPermissions(grantingNames, _permissionGate.GetPermissions());

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (toRemove.Any())
            {
                var removeRows = await _userRepository.QueryUserPermissions(asNoTracking: false)
                    .Where(up => up.UserId == user.Id && up.IsActive && toRemove.Contains(up.PermissionId))
                    .ToListAsync();

                RevokeDirectPermissions(removeRows);
                await _unitOfWork.SaveChangesAsync();
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(pid => NewPermissionLink(user.Id, pid));
                _userRepository.AddUserPermissions(addRows);
                await _unitOfWork.SaveChangesAsync();
            }

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, user.Id);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, user.Id, afterPerms);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region إزالة الصلاحيات الحقلية من مستخدم
    public async Task RemoveFieldPermissionsFromUserAsync(RemoveFieldPermissionsDto dto)
    {
        if (dto.FieldPermissionIds == null || dto.FieldPermissionIds.Count == 0)
            throw new InvalidUserDataException("لم يتم تحديد أي صلاحيات حقول.");

        _permissionGate.EnsureAll(SystemPermissions.UserManagement.RemoveFieldPermissions);

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        await EnsureCanManageTargetAsync(user.Id);

        var userFieldPermissions = (await _userRepository.GetUserFieldPermissionsAsync(dto.UserId))
            .Where(ufp => dto.FieldPermissionIds.Contains(ufp.FieldPermissionId))
            .ToList();

        if (!userFieldPermissions.Any())
            return;

        _userRepository.RemoveUserFieldPermissions(userFieldPermissions);
        await _unitOfWork.SaveChangesAsync();

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, dto.UserId);
    }
    #endregion

    #region تحديث المستخدم بالأدوار
    public async Task UpdateUserWithRolesAsync(string userId, UpdateUserWithRolesDto updatedUser)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        _permissionGate.EnsureAll(
            SystemPermissions.UserManagement.Edit,
            SystemPermissions.UserManagement.AssignRoles);

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        await EnsureCanManageTargetAsync(user.Id);

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var safeUserName = _textSanitizer.SanitizePlainText(updatedUser.UserName) ?? updatedUser.UserName;
            var safeEmail = _textSanitizer.SanitizePlainText(updatedUser.Email) ?? updatedUser.Email;
            user.UserName = safeUserName;
            user.NormalizedUserName = safeUserName.ToUpperInvariant();
            user.Email = safeEmail;
            user.NormalizedEmail = safeEmail.ToUpperInvariant();

            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
                throw new InvalidUserDataException(string.Join(", ", updateRes.Errors.Select(e => e.Description)));

            if (updatedUser.RoleIds is not null)
                await ApplyScopedRoleSyncAsync(userId, updatedUser.RoleIds, updatedUser.CenterId);

            await _unitOfWork.SaveChangesAsync();

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, userId);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, userId, afterPerms);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, userId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region القائمة المنسدلة للمستخدمين
    public async Task<List<DropdownUserDto>> GetDropDownUsersAsync()
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        var users = await (await BuildManageableUsersQueryAsync())
            .Where(u => u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return _mapper.Map<List<DropdownUserDto>>(users);
    }
    #endregion

    private async Task EnsureCanManageTargetAsync(string targetUserId)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanManageTargetAsync(
            _access, _userRepository, actorId, targetUserId);
    }

    private async Task<IQueryable<User>> BuildManageableUsersQueryAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        if (await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(_access, actorId))
            return _userRepository.QueryUsers();

        return _userRepository.QueryUsersManageableByLocalActor(actorId);
    }

        private void RevokeRoleAssignments(IEnumerable<UserRoleAssociation> rows)
        {
            var actor = AssignmentStamp.RequireActor(_currentUserService.UserId);
            foreach (var row in rows)
                IamRetention.Revoke(row, actor, IamRetention.RoleWithdrawn);
        }

        private void RevokeDirectPermissions(IEnumerable<UserPermission> rows)
        {
            var actor = AssignmentStamp.RequireActor(_currentUserService.UserId);
            foreach (var row in rows)
                IamRetention.Revoke(row, actor, IamRetention.DirectPermissionWithdrawn);
        }

        private UserRoleAssociation NewRoleLink(string userId, string roleId, Guid? centerId) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            AssignedByUserId = AssignmentStamp.RequireActor(_currentUserService.UserId),
            AssignedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        private UserPermission NewPermissionLink(string userId, Guid permissionId) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            GrantedByUserId = AssignmentStamp.RequireActor(_currentUserService.UserId),
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        private async Task<Guid> ResolveMembershipCenterAsync(
            bool actorIsCentral,
            Guid? requestedCenterId,
            string actorId)
        {
            Guid centerId;
            if (!actorIsCentral)
            {
                if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid active || active == Guid.Empty)
                    throw new ForbiddenPermissionException("لا يوجد مركز نشط لإنشاء المستخدم.");

                if (requestedCenterId is Guid requested && requested != active)
                    throw new ForbiddenPermissionException("لا يمكنك إنشاء مستخدم في مركز خارج مركزك النشط.");

                centerId = active;
            }
            else
            {
                if (requestedCenterId is not Guid requested || requested == Guid.Empty)
                    throw new InvalidUserDataException("يجب تحديد مركز فعّال للمستخدم.");

                centerId = requested;
            }

            var center = await _centers.GetByIdAsync(centerId, asNoTracking: true);
            MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

            if (!actorIsCentral && !await _access.IsActivelyAssignedAsync(actorId, centerId))
                throw new ForbiddenPermissionException("لا يمكنك إنشاء مستخدم خارج نطاق تفويضك.");

            return centerId;
        }

        private async Task<List<Role>> LoadRequestedRolesAsync(IEnumerable<string> idsOrNames)
        {
            var raw = idsOrNames
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (raw.Count == 0)
                return new List<Role>();

            var upper = raw.Select(s => s.ToUpperInvariant()).ToList();
            var found = await _roleRepository.QueryRoles()
                .Where(r =>
                    (r.Id != null && upper.Contains(r.Id.ToUpper())) ||
                    (r.Name != null && upper.Contains(r.Name.ToUpper())))
                .ToListAsync();

            var foundKeys = found
                .SelectMany(r => new[] { r.Id, r.Name })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.ToUpperInvariant())
                .ToHashSet();
            var missing = raw.Where(x => !foundKeys.Contains(x.ToUpperInvariant())).ToList();
            if (missing.Count > 0)
                throw new InvalidUserDataException($"أدوار غير موجودة أو غير نشطة: {string.Join(", ", missing)}");

            var inactive = found.Where(r => !r.IsActive).Select(r => r.Name ?? r.Id).ToList();
            if (inactive.Count > 0)
                throw new InvalidUserDataException($"لا يمكن إسناد دور معطّل: {string.Join(", ", inactive)}");

            return found;
        }

        private async Task ApplyScopedRoleSyncAsync(
            string userId,
            IEnumerable<string> roleIdsOrNames,
            Guid? requestedCenterId)
        {
            var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
            var actorIsCentral = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(_access, actorId);
            var roles = await LoadRequestedRolesAsync(roleIdsOrNames);
            foreach (var role in roles)
                AssignableRolePolicy.EnsureAssignable(actorIsCentral, role.Name, role.IsActive);

            var scopeCenterId = AssignableRolePolicy.ResolveSyncScope(
                actorIsCentral,
                roles.Select(r => r.Name!).ToList(),
                requestedCenterId,
                _currentCenter.CenterId,
                _currentCenter.IsCentralContext);

            if (roles.Count > 0 && scopeCenterId is Guid scopeId)
            {
                var center = await _centers.GetByIdAsync(scopeId, asNoTracking: true);
                MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
            }

            await UserManagementScopeGuard.EnsureCanAssignRoleScopeAsync(_access, actorId, scopeCenterId);

            var desiredIds = roles.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scopeRows = await _userRepository.QueryRoleAssociations(asNoTracking: false)
                .Include(ura => ura.Role)
                .Where(ura => ura.UserId == userId && ura.IsActive && ura.CenterId == scopeCenterId)
                .ToListAsync();

            var toAdd = roles.Where(r => scopeRows.All(row => !string.Equals(row.RoleId, r.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (toAdd.Count > 0 && scopeCenterId is Guid centerId &&
                !await _access.IsActivelyAssignedAsync(userId, centerId))
            {
                throw new InvalidUserDataException(
                    "لا يمكن إسناد دور مقيد بمركز دون وجود إسناد فعّال للمستخدم إلى ذلك المركز.");
            }

            var toRemove = scopeRows
                .Where(row => !desiredIds.Contains(row.RoleId))
                .Where(row => AssignableRolePolicy.MayRevoke(actorIsCentral, row.Role?.Name, scopeCenterId))
                .ToList();

            if (toRemove.Count > 0)
                RevokeRoleAssignments(toRemove);

            if (toAdd.Count > 0)
                _userRepository.AddRoleAssociations(toAdd.Select(r => NewRoleLink(userId, r.Id, scopeCenterId)));

            if (toAdd.Count > 0 || toRemove.Count > 0)
                await _unitOfWork.SaveChangesAsync();
        }
}
