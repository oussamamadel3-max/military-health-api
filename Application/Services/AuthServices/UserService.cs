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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AutoMapper;

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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActorScopeProvider _actorScopeProvider;
    private readonly JwtSettings _jwtSettings;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITextInputSanitizer _textSanitizer;
    private readonly IMapper _mapper;

    public UserService(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IFieldPermissionRepository fieldPermissionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IActorScopeProvider actorScopeProvider,
        JwtSettings jwtSettings,
        ICurrentUserService currentUserService,
        ITextInputSanitizer textSanitizer,
        IMapper mapper)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _fieldPermissionRepository = fieldPermissionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _actorScopeProvider = actorScopeProvider;
        _jwtSettings = jwtSettings;
        _currentUserService = currentUserService;
        _textSanitizer = textSanitizer;
        _mapper = mapper;
    }

    #region الانشاء 
    public async Task<CreateUserDto> Create(CreateUserDto createdUser)
    {
        var errors = new List<string>();

        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        // تحقّقات أساسية
        if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
        if (errors.Count > 0) throw new InvalidUserDataException("بيانات المستخدم غير صالحة.", new { errors });

        // منع التكرار (رمي استثناءات مخصصة)
        if (await _userManager.FindByNameAsync(createdUser.username) is not null)
            throw new UsernameAlreadyExistsException(createdUser.username);
        if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
            throw new EmailAlreadyExistsException(createdUser.email);

        // جلب الأدوار الفعالة (كما عندك)
        var rawRoles = createdUser.roles.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rolesFromDb = await _roleRepository.ResolveActiveRolesAsync(rawRoles);

        var foundUpper = rolesFromDb
            .SelectMany(r => new[] { (r.Id ?? "").ToUpperInvariant(), (r.Name ?? "").ToUpperInvariant() })
            .ToHashSet();
        var missing = rawRoles.Where(x => !foundUpper.Contains(x.ToUpperInvariant())).ToList();
        if (missing.Any())
            throw new InvalidUserDataException($"أدوار غير موجودة أو غير نشطة: {string.Join(", ", missing)}");

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بإنشاء المستخدمين.");

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var user = _mapper.Map<User>(createdUser);
            user.UserName = _textSanitizer.SanitizePlainText(createdUser.username) ?? createdUser.username;
            user.Email = _textSanitizer.SanitizePlainText(createdUser.email) ?? createdUser.email;
            user.PhoneNumber = string.IsNullOrEmpty(createdUser.phone) ? null : _textSanitizer.SanitizePlainText(createdUser.phone);
            user.IsActive = createdUser.isActive;
            user.CreationDate = DateTime.Now;
            user.FullName = _textSanitizer.SanitizePlainText(createdUser.fullName) ?? string.Empty;

            var createRes = await _userManager.CreateAsync(user, createdUser.password);
            if (!createRes.Succeeded)
            {
                var createErrors = createRes.Errors.Select(e => e.Description);
                throw new InvalidUserDataException(string.Join(", ", createErrors));
            }

            var associations = rolesFromDb.Select(r => new UserRoleAssociation { UserId = user.Id, RoleId = r.Id }).ToList();
            _userRepository.AddRoleAssociations(associations);

            var roleIds = rolesFromDb.Select(r => r.Id).ToList();
            var rolePermissions = await _roleRepository.GetActivePermissionIdsForRolesAsync(roleIds);

            var existingUserPermissions = await _userRepository.GetDirectPermissionIdsAsync(user.Id);

            var newPermissions = rolePermissions
                .Where(p => !existingUserPermissions.Contains(p))
                .Select(p => new UserPermission { UserId = user.Id, PermissionId = p })
                .ToList();

            if (newPermissions.Any()) _userRepository.AddUserPermissions(newPermissions);

            await _unitOfWork.SaveChangesAsync();
            await tx.CommitAsync();

            return createdUser;
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
        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بتعديل المستخدمين.");

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

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
        if (updatedUser.IsActive.HasValue && updatedUser.IsActive.Value != user.IsActive)
        {
            user.IsActive = updatedUser.IsActive.Value;
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
        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بحذف المستخدمين.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }
    #endregion

    #region جلب الكل 
    public async Task<PaginatedList<UserListItemDto>> GetUsersListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentUserId = _currentUserService.UserId ?? string.Empty;

        var actorScope = await _actorScopeProvider.GetScopeAsync(currentUserId);
        bool isSuperAdmin = actorScope.IsSuperAdmin;
        bool isBranchManger = actorScope.IsBranchManager;

        IQueryable<User> baseQuery = _userRepository.QueryUsers();

        if (!isSuperAdmin && !isBranchManger)
            baseQuery = baseQuery.Where(u => false);

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

        baseQuery = baseQuery.OrderBy(u => u.UserName);

        var totalCount = await baseQuery.CountAsync();

        var usersPage = await baseQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                FullName = u.FullName ?? u.UserName,
                u.UserName,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.CreationDate
            })
            .ToListAsync();

        if (usersPage.Count == 0)
            return new PaginatedList<UserListItemDto>(new List<UserListItemDto>(), totalCount, pageNumber, pageSize);

        var userIds = usersPage.Select(u => u.Id).ToList();

        var rolesByUser = await _userRepository.QueryRoleAssociations()
            .Where(ura => userIds.Contains(ura.UserId))
            .Select(ura => new { ura.UserId, ura.RoleId, RoleName = ura.Role.Name })
            .ToListAsync();

        var rolesLookup = rolesByUser
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new RoleLiteDto(x.RoleId, AuthDisplayNames.RoleAr(x.RoleName ?? string.Empty))).ToList()
            );

        var roleIdsOnPage = rolesByUser.Select(x => x.RoleId).Distinct().ToList();

        var rolePermsLookup = await _roleRepository.GetRolePermissionsForRolesAsync(roleIdsOnPage);
        var rolePermsGrouped = rolePermsLookup
            .Where(rp => rp.Permission != null && rp.Permission.Name != null)
            .GroupBy(x => x.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new PermissionLiteDto(x.PermissionId.ToString(), AuthDisplayNames.PermissionAr(x.Permission!.Name!)))
                      .ToList()
            );

        var directPermsLookup = await _userRepository.QueryUserPermissions()
            .Where(up => userIds.Contains(up.UserId))
            .Select(up => new { up.UserId, up.PermissionId, PermissionName = up.Permission.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Where(x => x.PermissionId.HasValue && x.PermissionName != null)
                      .Select(x => new PermissionLiteDto(x.PermissionId!.Value.ToString(), AuthDisplayNames.PermissionAr(x.PermissionName!)))
                      .ToList()
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

            return new UserListItemDto
            {
                userId = u.Id,
                fullName = u.FullName ?? string.Empty,
                username = u.UserName ?? string.Empty,
                email = u.Email ?? string.Empty,
                phone = u.PhoneNumber,
                isActive = u.IsActive,
                createdAt = u.CreationDate,
                rolesCount = userRoles.Count,
                permissionsCount = permMap.Count,
                roles = userRoles,
                Permissions = permMap.Values
                                       .OrderBy(p => p.PermissionName, StringComparer.OrdinalIgnoreCase)
                                       .ToList()
            };
        }).ToList();

        return new PaginatedList<UserListItemDto>(items, totalCount, pageNumber, pageSize);
    }
    #endregion

    #region جلب حسب المعرف 
    public async Task<UserListItemDto> GetUserAsync(string id)
    {
        var (actor, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        var user = await _userRepository.QueryUsers()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.CreationDate,
                FullName = u.FullName
            })
            .FirstOrDefaultAsync()
            ?? throw new UserNotFoundException(id);

        if (!actorIsSuperAdmin && !actorIsBranchManager && actor.Id != user.Id)
            throw new ForbiddenPermissionException("غير مسموح بالوصول.");

        var roles = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty))
            .ToListAsync();

        var roleIds = roles.Select(r => r.RoleId).ToList();

        var rolePermissions = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Select(rp => new { rp.PermissionId, rp.Permission!.Name })
            .ToList();

        var directPermissions = await _userRepository.QueryUserPermissions()
            .Where(up => up.UserId == user.Id)
            .Select(up => new { up.PermissionId, up.Permission.Name })
            .ToListAsync();

        var permissionPairs = directPermissions
            .Where(p => p.PermissionId.HasValue && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new { Id = p.PermissionId!.Value, Name = p.Name! })
            .Concat(
                rolePermissions
                    .Where(p => p.PermissionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new { Id = p.PermissionId, Name = p.Name! })
            );

        var permissionMap = permissionPairs
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<Guid>.Default);

        var permissionsLite = permissionMap
            .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new PermissionLiteDto(kvp.Key.ToString(), kvp.Value))
            .ToList();

        return new UserListItemDto
        {
            userId = user.Id,
            fullName = user.FullName ?? user.UserName ?? string.Empty,
            username = user.UserName ?? string.Empty,
            email = user.Email ?? string.Empty,
            phone = user.PhoneNumber,
            isActive = user.IsActive,
            createdAt = user.CreationDate,
            roles = roles,
            rolesCount = roles.Count,
            permissionsCount = permissionsLite.Count,
            Permissions = permissionsLite
        };
    }

    #endregion

    #region التعديل الجزئي 
    public async Task<UserListItemDto> UpdateUserAsync(string id, UpdateUserPatchRequest dto)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بالتعديل إلا لمدير الفرع أو SuperAdmin.");

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

        if (dto.fullName is not null) user.FullName = _textSanitizer.SanitizePlainText(dto.fullName) ?? dto.fullName;
        if (dto.username is not null) user.UserName = _textSanitizer.SanitizePlainText(dto.username) ?? dto.username;
        if (dto.email is not null) user.Email = _textSanitizer.SanitizePlainText(dto.email) ?? dto.email;
        if (dto.phone is not null) user.PhoneNumber = _textSanitizer.SanitizePlainText(dto.phone) ?? dto.phone;
        if (dto.isActive.HasValue) user.IsActive = dto.isActive.Value;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        if (dto.roles != null)
        {
            if (!isSuperAdmin)
            {
                var superAdminRoleId = await _roleRepository.GetSuperAdminRoleIdAsync();
                bool touchesSuperAdmin =
                    (dto.roles.Any(r => !string.IsNullOrWhiteSpace(r.RoleName) &&
                                        r.RoleName.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)))
                    ||
                    (dto.roles.Any(r => !string.IsNullOrWhiteSpace(r.RoleId) &&
                                        r.RoleId == superAdminRoleId));

                if (touchesSuperAdmin)
                    throw new ForbiddenPermissionException("غير مسموح لمدير الفرع بمنح/سحب دور SuperAdmin.");
            }

            var current = await _userRepository.GetRoleAssociationsAsync(user.Id);
            var requested = (dto.roles ?? new()).Where(r => r is not null).ToList();

            var incomingIds = requested
                .Where(r => !string.IsNullOrWhiteSpace(r.RoleId))
                .Select(r => r.RoleId!)
                .ToList();

            var incomingNames = requested
                .Where(r => string.IsNullOrWhiteSpace(r.RoleId) && !string.IsNullOrWhiteSpace(r.RoleName))
                .Select(r => r.RoleName!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rolesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (incomingNames.Any())
            {
                var foundByName = await _roleRepository.QueryRoles()
                    .Where(r => r.Name != null && incomingNames.Contains(r.Name))
                    .Select(r => new { r.Id, r.Name })
                    .ToListAsync();

                var validByName = foundByName.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
                rolesByName = validByName.ToDictionary(x => x.Name!, x => x.Id, StringComparer.OrdinalIgnoreCase);

                var missingByName = incomingNames.Where(n => !rolesByName.ContainsKey(n)).ToList();
                if (missingByName.Any())
                    throw new InvalidUserDataException($"بعض الأدوار غير موجودة: {string.Join(", ", missingByName)}");
            }

            var finalIncomingIds = new HashSet<string>(incomingIds, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rolesByName) finalIncomingIds.Add(kv.Value);

            var currentIds = new HashSet<string>(current.Select(c => c.RoleId), StringComparer.OrdinalIgnoreCase);

            var toAdd = finalIncomingIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).ToList();
            var toRemove = currentIds.Except(finalIncomingIds, StringComparer.OrdinalIgnoreCase).ToList();

            if (toRemove.Any())
            {
                var removeRows = current.Where(c => toRemove.Contains(c.RoleId, StringComparer.OrdinalIgnoreCase)).ToList();
                _userRepository.RemoveRoleAssociations(removeRows);
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(rid => new UserRoleAssociation
                {
                    UserId = user.Id,
                    RoleId = rid
                });
                _userRepository.AddRoleAssociations(addRows);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        var rolesLite = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty
            ))
            .ToListAsync();

        var roleIds = rolesLite.Select(r => r.RoleId).ToList();

        var rolePerms = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Where(rp => rp.Permission != null && rp.Permission.IsActive)
            .Select(rp => new { rp.PermissionId, rp.Permission!.Name })
            .ToList();

        var directPerms = await _userRepository.QueryUserPermissions()
            .Where(up => up.UserId == user.Id && up.Permission != null && up.Permission.IsActive)
            .Select(up => new { up.PermissionId, up.Permission!.Name })
            .ToListAsync();

        var permissionPairs2 = directPerms
            .Where(p => p.PermissionId.HasValue && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new { Id = p.PermissionId!.Value, Name = p.Name! })
            .Concat(
                rolePerms
                    .Where(p => p.PermissionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new { Id = p.PermissionId, Name = p.Name! })
            );

        var permissionMap2 = permissionPairs2
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<Guid>.Default);

        var permissionsLite2 = permissionMap2
            .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new PermissionLiteDto(kvp.Key.ToString(), kvp.Value))
            .ToList();

        return new UserListItemDto
        {
            userId = user.Id,
            fullName = user.FullName,
            username = user.UserName ?? string.Empty,
            email = user.Email ?? string.Empty,
            phone = user.PhoneNumber,
            isActive = user.IsActive,
            createdAt = user.CreationDate,
            roles = rolesLite.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList(),
            rolesCount = rolesLite.Count,
            permissionsCount = permissionsLite2.Count,
            Permissions = permissionsLite2
        };
    }

    #endregion

    #region تعديل حالة المستخدم فقط
    public async Task<UpdateUserStatusRequest> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException(id);

        user.IsActive = request.isActive;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", updRes.Errors.Select(e => e.Description)));

        return new UpdateUserStatusRequest
        {
            isActive = user.IsActive,
        };
    }
    #endregion

    #region تغيير كلمة المرور (المستخدم نفسه)
    public async Task ChangeOwnPasswordAsync(ChangePasswordRequest request)
    {
        var userId = _currentUserService.UserId ?? string.Empty;
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }
    #endregion

    #region إعادة تعيين كلمة المرور (الإداري)
    public async Task ResetUserPasswordAsync(AdminChangePasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId)
            ?? throw new UserNotFoundException(request.UserId);

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", resetResult.Errors.Select(e => e.Description)));
    }
    #endregion

    #region اسناد وحذف ادوار الى مستخدم 
    public async Task ToggleRolesForUserAsync(AssignRolesDto dto)
    {
        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بتعديل الأدوار إلا لمدير الفرع أو SuperAdmin.");

        var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (dto.RoleIds is { Count: > 0 })
            foreach (var rid in dto.RoleIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                incomingIds.Add(rid);

        if (dto.RoleNames is { Count: > 0 })
        {
            var names = dto.RoleNames!
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
            {
                var found = await _roleRepository.QueryRoles()
                    .Where(r => r.Name != null && names.Contains(r.Name))
                    .Select(r => new { r.Id, r.Name, r.IsActive })
                    .ToListAsync();

                var missing = names.Where(n => !found.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
                if (missing.Any())
                    throw new InvalidUserDataException($"أدوار غير موجودة: {string.Join(", ", missing)}");

                foreach (var f in found) incomingIds.Add(f.Id);
            }
        }

        var activeIncomingIds = await _roleRepository.GetActiveRoleIdsAsync(incomingIds);
        string? superAdminRoleId = await _roleRepository.GetSuperAdminRoleIdAsync();

        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
        {
            activeIncomingIds = activeIncomingIds.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var currentRoleIds = await _userRepository.GetActiveRoleIdsAsync(user.Id);

        var currentSet = new HashSet<string>(currentRoleIds, StringComparer.OrdinalIgnoreCase);
        var finalSet = new HashSet<string>(activeIncomingIds, StringComparer.OrdinalIgnoreCase);

        var toAdd = finalSet.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentSet.Except(finalSet, StringComparer.OrdinalIgnoreCase).ToList();

        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
            toRemove = toRemove.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return;

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (toRemove.Any())
            {
                var removeRows = await _userRepository.QueryRoleAssociations(asNoTracking: false)
                    .Where(ura => ura.UserId == user.Id && toRemove.Contains(ura.RoleId))
                    .ToListAsync();

                _userRepository.RemoveRoleAssociations(removeRows);
                await _unitOfWork.SaveChangesAsync();
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(rid => new UserRoleAssociation
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = rid
                });
                _userRepository.AddRoleAssociations(addRows);
                await _unitOfWork.SaveChangesAsync();
            }

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
        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بتعديل الصلاحيات المباشرة إلا لمدير الفرع أو SuperAdmin.");

        var incomingIds = new HashSet<Guid>();
        if (dto.PermissionId is { Count: > 0 })
            foreach (var pid in dto.PermissionId.Where(x => x != Guid.Empty))
                incomingIds.Add(pid);

        var activeIncomingIds = await _permissionRepository.GetActiveIdsAsync(incomingIds);
        var currentPermIds = await _userRepository.GetDirectPermissionIdsAsync(user.Id);

        var currentSet = new HashSet<Guid>(currentPermIds);
        var finalSet = new HashSet<Guid>(activeIncomingIds);

        var toAdd = finalSet.Except(currentSet).ToList();
        var toRemove = currentSet.Except(finalSet).ToList();

        if (!toAdd.Any() && !toRemove.Any())
            return;

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (toRemove.Any())
            {
                var removeRows = await _userRepository.QueryUserPermissions(asNoTracking: false)
                    .Where(up => up.UserId == user.Id && up.PermissionId.HasValue && toRemove.Contains(up.PermissionId.Value))
                    .ToListAsync();

                _userRepository.RemoveUserPermissions(removeRows);
                await _unitOfWork.SaveChangesAsync();
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(pid => new UserPermission
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    PermissionId = pid
                });
                _userRepository.AddUserPermissions(addRows);
                await _unitOfWork.SaveChangesAsync();
            }

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

        var user = await _userManager.FindByIdAsync(dto.UserId)
            ?? throw new UserNotFoundException(dto.UserId);

        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);

        if (!actorScope.IsSuperAdmin && !actorScope.IsBranchManager)
            throw new ForbiddenPermissionException("غير مسموح بإزالة الصلاحيات الحقلية إلا لمدير الفرع أو SuperAdmin.");

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
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        var patchReq = new UpdateUserPatchRequest
        {
            username = updatedUser.UserName,
            email = updatedUser.Email,
            roles = updatedUser.RoleIds?.Select(r => new RoleLiteDto(r, string.Empty)).ToList()
        };

        await UpdateUserAsync(userId, patchReq);
    }
    #endregion

    #region القائمة المنسدلة للمستخدمين
    public async Task<List<DropdownUserDto>> GetDropDownUsersAsync()
    {
        return await _userRepository.QueryUsers()
            .Where(u => u.IsActive)
            .OrderBy(u => u.UserName)
            .Select(u => new DropdownUserDto
            {
                Id = u.Id,
                Name = u.FullName ?? u.UserName ?? string.Empty
            })
            .ToListAsync();
    }
    #endregion

    private async Task<(User actor, bool isSuperAdmin, bool isBranchManager)> GetActorAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);

        if (actorScope.Actor == null)
            throw new UnauthenticatedUserException();

        return (actorScope.Actor, actorScope.IsSuperAdmin, actorScope.IsBranchManager);
    }
}
