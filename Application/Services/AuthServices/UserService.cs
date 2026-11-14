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
using Domain.Enums;
using PasswordPolicyRules = Application.Common.PasswordPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MapsterMapper;

namespace Application.Services.AuthServices;

public class UserService : IUserService
{
    public const string ProfileDoesNotChangeAssignmentsMessage =
        "تعديل الأدوار أو المراكز يتم من مسارات الإسناد المخصصة، وليس من تعديل البيانات العامة.";
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
    private readonly IAuditLogRepository _auditLog;

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
        ICurrentCenterService currentCenter,
        IAuditLogRepository auditLog)
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
        _auditLog = auditLog;
    }

    #region الانشاء 
    public async Task<CreatedUserResponseDto> Create(CreateUserDto createdUser)
    {
        var errors = new List<string>();

        // تحقّقات أساسية
        if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
        if (string.IsNullOrWhiteSpace(createdUser.employeeNumber)) errors.Add("الرقم الوظيفي مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.rankOrTitle)) errors.Add("الرتبة أو المسمى الوظيفي مطلوب.");
        if (!string.Equals(createdUser.password, createdUser.confirmPassword, StringComparison.Ordinal))
            errors.Add(PasswordPolicyRules.ConfirmationMismatchMessage);
        if (!PasswordPolicyRules.IsCompliant(createdUser.password))
            errors.Add(PasswordPolicyRules.ComplexityErrorMessage);
        if (errors.Count > 0) throw new InvalidUserDataException("بيانات المستخدم غير صالحة.", new { errors });

        var (employeeNumber, rankOrTitle) = NormalizeJobFields(
            createdUser.employeeNumber, createdUser.rankOrTitle);

        // منع التكرار (رمي استثناءات مخصصة)
        if (await _userManager.FindByNameAsync(createdUser.username) is not null)
            throw new UsernameAlreadyExistsException(createdUser.username);
        if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
            throw new EmailAlreadyExistsException(createdUser.email);
        await EnsureEmployeeNumberAvailableAsync(employeeNumber);

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
        var actorAssignsRolesCentrally = await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
            _access, actorId, SystemPermissions.UserManagement.AssignRoles);
        var actorCreatesCentrally = await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
            _access, actorId, SystemPermissions.UserManagement.Create);
        var rolesFromDb = await LoadRequestedRolesAsync(rawRoles);
        foreach (var role in rolesFromDb)
            AssignableRolePolicy.EnsureAssignable(actorAssignsRolesCentrally, role.Name, role.IsActive, role.Scope);

        var roleScopes = rolesFromDb.Select(r => r.Scope).ToList();
        if (roleScopes.Any(AssignableRolePolicy.IsCentralScope) &&
            roleScopes.Any(s => !AssignableRolePolicy.IsCentralScope(s)))
            throw new InvalidUserDataException("لا يمكن الجمع بين دور مركزي ودور مقيد بمركز في الطلب نفسه.");

        var membershipCenterId = await ResolveMembershipCenterAsync(actorCreatesCentrally, createdUser.centerId, actorId);

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
            // قيد iam.validateUserCenterAssignment يمنع إسناد مركز نشط لمستخدم غير نشط.
            // عند الإنشاء غير النشط يُحفظ الإسناد معلّقاً (غير نشط مع سبب ثابت) ويُفعَّل مع الحساب.
            var membership = new UserCenter
            {
                UserId = user.Id,
                CenterId = membershipCenterId,
                AssignedByUserId = actorId,
                AssignedAt = assignedAt,
                IsActive = createdUser.isActive
            };
            if (!createdUser.isActive)
            {
                membership.RevokedAt = assignedAt;
                membership.RevokedByUserId = actorId;
                membership.RevokeReason = IamRetention.AccountCreatedInactive;
            }

            _userRepository.AddUserCenters(new[] { membership });

            var associations = rolesFromDb.Select(role =>
            {
                var roleCenterId = AssignableRolePolicy.IsCentralScope(role.Scope)
                    ? (Guid?)null
                    : membershipCenterId;
                return NewRoleLink(user.Id, role.Id, roleCenterId);
            }).ToList();
            _userRepository.AddRoleAssociations(associations);

            await UpsertProfileAsync(user.Id, employeeNumber, rankOrTitle, actorId);

            await _unitOfWork.SaveChangesAsync();
            await tx.CommitAsync();

            return new CreatedUserResponseDto
            {
                fullName = createdUser.fullName,
                username = createdUser.username,
                email = createdUser.email,
                phone = createdUser.phone,
                employeeNumber = employeeNumber,
                rankOrTitle = rankOrTitle,
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
        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.Edit);

        if (statusChanging && updatedUser.IsActive == false)
        {
            await CenterManagerContinuityGuard.EnsureDeactivatingUserAsync(
                _userRepository, _roleRepository, _centers, user.Id);
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

        var (employeeNumber, rankOrTitle) = NormalizeJobFields(
            updatedUser.EmployeeNumber, updatedUser.RankOrTitle);
        await EnsureEmployeeNumberAvailableAsync(employeeNumber, user.Id);

        bool invalidateTokens = false;
        if (statusChanging)
        {
            user.IsActive = updatedUser.IsActive!.Value;
            if (!user.IsActive) invalidateTokens = true;
        }

        MarkUserModified(user);
        var updateRes = await _userManager.UpdateAsync(user);
        if (!updateRes.Succeeded) throw new InvalidUserDataException(string.Join(", ", updateRes.Errors.Select(e => e.Description)));

        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        await UpsertProfileAsync(user.Id, employeeNumber, rankOrTitle, actorId);
        await _unitOfWork.SaveChangesAsync();

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

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.Delete);

        await CenterManagerContinuityGuard.EnsureDeactivatingUserAsync(
            _userRepository, _roleRepository, _centers, user.Id);
        await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
            _userRepository, _roleRepository, user.Id);

        if (!user.IsActive)
            return;

        IamRetention.DeactivateUser(user);
        MarkUserModified(user);
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
        string? roleId = null,
        Guid? centerId = null)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentUserId = _currentUserService.UserId ?? string.Empty;

        IQueryable<User> baseQuery = await BuildManageableUsersQueryAsync();

        baseQuery = baseQuery.Where(u => u.Id != currentUserId);

        if (centerId is Guid requestedCenter)
            baseQuery = await ApplyActiveCenterFilterAsync(baseQuery, requestedCenter);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = SqlLikePatternEscape.EscapeForSqlServerLike(q.Trim());
            baseQuery = baseQuery.Where(u =>
                EF.Functions.Like((u.FullName ?? u.UserName)!, $"%{s}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{s}%")) ||
                (u.Profile != null && EF.Functions.Like(u.Profile.EmployeeNumber, $"%{s}%"))
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

        var profilesByUserId = await _userRepository.QueryProfiles()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p);

        var centersByUserId = await LoadActiveCentersByUserIdsAsync(userIds);

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
            ApplyProfileFields(item, profilesByUserId.GetValueOrDefault(u.Id));
            item.centers = centersByUserId.GetValueOrDefault(u.Id) ?? new List<AssignedCenterDto>();
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

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.Read);

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

        var profile = await _userRepository.QueryProfiles()
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        var centersByUserId = await LoadActiveCentersByUserIdsAsync(new[] { user.Id });

        var result = _mapper.Map<UserListItemDto>(user);
        ApplyProfileFields(result, profile);
        result.centers = centersByUserId.GetValueOrDefault(user.Id) ?? new List<AssignedCenterDto>();
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

        var required = new List<string> { SystemPermissions.UserManagement.Edit };
        if (statusChanging) required.Add(SystemPermissions.UserManagement.ChangeStatus);
        _permissionGate.EnsureAll(required.ToArray());
        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.Edit);

        if (dto.roles != null || dto.centerId.HasValue)
            throw new InvalidUserDataException(ProfileDoesNotChangeAssignmentsMessage);

        if (statusChanging && dto.isActive == false)
        {
            await CenterManagerContinuityGuard.EnsureDeactivatingUserAsync(
                _userRepository, _roleRepository, _centers, user.Id);
            await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
                _userRepository, _roleRepository, user.Id);
        }

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

        var (employeeNumber, rankOrTitle) = NormalizeJobFields(dto.employeeNumber, dto.rankOrTitle);
        await EnsureEmployeeNumberAvailableAsync(employeeNumber, user.Id);

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

        MarkUserModified(user);
        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        await UpsertProfileAsync(user.Id, employeeNumber, rankOrTitle, actorId);
        await _unitOfWork.SaveChangesAsync();

        if (statusChanging && !user.IsActive)
        {
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

        var centersByUserId = await LoadActiveCentersByUserIdsAsync(new[] { user.Id });
        var updated = _mapper.Map<UserListItemDto>(user);
        updated.employeeNumber = employeeNumber;
        updated.rankOrTitle = rankOrTitle;
        updated.centers = centersByUserId.GetValueOrDefault(user.Id) ?? new List<AssignedCenterDto>();
        updated.roles = rolesLite.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList();
        updated.rolesCount = rolesLite.Count;
        updated.permissionsCount = permissionsLite2.Count;
        updated.Permissions = permissionsLite2;

        return updated;
        }
        catch
        {
            throw;
        }
    }

    #endregion

    #region تعديل حالة المستخدم فقط
    public async Task<UpdateUserStatusRequest> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.ChangeStatus);

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.ChangeStatus);

        if (user.IsActive && !request.isActive)
        {
            await CenterManagerContinuityGuard.EnsureDeactivatingUserAsync(
                _userRepository, _roleRepository, _centers, user.Id);
            await IamAdminGuard.EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
                _userRepository, _roleRepository, user.Id);
        }

        var wasActive = user.IsActive;
        user.IsActive = request.isActive;
        MarkUserModified(user);

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        if (wasActive && !user.IsActive)
        {
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id,
                _auditLog, _currentUserService,
                "إبطال الجلسات بعد تعطيل الحساب",
                _currentCenter.IsCentralContext ? null : _currentCenter.CenterId);
        }
        else if (!wasActive && user.IsActive)
        {
            await ReactivateDeferredCenterMembershipsAsync(id);
        }

        return new UpdateUserStatusRequest
        {
            isActive = user.IsActive,
        };
    }
    #endregion

    #region تغيير كلمة المرور (المستخدم نفسه)
    /// <summary>
    /// MH-SEC-053: rejects reuse of the current password via Identity CheckPasswordAsync
    /// without touching PasswordHash/SecurityStamp/sessions on rejection.
    /// </summary>
    public async Task ChangeOwnPasswordAsync(ChangePasswordRequest request)
    {
        var user = await GetCurrentUserOrThrowAsync();

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            throw new InvalidUserDataException(PasswordPolicyRules.ConfirmationMismatchMessage);

        if (!PasswordPolicyRules.IsCompliant(request.NewPassword))
            throw new InvalidUserDataException(PasswordPolicyRules.ComplexityErrorMessage);

        if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
            throw new InvalidUserDataException("كلمة المرور الحالية غير صحيحة.");

        // Prefer Identity hasher APIs — never compare PasswordHash strings directly.
        if (await _userManager.CheckPasswordAsync(user, request.NewPassword))
            throw new InvalidUserDataException(PasswordPolicyRules.SameAsCurrentMessage);

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));

        if (user.MustChangePassword)
            user.MustChangePassword = false;
        MarkUserModified(user);
        var clearFlag = await _userManager.UpdateAsync(user);
        if (!clearFlag.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", clearFlag.Errors.Select(e => e.Description)));

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
        MarkUserModified(user);

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

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.ResetPassword);

        if (!user.IsActive)
            throw new InvalidUserDataException("الحساب معطّل. يجب تفعيل الحساب قبل إعادة تعيين كلمة المرور.");

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", resetResult.Errors.Select(e => e.Description)));

        user.MustChangePassword = true;
        MarkUserModified(user);
        var flagResult = await _userManager.UpdateAsync(user);
        if (!flagResult.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", flagResult.Errors.Select(e => e.Description)));

        _auditLog.Add(UserAccountAdminAudit.Create(
            _currentUserService,
            UserAccountAdminEventTypes.PasswordReset,
            user.Id,
            "إعادة تعيين كلمة المرور إدارياً مع فرض التغيير عند الدخول التالي",
            details: new { mustChangePassword = true },
            centerId: _currentCenter.IsCentralContext ? null : _currentCenter.CenterId));

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id,
            _auditLog, _currentUserService,
            "إبطال الجلسات بعد إعادة تعيين كلمة المرور",
            _currentCenter.IsCentralContext ? null : _currentCenter.CenterId);
    }
    #endregion

    #region اسناد وحذف ادوار الى مستخدم 
    public async Task ToggleRolesForUserAsync(AssignRolesDto dto)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignRoles);

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.AssignRoles);

        var requested = MinimumActiveRoleGuard.CollectTokens(dto.RoleIds, dto.RoleNames);
        MinimumActiveRoleGuard.EnsureRequestContainsRoleToken(requested);

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

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.AssignPermissions);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        var scopeCenterId = await ResolveDirectPermissionScopeAsync(actorId, user.Id, dto.CenterId);

        var incomingIds = new HashSet<Guid>();
        if (dto.PermissionId is { Count: > 0 })
            foreach (var pid in dto.PermissionId.Where(x => x != Guid.Empty))
                incomingIds.Add(pid);

        var activeIncomingIds = await _permissionRepository.GetActiveIdsAsync(incomingIds);
        var currentPermIds = await _userRepository.GetActiveDirectPermissionIdsAsync(user.Id, scopeCenterId);

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

        var actorPermissions = _permissionGate.GetPermissions();
        var grantingNames = (await _permissionRepository.GetActiveByIdsAsync(toAdd))
            .Select(p => p.Name);
        IamAdminGuard.EnsureAssignableWithinActorPermissions(grantingNames, actorPermissions);

        List<UserPermission>? removeRows = null;
        if (toRemove.Count > 0)
        {
            removeRows = await _userRepository.QueryUserPermissions(asNoTracking: false)
                .Include(up => up.Permission)
                .Where(up => up.UserId == user.Id && up.IsActive && up.CenterId == scopeCenterId && toRemove.Contains(up.PermissionId))
                .ToListAsync();

            IamAdminGuard.EnsureRevocableWithinActorPermissions(
                removeRows.Select(r => r.Permission?.Name),
                actorPermissions);
        }

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (removeRows is { Count: > 0 })
            {
                RevokeDirectPermissions(removeRows);
                await _unitOfWork.SaveChangesAsync();
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(pid => NewPermissionLink(user.Id, pid, scopeCenterId));
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

    /// <summary>
    /// يطابق الصلاحيات المباشرة الفعّالة داخل النطاق مع المجموعة النهائية.
    /// لا يمس صلاحيات الأدوار ولا إسنادات المراكز الأخرى، ولا يبدّل الختم إن لم يتغيّر شيء.
    /// </summary>
    public async Task<DirectPermissionSyncResultDto> SyncDirectPermissionsAsync(SyncDirectPermissionsDto dto)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignPermissions);

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.AssignPermissions);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        var scopeCenterId = await ResolveDirectPermissionScopeAsync(actorId, user.Id, dto.CenterId);

        var requested = (dto.PermissionIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requested.Count > 0)
        {
            var knownRequested = await _permissionRepository.GetActiveByIdsAsync(requested);
            if (knownRequested.Count != requested.Count)
                throw new InvalidUserDataException("لا يمكن إسناد صلاحية غير موجودة أو غير فعّالة.");
        }

        var currentRows = await _userRepository.QueryUserPermissions(asNoTracking: false)
            .Include(up => up.Permission)
            .Where(up => up.UserId == user.Id && up.IsActive && up.CenterId == scopeCenterId)
            .ToListAsync();

        var actorPermissions = _permissionGate.GetPermissions();
        var requestedSet = requested.ToHashSet();
        var currentIds = currentRows.Select(row => row.PermissionId).ToHashSet();
        var toAdd = requested.Where(id => !currentIds.Contains(id)).ToList();

        // Omissions from the final set are revoke candidates. Outside-delegation grants stay
        // active but are reported in SkippedRevokes so the client is not told they were withdrawn.
        var candidateRemovals = currentRows
            .Where(row => !requestedSet.Contains(row.PermissionId))
            .ToList();
        var toRemove = candidateRemovals
            .Where(row => actorPermissions.Contains(
                row.Permission?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var skippedRevokes = candidateRemovals
            .Where(row => !actorPermissions.Contains(
                row.Permission?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .GroupBy(row => row.PermissionId)
            .Select(g => new SkippedDirectPermissionRevokeDto
            {
                PermissionId = g.Key,
                Name = g.Select(r => r.Permission?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? string.Empty
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PermissionId)
            .ToList();

        if (toAdd.Count > 0)
        {
            var grantingPermissions = await _permissionRepository.GetActiveByIdsAsync(toAdd);
            IamAdminGuard.EnsureAssignableWithinActorPermissions(
                grantingPermissions.Select(p => p.Name), actorPermissions);
        }

        var finalIds = new HashSet<Guid>(currentIds);
        foreach (var row in toRemove)
            finalIds.Remove(row.PermissionId);
        foreach (var id in toAdd)
            finalIds.Add(id);

        var finalPermissions = await _permissionRepository.GetActiveByIdsAsync(finalIds);
        PermissionDependencyRules.EnsureReadBeforeWriteActions(
            finalPermissions.Select(p => p.Name),
            message => new InvalidUserDataException(message));

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            return new DirectPermissionSyncResultDto
            {
                SkippedRevokes = skippedRevokes
            };
        }

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (toRemove.Count > 0)
                RevokeDirectPermissions(toRemove);

            if (toAdd.Count > 0)
            {
                var inactive = await _userRepository.QueryUserPermissions(asNoTracking: false)
                    .Where(up => up.UserId == user.Id
                        && !up.IsActive
                        && up.CenterId == scopeCenterId
                        && toAdd.Contains(up.PermissionId))
                    .ToListAsync();

                foreach (var row in inactive)
                    IamRetention.Reactivate(row, actorId, DateTimeOffset.UtcNow);

                var restored = inactive.Select(row => row.PermissionId).ToHashSet();
                var inserts = toAdd
                    .Where(id => !restored.Contains(id))
                    .Select(id => NewPermissionLink(user.Id, id, scopeCenterId));
                _userRepository.AddUserPermissions(inserts);
            }

            await _unitOfWork.SaveChangesAsync();

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

        return new DirectPermissionSyncResultDto
        {
            Added = toAdd.Count,
            Removed = toRemove.Count,
            SessionsInvalidated = true,
            SkippedRevokes = skippedRevokes
        };
    }

    /// <summary>
    /// يعيد الصلاحيات التي يجوز للمنفّذ منحها/سحبها مباشرة ضمن نطاق المستهدف،
    /// مع الصلاحيات المباشرة الحالية في ذلك النطاق (مقفولة إن خرجت عن التفويض).
    /// قراءة فقط — لا تكتب ولا تبطل جلسات.
    /// </summary>
    public async Task<AssignablePermissionsDto> GetAssignablePermissionsAsync(
        string userId,
        Guid? centerId = null,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignPermissions);

        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.AssignPermissions);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        var scopeCenterId = await ResolveDirectPermissionScopeAsync(actorId, user.Id, centerId);

        var actorPermissionNames = _permissionGate.GetPermissions()
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalog = await _permissionRepository.GetAllOrderedAsync(cancellationToken);
        var assignable = catalog
            .Where(p => p.IsActive
                && !string.IsNullOrWhiteSpace(p.Name)
                && actorPermissionNames.Contains(p.Name!))
            .Select(ToAssignableItem)
            .OrderBy(p => p.ModuleLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.NameAr, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentRows = await _userRepository.QueryUserPermissions(asNoTracking: true)
            .Include(up => up.Permission)
            .Where(up => up.UserId == user.Id && up.IsActive && up.CenterId == scopeCenterId)
            .ToListAsync(cancellationToken);

        var currentDirect = currentRows
            .Where(row => row.Permission is not null && !string.IsNullOrWhiteSpace(row.Permission.Name))
            .Select(row =>
            {
                var permission = row.Permission!;
                var name = permission.Name!;
                var within = actorPermissionNames.Contains(name);
                var moduleKey = PermissionDependencyRules.GetModuleKey(name);
                return new CurrentDirectPermissionItemDto
                {
                    Id = permission.Id,
                    Name = name,
                    NameAr = AuthDisplayNames.PermissionAr(name),
                    Description = string.IsNullOrWhiteSpace(permission.Description)
                        ? AuthDisplayNames.PermissionAr(name)
                        : permission.Description,
                    ModuleKey = moduleKey,
                    ModuleLabel = AuthDisplayNames.ModuleAr(moduleKey),
                    WithinActorDelegation = within,
                    CanRevoke = within,
                    IsLocked = !within
                };
            })
            .OrderBy(p => p.IsLocked)
            .ThenBy(p => p.ModuleLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.NameAr, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AssignablePermissionsDto
        {
            UserId = user.Id,
            ScopeCenterId = scopeCenterId,
            Assignable = assignable,
            CurrentDirect = currentDirect
        };
    }

    private static AssignablePermissionItemDto ToAssignableItem(Permission permission)
    {
        var name = permission.Name ?? string.Empty;
        var moduleKey = PermissionDependencyRules.GetModuleKey(name);
        return new AssignablePermissionItemDto
        {
            Id = permission.Id,
            Name = name,
            NameAr = AuthDisplayNames.PermissionAr(name),
            Description = string.IsNullOrWhiteSpace(permission.Description)
                ? AuthDisplayNames.PermissionAr(name)
                : permission.Description,
            ModuleKey = moduleKey,
            ModuleLabel = AuthDisplayNames.ModuleAr(moduleKey),
            CanGrant = true,
            CanRevoke = true
        };
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

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.RemoveFieldPermissions);

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

        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Edit);

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        await EnsureCanManageTargetAsync(user.Id, SystemPermissions.UserManagement.Edit);

        if (updatedUser.RoleIds != null || updatedUser.CenterId.HasValue)
            throw new InvalidUserDataException(ProfileDoesNotChangeAssignmentsMessage);

        var safeUserName = _textSanitizer.SanitizePlainText(updatedUser.UserName) ?? updatedUser.UserName;
        var safeEmail = _textSanitizer.SanitizePlainText(updatedUser.Email) ?? updatedUser.Email;
        user.UserName = safeUserName;
        user.NormalizedUserName = safeUserName.ToUpperInvariant();
        user.Email = safeEmail;
        user.NormalizedEmail = safeEmail.ToUpperInvariant();
        MarkUserModified(user);

        var updateRes = await _userManager.UpdateAsync(user);
        if (!updateRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updateRes.Errors.Select(e => e.Description)));

        await _unitOfWork.SaveChangesAsync();
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

    private async Task EnsureCanManageTargetAsync(string targetUserId, string operationPermission)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanManageTargetAsync(
            _access, _userRepository, actorId, targetUserId, operationPermission);
    }

    /// <summary>
    /// يضيّق القائمة بإسناد مركز فعّال قبل العد. المدير المحلي يُقبل فقط لمركز الجلسة.
    /// التجاوز المركزي يتطلب UserManagement.Read بنطاق مركزي.
    /// </summary>
    private async Task<IQueryable<User>> ApplyActiveCenterFilterAsync(IQueryable<User> query, Guid centerId)
    {
        if (centerId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        var actorId = _currentUserService.UserId ?? string.Empty;
        var unrestricted = await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
            _access, actorId, SystemPermissions.UserManagement.Read);
        if (!unrestricted)
        {
            if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid active || active != centerId)
                throw ForbiddenPermissionException.AccessDenied(
                    "لا يمكن عرض مستخدمي مركز غير المركز النشط.",
                    "UserService.ApplyActiveCenterFilter",
                    "client-center-mismatch",
                    SystemPermissions.UserManagement.Read,
                    centerId: centerId);

            if (!await _access.IsActivelyAssignedAsync(actorId, centerId))
                throw ForbiddenPermissionException.AccessDenied(
                    "لا يمكن عرض مستخدمي مركز غير مسند لك.",
                    "UserService.ApplyActiveCenterFilter",
                    "actor-center-unassigned",
                    SystemPermissions.UserManagement.Read,
                    centerId: centerId);
        }
        else
        {
            _ = await _centers.GetByIdAsync(centerId, asNoTracking: true)
                ?? throw new MedicalCenterNotFoundException();
        }

        var assignedUserIds = _userRepository.QueryUserCenters()
            .Where(uc => uc.CenterId == centerId && uc.IsActive && uc.Center.IsActive)
            .Select(uc => uc.UserId);
        return query.Where(u => assignedUserIds.Contains(u.Id));
    }

    private async Task<IQueryable<User>> BuildManageableUsersQueryAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        if (await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
                _access, actorId, SystemPermissions.UserManagement.Read))
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

        /// <summary>
        /// يعيد تفعيل إسنادات المراكز المعلّقة عند إنشاء حساب غير نشط بعد تفعيل المستخدم.
        /// </summary>
        private async Task ReactivateDeferredCenterMembershipsAsync(string userId)
        {
            var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
            // ToList متزامن ليعمل مع EF ومع مستودعات الاختبار التي لا توفّر IAsyncEnumerable.
            var deferred = _userRepository.QueryUserCenters(asNoTracking: false)
                .Where(uc =>
                    uc.UserId == userId
                    && !uc.IsActive
                    && uc.RevokeReason == IamRetention.AccountCreatedInactive)
                .ToList();

            if (deferred.Count == 0)
                return;

            if (_centers is null)
                throw new InvalidOperationException("مستودع المراكز غير متاح لإعادة تفعيل الإسناد.");

            var assignedAt = DateTimeOffset.UtcNow;
            foreach (var row in deferred)
            {
                var center = await _centers.GetByIdAsync(row.CenterId, asNoTracking: true);
                MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
                IamRetention.Reactivate(row, actorId, assignedAt);
            }

            await _unitOfWork.SaveChangesAsync();
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

        private UserPermission NewPermissionLink(string userId, Guid permissionId, Guid? centerId) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionId = permissionId,
            CenterId = centerId,
            GrantedByUserId = AssignmentStamp.RequireActor(_currentUserService.UserId),
            GrantedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        private static void MarkUserModified(User user) => user.UpdatedAt = DateTimeOffset.UtcNow;

        private (string EmployeeNumber, string RankOrTitle) NormalizeJobFields(
            string? employeeNumber,
            string? rankOrTitle)
        {
            var emp = _textSanitizer.SanitizePlainText(employeeNumber)?.Trim() ?? string.Empty;
            var rank = _textSanitizer.SanitizePlainText(rankOrTitle)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emp))
                throw new InvalidUserDataException("الرقم الوظيفي مطلوب.");
            if (string.IsNullOrWhiteSpace(rank))
                throw new InvalidUserDataException("الرتبة أو المسمى الوظيفي مطلوب.");
            return (emp, rank);
        }

        private async Task EnsureEmployeeNumberAvailableAsync(string employeeNumber, string? excludeUserId = null)
        {
            if (await _userRepository.EmployeeNumberExistsAsync(employeeNumber, excludeUserId))
                throw new InvalidUserDataException("الرقم الوظيفي مستخدم مسبقاً.");
        }

        private async Task UpsertProfileAsync(
            string userId,
            string employeeNumber,
            string rankOrTitle,
            string actorId)
        {
            var now = DateTimeOffset.UtcNow;
            var profile = await _userRepository.FindTrackedProfileAsync(userId);
            if (profile is null)
            {
                _userRepository.AddProfile(new UserProfile
                {
                    UserId = userId,
                    EmployeeNumber = employeeNumber,
                    RankOrTitle = rankOrTitle,
                    UpdatedByUserId = actorId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                return;
            }

            profile.EmployeeNumber = employeeNumber;
            profile.RankOrTitle = rankOrTitle;
            profile.UpdatedByUserId = actorId;
            profile.UpdatedAt = now;
        }

        private static bool IsPlaceholderJobData(UserProfile profile)
            => string.Equals(profile.RankOrTitle, "غير محدد", StringComparison.Ordinal)
               || string.Equals(profile.EmployeeNumber, profile.UserId, StringComparison.OrdinalIgnoreCase);

        private static void ApplyProfileFields(UserListItemDto item, UserProfile? profile)
        {
            if (profile is null || IsPlaceholderJobData(profile))
                return;
            item.employeeNumber = profile.EmployeeNumber;
            item.rankOrTitle = profile.RankOrTitle;
        }

        private async Task<Dictionary<string, List<AssignedCenterDto>>> LoadActiveCentersByUserIdsAsync(
            IReadOnlyCollection<string> userIds)
        {
            if (userIds.Count == 0)
                return new Dictionary<string, List<AssignedCenterDto>>();

            var rows = await _userRepository.QueryUserCenters()
                .Where(uc => userIds.Contains(uc.UserId) && uc.IsActive && uc.Center.IsActive)
                .OrderBy(uc => uc.Center.NameAr)
                .Select(uc => new
                {
                    uc.UserId,
                    Center = new AssignedCenterDto
                    {
                        CenterId = uc.CenterId,
                        Code = uc.Center.Code,
                        NameAr = uc.Center.NameAr
                    }
                })
                .ToListAsync();

            return rows
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Center).ToList());
        }

        private async Task<Guid?> ResolveDirectPermissionScopeAsync(
            string actorId,
            string targetUserId,
            Guid? requestedCenterId)
        {
            if (requestedCenterId == Guid.Empty)
                throw new InvalidUserDataException("معرف المركز غير صالح.");

            var actorIsCentral = await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
                _access, actorId, SystemPermissions.UserManagement.AssignPermissions);
            if (!actorIsCentral)
            {
                if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid active || active == Guid.Empty)
                    throw ForbiddenPermissionException.AccessDenied(
                        "لا يوجد مركز نشط لإسناد الصلاحية.",
                        "UserService.ResolveDirectPermissionScope",
                        "no-active-center",
                        SystemPermissions.UserManagement.AssignPermissions,
                        targetUserId: targetUserId);

                if (requestedCenterId is Guid requested && requested != active)
                    throw ForbiddenPermissionException.AccessDenied(
                        "لا يمكنك إسناد صلاحية خارج مركزك النشط.",
                        "UserService.ResolveDirectPermissionScope",
                        "client-center-mismatch",
                        SystemPermissions.UserManagement.AssignPermissions,
                        targetUserId: targetUserId,
                        centerId: active);

                if (!await _access.IsActivelyAssignedAsync(targetUserId, active))
                    throw ForbiddenPermissionException.AccessDenied(
                        "لا يمكن منح صلاحية خارج مركز مسند للمستخدم.",
                        "UserService.ResolveDirectPermissionScope",
                        "target-center-unassigned",
                        SystemPermissions.UserManagement.AssignPermissions,
                        targetUserId: targetUserId,
                        centerId: active);

                return active;
            }

            if (requestedCenterId is null)
                return null;

            var center = await _centers.GetByIdAsync(requestedCenterId.Value, asNoTracking: true);
            MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
            if (!await _access.IsActivelyAssignedAsync(targetUserId, requestedCenterId.Value))
                throw ForbiddenPermissionException.AccessDenied(
                    "لا يمكن منح صلاحية خارج مركز مسند للمستخدم.",
                    "UserService.ResolveDirectPermissionScope",
                    "target-center-unassigned",
                    SystemPermissions.UserManagement.AssignPermissions,
                    targetUserId: targetUserId,
                    centerId: requestedCenterId);

            return requestedCenterId;
        }

        private async Task<Guid> ResolveMembershipCenterAsync(
            bool actorIsCentral,
            Guid? requestedCenterId,
            string actorId)
        {
            Guid centerId;
            if (!actorIsCentral)
            {
                if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid active || active == Guid.Empty)
                    throw ForbiddenPermissionException.AccessDenied(
                        "لا يوجد مركز نشط لإنشاء المستخدم.",
                        "UserService.ResolveMembershipCenter",
                        "no-active-center",
                        SystemPermissions.UserManagement.Create);

                if (requestedCenterId is Guid requested && requested != active)
                    throw ForbiddenPermissionException.AccessDenied(
                        "لا يمكنك إنشاء مستخدم في مركز خارج مركزك النشط.",
                        "UserService.ResolveMembershipCenter",
                        "client-center-mismatch",
                        SystemPermissions.UserManagement.Create,
                        centerId: active);

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
                throw ForbiddenPermissionException.AccessDenied(
                    "لا يمكنك إنشاء مستخدم خارج نطاق تفويضك.",
                    "UserService.ResolveMembershipCenter",
                    "actor-center-unassigned",
                    SystemPermissions.UserManagement.Create,
                    centerId: centerId);

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
            var actorIsCentral = await UserManagementScopeGuard.ActorHasCentralOperationScopeAsync(
                _access, actorId, SystemPermissions.UserManagement.AssignRoles);
            var roles = await LoadRequestedRolesAsync(roleIdsOrNames);
            foreach (var role in roles)
                AssignableRolePolicy.EnsureAssignable(actorIsCentral, role.Name, role.IsActive, role.Scope);

            var scopeCenterId = AssignableRolePolicy.ResolveSyncScope(
                actorIsCentral,
                roles.Select(r => r.Scope).ToList(),
                requestedCenterId,
                _currentCenter.CenterId,
                _currentCenter.IsCentralContext);

            if (roles.Count > 0 && scopeCenterId is Guid scopeId)
            {
                var center = await _centers.GetByIdAsync(scopeId, asNoTracking: true);
                MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
            }

            await UserManagementScopeGuard.EnsureCanAssignRoleScopeAsync(
                _access, actorId, scopeCenterId, SystemPermissions.UserManagement.AssignRoles);

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
                .Where(row => AssignableRolePolicy.MayRevoke(
                    actorIsCentral,
                    row.Role?.Name,
                    scopeCenterId,
                    row.Role?.Scope ?? RoleScope.Center))
                .ToList();

            if (toAdd.Count > 0 || toRemove.Count > 0)
            {
                var allActiveRows = await _userRepository.QueryRoleAssociations(asNoTracking: true)
                    .Include(ura => ura.Role)
                    .Where(ura => ura.UserId == userId && ura.IsActive && ura.Role != null && ura.Role.IsActive)
                    .ToListAsync();
                var allActive = allActiveRows
                    .Select(ura => new MinimumActiveRoleGuard.ActiveRoleKey(ura.RoleId, ura.CenterId));
                var removing = toRemove.Select(r => new MinimumActiveRoleGuard.ActiveRoleKey(r.RoleId, r.CenterId));
                var adding = toAdd.Select(r => new MinimumActiveRoleGuard.ActiveRoleKey(r.Id, scopeCenterId));
                MinimumActiveRoleGuard.EnsureProjectedActiveRolesRemain(allActive, removing, adding);

                if (toRemove.Count > 0)
                {
                    foreach (var row in toRemove)
                    {
                        await CenterManagerContinuityGuard.EnsureRevokingRoleLinkAsync(
                            _userRepository,
                            _roleRepository,
                            _centers,
                            userId,
                            row.Role?.Name,
                            row.CenterId);
                    }

                    RevokeRoleAssignments(toRemove);
                }

                if (toAdd.Count > 0)
                    _userRepository.AddRoleAssociations(toAdd.Select(r => NewRoleLink(userId, r.Id, scopeCenterId)));

                await _unitOfWork.SaveChangesAsync();
                await MinimumActiveRoleGuard.EnsurePersistedActiveRoleRemainsAsync(_userRepository, userId);
            }
        }
}
