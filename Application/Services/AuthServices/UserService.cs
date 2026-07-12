using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Common.Localization;
using Application.Common.Results;
using Application.Common.Permissions;
using Application.Dtos.AuthDtos;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        ITextInputSanitizer textSanitizer)
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
    }


    #region الانشاء 
    public async Task<Result<CreateUserDto>> Create(CreateUserDto createdUser)
    {
        var errors = new List<string>();

        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        // تحقّقات أساسية
        if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
        if (errors.Count > 0) return Result<CreateUserDto>.Failure(errors, null);

        // منع التكرار
        if (await _userManager.FindByNameAsync(createdUser.username) is not null)
            return Result<CreateUserDto>.Failure(new[] { "اسم المستخدم موجود مسبقاً." }, null);
        if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
            return Result<CreateUserDto>.Failure(new[] { "البريد الإلكتروني موجود مسبقاً." }, null);

        // جلب الأدوار الفعالة (كما عندك)
        var rawRoles = createdUser.roles.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rolesFromDb = await _roleRepository.ResolveActiveRolesAsync(rawRoles);

        var foundUpper = rolesFromDb
            .SelectMany(r => new[] { (r.Id ?? "").ToUpperInvariant(), (r.Name ?? "").ToUpperInvariant() })
            .ToHashSet();
        var missing = rawRoles.Where(x => !foundUpper.Contains(x.ToUpperInvariant())).ToList();
        if (missing.Any())
            return Result<CreateUserDto>.Failure(new[] { $"أدوار غير موجودة أو غير نشطة: {string.Join(", ", missing)}" }, null);

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            return Result<CreateUserDto>.Failure(new[] { "غير مسموح بإنشاء المستخدمين." }, null);

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var user = new User
            {
                UserName = _textSanitizer.SanitizePlainText(createdUser.username) ?? createdUser.username,
                Email = _textSanitizer.SanitizePlainText(createdUser.email) ?? createdUser.email,
                PhoneNumber = string.IsNullOrEmpty(createdUser.phone) ? null : _textSanitizer.SanitizePlainText(createdUser.phone),
                IsActive = createdUser.isActive,
                CreationDate = DateTime.Now,
                FullName = _textSanitizer.SanitizePlainText(createdUser.fullName) ?? string.Empty
            };

            var createRes = await _userManager.CreateAsync(user, createdUser.password);
            if (!createRes.Succeeded)
                return Result<CreateUserDto>.Failure(createRes.Errors.Select(e => e.Description), null);

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

            return Result<CreateUserDto>.Success(createdUser);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region التعديل الكامل    
    public async Task<Result<string>> Update(string id, UpdateUserDto updatedUser)
    {
        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            return Result<string>.Failure(new[] { "غير مسموح بتعديل المستخدمين." }, null);

        var user = await _userRepository.GetByIdAsync(id);
        if (user is null) return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        if (!string.IsNullOrWhiteSpace(updatedUser.Email) &&
            !string.Equals(updatedUser.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailDup = await _userManager.FindByEmailAsync(updatedUser.Email);
            if (emailDup != null && emailDup.Id != user.Id)
                return Result<string>.Failure(new[] { "البريد الإلكتروني مستخدم من قبل حساب آخر." }, null);

            var setEmail = await _userManager.SetEmailAsync(user, updatedUser.Email);
            if (!setEmail.Succeeded) return Result<string>.Failure(setEmail.Errors.Select(e => e.Description), null);
        }

        if (!string.IsNullOrWhiteSpace(updatedUser.UserName) &&
            !string.Equals(updatedUser.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var userNameDup = await _userManager.FindByNameAsync(updatedUser.UserName);
            if (userNameDup != null && userNameDup.Id != user.Id)
                return Result<string>.Failure(new[] { "اسم المستخدم مستخدم من قبل حساب آخر." }, null);

            var setUserName = await _userManager.SetUserNameAsync(user, updatedUser.UserName);
            if (!setUserName.Succeeded) return Result<string>.Failure(setUserName.Errors.Select(e => e.Description), null);
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
        if (!updateRes.Succeeded) return Result<string>.Failure(updateRes.Errors.Select(e => e.Description), null);

        if (invalidateTokens)
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

        return Result<string>.Success("تم التحديث بنجاح");
    }

    #endregion

    #region الحذف

    public async Task<Result<string>> Delete(string userId)
    {
        var (_, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        if (!actorIsSuperAdmin && !actorIsBranchManager)
            return Result<string>.Failure(new[] { "غير مسموح بحذف المستخدمين." }, null);

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return Result<string>.Failure(result.Errors.Select(e => e.Description), null);

        return Result<string>.Success("تم الحذف بنجاح");
    }

    #endregion

    #region  جلب الكل 
    public async Task<Result<PaginatedList<UserListItemDto>>> GetUsersListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        // === تحديد النطاق بحسب المستخدم الحالي ===
        var currentUserId = _currentUserService.UserId ?? string.Empty;

        var actorScope = await _actorScopeProvider.GetScopeAsync(currentUserId);
        bool isSuperAdmin = actorScope.IsSuperAdmin;
        bool isBranchManger = actorScope.IsBranchManager;

        IQueryable<User> baseQuery = _userRepository.QueryUsers();

        // 🧭 تطبيق النطاق
        if (!isSuperAdmin && !isBranchManger)
            baseQuery = baseQuery.Where(u => false);

        baseQuery = baseQuery.Where(u => u.Id != currentUserId);

        // 🔎 بحث موحّد: الاسم (FullName إن وجد، وإلا UserName) أو الإيميل
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = SqlLikePatternEscape.EscapeForSqlServerLike(q.Trim());
            baseQuery = baseQuery.Where(u =>
                EF.Functions.Like((u.FullName ?? u.UserName)!, $"%{s}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{s}%"))
            );
        }

        // 🟢 فلترة بالحالة
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

        var emptyPage = new PaginatedList<UserListItemDto>(new List<UserListItemDto>(), totalCount, pageNumber, pageSize);
        if (usersPage.Count == 0)
            return Result<PaginatedList<UserListItemDto>>.Success(emptyPage);

        var userIds = usersPage.Select(u => u.Id).ToList();

        // الأدوار لكل مستخدم
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

        // صلاحيات الأدوار
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

        // تركيب الـ DTO
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

        var page = new PaginatedList<UserListItemDto>(items, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<UserListItemDto>>.Success(page);
    }
    #endregion

    #region     جلب حسب المعرف 
    public async Task<Result<UserListItemDto>> GetUserAsync(string id)
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
            .FirstOrDefaultAsync();

        if (user == null)
            return Result<UserListItemDto>.Failure(new[] { "المستخدم غير موجود" }, null);

        if (!actorIsSuperAdmin && !actorIsBranchManager && actor.Id != user.Id)
            return Result<UserListItemDto>.Failure(new[] { "غير مسموح بالوصول." }, null);

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

        var dto = new UserListItemDto
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

        return Result<UserListItemDto>.Success(dto);
    }

    #endregion

    #region التعديل الجزئي 
    public async Task<Result<UserListItemDto>> UpdateUserAsync(string id, UpdateUserPatchRequest dto)
    {
        // 0) الهدف
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null)
            return Result<UserListItemDto>.Failure(new[] { "المستخدم غير موجود" }, null);

        var actorId = _currentUserService.UserId ?? string.Empty;

        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            return Result<UserListItemDto>.Failure(new[] { "غير مسموح بالتعديل إلا لمدير الفرع أو SuperAdmin." }, null);

        // 2) تحقّقات uniqueness عند تعديل username/email
        if (!string.IsNullOrWhiteSpace(dto.username) &&
            !string.Equals(dto.username, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var existsUserName = await _userManager.FindByNameAsync(dto.username);
            if (existsUserName is not null && existsUserName.Id != user.Id)
                return Result<UserListItemDto>.Failure(new[] { "اسم المستخدم موجود مسبقا." }, null);
        }

        if (!string.IsNullOrWhiteSpace(dto.email) &&
            !string.Equals(dto.email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existsEmail = await _userManager.FindByEmailAsync(dto.email);
            if (existsEmail is not null && existsEmail.Id != user.Id)
                return Result<UserListItemDto>.Failure(new[] { "الايميل موجود مسبقا." }, null);
        }

        // 3) تعديل الحقول (PATCH)
        if (dto.fullName is not null) user.FullName = _textSanitizer.SanitizePlainText(dto.fullName) ?? dto.fullName;
        if (dto.username is not null) user.UserName = _textSanitizer.SanitizePlainText(dto.username) ?? dto.username;
        if (dto.email is not null) user.Email = _textSanitizer.SanitizePlainText(dto.email) ?? dto.email;
        if (dto.phone is not null) user.PhoneNumber = _textSanitizer.SanitizePlainText(dto.phone) ?? dto.phone;
        if (dto.isActive.HasValue) user.IsActive = dto.isActive.Value;

        // 4) تحديث المستخدم عبر Identity
        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            return Result<UserListItemDto>.Failure(updRes.Errors.Select(e => e.Description), null);

        // 5) الأدوار (null => لا تعديل، empty => إزالة كل الأدوار، otherwise => استبدال كامل)
        if (dto.roles != null)
        {
            // 🚫 منع مدير الفرع من إضافة/سحب SuperAdmin
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
                    return Result<UserListItemDto>.Failure(new[] { "غير مسموح لمدير الفرع بمنح/سحب دور SuperAdmin." }, null);
            }

            // الأدوار الحالية
            var current = await _userRepository.GetRoleAssociationsAsync(user.Id);

            // تحويل المدخلات إلى مجموعة RoleIds نهائية
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
                    return Result<UserListItemDto>.Failure(new[] { $"بعض الأدوار غير موجودة: {string.Join(", ", missingByName)}" }, null);
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

        // 6) إعداد الخرج: roles + permissions (كما هو لديك)
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

        var dtoOut = new UserListItemDto
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

        return Result<UserListItemDto>.Success(dtoOut);
    }

    #endregion

    #region          تعديل حالة المستخدم فقط
    public async Task<Result<UpdateUserStatusRequest>> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null)
            return Result<UpdateUserStatusRequest>.Failure(new[] { "المتسخدم غير موجود." }, null);

        user.IsActive = request.isActive;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            return Result<UpdateUserStatusRequest>.Failure(updRes.Errors.Select(e => e.Description), null);

        var dtoOut = new UpdateUserStatusRequest
        {
            isActive = user.IsActive,
        };
        return Result<UpdateUserStatusRequest>.Success(dtoOut);
    }
    #endregion

    #region         تغيير كلمة المرور (المستخدم نفسه)
    public async Task<Result<string>> ChangeOwnPasswordAsync(ChangePasswordRequest request)
    {
        var errorList = new List<string>();
        var userId = _currentUserService.UserId;

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            errorList.Add("المستخدم غير موجود");
            return Result<string>.Failure(errorList, null);
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            errorList.AddRange(result.Errors.Select(e => e.Description));
            return Result<string>.Failure(errorList, null);
        }

        return Result<string>.Success("تم تحديث كلمة المرور بنجاح.");

    }
    #endregion

    #region         إعادة تعيين كلمة المرور (الإداري)
    public async Task<Result<string>> ResetUserPasswordAsync(AdminChangePasswordRequest request)
    {
        var errorList = new List<string>();

        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            errorList.Add("المستخدم غير موجود");
            return Result<string>.Failure(errorList, null);
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
        {
            errorList.AddRange(resetResult.Errors.Select(e => e.Description));
            return Result<string>.Failure(errorList, null);
        }

        return Result<string>.Success("تم إعادة تعيين كلمة المرور بنجاح.");
    }
    #endregion

    #region         اسناد وحذف ادوار الى مستخدم 
    public async Task<Result<string>> ToggleRolesForUserAsync(AssignRolesDto dto)
    {
        // 0) الهدف
        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        // 1) المنفّذ وصلاحيته
        var actorId = _currentUserService.UserId ?? string.Empty;

        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            return Result<string>.Failure(new[] { "غير مسموح بتعديل الأدوار إلا لمدير الفرع أو SuperAdmin." }, null);

        // 2) حلّ القائمة المرسلة إلى RoleIds نهائية
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
                    return Result<string>.Failure(new[] { $"أدوار غير موجودة: {string.Join(", ", missing)}" }, null);

                foreach (var f in found) incomingIds.Add(f.Id);
            }
        }

        // إذا ما انبعت أي دور، نعتبرها قائمة نهائية فاضية (يعني إزالة كل الأدوار المسموح إزالتها)
        // بس رح نتقيّد بقيود SuperAdmin أدناه.

        // 3) نشِّط فقط الأدوار الفعّالة
        var activeIncomingIds = await _roleRepository.GetActiveRoleIdsAsync(incomingIds);

        string? superAdminRoleId = await _roleRepository.GetSuperAdminRoleIdAsync();

        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
        {
            // امنع تضمين SuperAdmin ضمن الحالة النهائية لمدير فرع
            activeIncomingIds = activeIncomingIds.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 5) الأدوار الحالية
        var currentRoleIds = await _userRepository.GetActiveRoleIdsAsync(user.Id);

        var currentSet = new HashSet<string>(currentRoleIds, StringComparer.OrdinalIgnoreCase);
        var finalSet = new HashSet<string>(activeIncomingIds, StringComparer.OrdinalIgnoreCase); // الحالة النهائية القادمة من الواجهة


        // 6) احسب الفرق: ما يجب إضافته/إزالته
        var toAdd = finalSet.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentSet.Except(finalSet, StringComparer.OrdinalIgnoreCase).ToList();
        //var toRemove = finalSet.Intersect(currentSet, StringComparer.OrdinalIgnoreCase).ToList(); // موجودة => احذف
        //var toAdd = finalSet.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();    // غير موجودة => أضف


        // حماية إضافية: لو المستخدم عنده SuperAdmin حاليًا، لا تسمح لغير السوبر بحذفه
        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
            toRemove = toRemove.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return Result<string>.Success("لا توجد تغييرات.");

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

            var finalRoleIds = await _userRepository.GetActiveRoleIdsAsync(user.Id);

            var finalRolePermissionIds = await _roleRepository.GetActivePermissionIdsForRolesAsync(finalRoleIds);

            var existingUserPermissionIds = await _userRepository.GetDirectPermissionIdsAsync(user.Id);

            var toAddPerms = finalRolePermissionIds
                .Except(existingUserPermissionIds)
                .Select(pid => new UserPermission { UserId = user.Id, PermissionId = pid })
                .ToList();
            if (toAddPerms.Any())
                _userRepository.AddUserPermissions(toAddPerms);

            var stillNeededPerms = new HashSet<Guid>(finalRolePermissionIds);
            var userPermsToRemove = await _userRepository.GetUserPermissionsAsync(user.Id);
            userPermsToRemove = userPermsToRemove
                .Where(up => up.PermissionId != null && !stillNeededPerms.Contains(up.PermissionId!.Value))
                .ToList();
            if (userPermsToRemove.Any())
                _userRepository.RemoveUserPermissions(userPermsToRemove);

            await _unitOfWork.SaveChangesAsync();

            var finalFieldPermIds = await _roleRepository.GetFieldPermissionIdsForRolesAsync(finalRoleIds);

            var existingUserFieldPermIds = await _userRepository.GetDirectFieldPermissionIdsAsync(user.Id);

            var toAddFieldPerms = finalFieldPermIds
                .Except(existingUserFieldPermIds)
                .Select(fid => new UserFieldPermission { UserId = user.Id, FieldPermissionId = fid })
                .ToList();
            if (toAddFieldPerms.Any())
                _userRepository.AddUserFieldPermissions(toAddFieldPerms);

            var stillNeededFieldPerms = new HashSet<Guid>(finalFieldPermIds);
            var userFieldPermsToRemove = await _userRepository.GetUserFieldPermissionsAsync(user.Id);
            userFieldPermsToRemove = userFieldPermsToRemove
                .Where(ufp => !stillNeededFieldPerms.Contains(ufp.FieldPermissionId))
                .ToList();
            if (userFieldPermsToRemove.Any())
                _userRepository.RemoveUserFieldPermissions(userFieldPermsToRemove);

            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync();

            return Result<string>.Success($"تم تعديل الأدوار بنجاح. (إضافة: {toAdd.Count} | إزالة: {toRemove.Count})");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region  اسناد وحذف صلاحيات الى مستخدم
    public async Task<Result<string>> TogglePermissionsForUserAsync(AssignPermissionToUserDto dto)
    {
        // 0) تحقق إدخال
        if (string.IsNullOrWhiteSpace(dto.UserId))
            return Result<string>.Failure(new[] { "UserId مطلوب." }, null);

        if (dto.PermissionId == null || dto.PermissionId.Count == 0)
            return Result<string>.Failure(new[] { "لم يتم تحديد أي صلاحيات." }, null);

        // 1) المستخدم الهدف
        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود." }, null);

        // 2) المنفّذ وصلاحيته/نطاقه
        var actorId = _currentUserService.UserId ?? string.Empty;

        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var isSuperAdmin = actorScope.IsSuperAdmin;
        var isBranchManager = actorScope.IsBranchManager;

        if (!isSuperAdmin && !isBranchManager)
            return Result<string>.Failure(new[] { "غير مسموح بتعديل الصلاحيات إلا لمدير الفرع أو SuperAdmin." }, null);

        var incomingIds = dto.PermissionId
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (incomingIds.Count == 0)
            return Result<string>.Failure(new[] { "لم يتم تمرير أي صلاحية صالحة." }, null);

        var activeIncomingIds = await _permissionRepository.GetActiveIdsAsync(incomingIds);

        var missingOrInactive = incomingIds.Except(activeIncomingIds).ToList();
        if (missingOrInactive.Any())
            return Result<string>.Failure(new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

        var currentDirectIds = await _userRepository.GetDirectPermissionIdsAsync(user.Id);

        var currentSet = new HashSet<Guid>(currentDirectIds);
        var finalSet = new HashSet<Guid>(activeIncomingIds); // نفس أسلوب ToggleRoles (Set للمدخلات)

        // 5) Toggle على "المحدد فقط" (بدون المساس بالباقي)
        // الموجود ضمن direct && ضمن المحدد => احذفه
        var toRemoveIds = finalSet.Where(pid => currentSet.Contains(pid)).ToList();
        // غير الموجود ضمن direct && ضمن المحدد => أضفه
        var toAddIds = finalSet.Where(pid => !currentSet.Contains(pid)).ToList();

        if (toAddIds.Count == 0 && toRemoveIds.Count == 0)
            return Result<string>.Success("لا توجد تغييرات.");

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (toRemoveIds.Any())
            {
                var removeRows = await _userRepository.GetUserPermissionsAsync(user.Id);
                removeRows = removeRows
                    .Where(up => up.PermissionId != null && toRemoveIds.Contains(up.PermissionId!.Value))
                    .ToList();

                if (removeRows.Any())
                    _userRepository.RemoveUserPermissions(removeRows);

                await _unitOfWork.SaveChangesAsync();
            }

            if (toAddIds.Any())
            {
                var addRows = toAddIds.Select(pid => new UserPermission
                {
                    UserId = user.Id,
                    PermissionId = pid
                });

                _userRepository.AddUserPermissions(addRows);
                await _unitOfWork.SaveChangesAsync();
            }

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync();

            return Result<string>.Success(
                $"تم تعديل الصلاحيات بنجاح. (إضافة: {toAddIds.Count} | إزالة: {toRemoveIds.Count})");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region         إزالة صلاحيات الحقول من مستخدم
    public async Task<Result<string>> RemoveFieldPermissionsFromUserAsync(RemoveFieldPermissionsDto dto)
    {
        var errors = new List<string>();

        if (dto.FieldPermissionIds == null || dto.FieldPermissionIds.Count == 0)
        {
            errors.Add("لم يتم تحديد أي صلاحيات حقول.");
            return Result<string>.Failure(errors, null);
        }

        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
        {
            errors.Add("المستخدم غير موجود.");
            return Result<string>.Failure(errors, null);
        }

        var userFieldPermissions = (await _userRepository.GetUserFieldPermissionsAsync(dto.UserId))
            .Where(ufp => dto.FieldPermissionIds.Contains(ufp.FieldPermissionId))
            .ToList();

        if (!userFieldPermissions.Any())
            return Result<string>.Success("لا توجد صلاحيات حقول مطابقة لإزالتها.");

        _userRepository.RemoveUserFieldPermissions(userFieldPermissions);
        await _unitOfWork.SaveChangesAsync();

        await SecurityStampHelper.InvalidateUserTokensAsync(
            _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, dto.UserId);

        return Result<string>.Success("تم إزالة صلاحيات الحقول المحددة من المستخدم.");
    }
    #endregion

    #region تعديل مستخدم وأدواره
    public async Task<Result<string>> UpdateUserWithRolesAsync(string userId, UpdateUserWithRolesDto dto)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // تحقق من صحة الـ UserId
        if (string.IsNullOrWhiteSpace(userId))
        {
            errors.Add("معرف المستخدم غير صالح.");
            return Result<string>.Failure(errors, null);
        }

        // جلب المستخدم
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            errors.Add("المستخدم غير موجود.");
            return Result<string>.Failure(errors, null);
        }

        // ====================== بدء الترانزاكشن ======================
        using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            var safeUserName = _textSanitizer.SanitizePlainText(dto.UserName) ?? dto.UserName;
            var safeEmail = _textSanitizer.SanitizePlainText(dto.Email) ?? dto.Email;
            user.UserName = safeUserName;
            user.NormalizedUserName = safeUserName.ToUpperInvariant();
            user.Email = safeEmail;
            user.NormalizedEmail = safeEmail.ToUpperInvariant();

            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
            {
                errors.AddRange(updateRes.Errors.Select(e => e.Description));
                return Result<string>.Failure(errors, null);
            }

            if (dto.RoleIds?.Any() == true)
            {
                var foundRoleIds = await _roleRepository.GetActiveRoleIdsAsync(dto.RoleIds);
                var notFound = dto.RoleIds.Except(foundRoleIds).ToList();

                if (notFound.Any())
                {
                    errors.Add($"الأدوار التالية غير موجودة: {string.Join(", ", notFound)}");
                    return Result<string>.Failure(errors, null);
                }

                var currentRoleIds = await _userRepository.GetActiveRoleIdsAsync(userId);

                var rolesToAdd = foundRoleIds.Except(currentRoleIds).ToList();
                var rolesToRemove = currentRoleIds.Except(foundRoleIds).ToList();

                if (rolesToAdd.Any())
                {
                    var newAssociations = rolesToAdd.Select(rid => new UserRoleAssociation
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        RoleId = rid
                    }).ToList();

                    _userRepository.AddRoleAssociations(newAssociations);
                }

                if (rolesToRemove.Any())
                {
                    var toRemove = await _userRepository.QueryRoleAssociations(asNoTracking: false)
                        .Where(ura => ura.UserId == userId && rolesToRemove.Contains(ura.RoleId))
                        .ToListAsync();

                    _userRepository.RemoveRoleAssociations(toRemove);
                }

                if (!rolesToAdd.Any() && !rolesToRemove.Any())
                {
                    warnings.Add("لم يتم إجراء أي تعديل على الأدوار. جميع الأدوار المدخلة مطابقة للبيانات الحالية.");
                }
            }

            var affected = await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            var message = $"تم تحديث المستخدم بنجاح. عدد العمليات: {affected}.";
            if (warnings.Any())
                message += $" تحذيرات: {string.Join(" | ", warnings)}";

            return Result<string>.Success(message);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            errors.Add($"حدث خطأ أثناء التحديث: {ex.Message}");
            return Result<string>.Failure(errors, null);
        }
    }
    #endregion

    #region جلب المستخدمين للقائمة المنسدلة
    public async Task<Result<List<DropdownUserDto>>> GetDropDownUsersAsync()
    {
        var users = await _userRepository.QueryUsers()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName ?? u.UserName ?? string.Empty)
            .Select(u => new DropdownUserDto
            {
                Id = u.Id,
                Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.UserName ?? string.Empty)
            })
            .ToListAsync();

        return Result<List<DropdownUserDto>>.Success(users);
    }
    #endregion

    #region  دوال مساعدة خاصة بالمستخدم الحالي
    // ✅ فحص السوبر أدمن (قابل للترجمة SQL)
      private Task<bool> IsSuperAdminAsync(string userId) =>
        _actorScopeProvider.IsSuperAdminAsync(userId);

    private async Task<(User actor, bool isSuperAdmin, bool isBranchManager)> GetActorAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);
        var actor = actorScope.Actor ?? throw new InvalidOperationException("المستخدم الحالي غير موجود.");
        return (actor, actorScope.IsSuperAdmin, actorScope.IsBranchManager);
    }
    #endregion
    private async Task<UserListItemDto> BuildUserListItemDtoAsync(string userId, string? fullNameFromRequest)
    {
        var u = await _userRepository.QueryUsers()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id,
                x.UserName,
                x.Email,
                x.PhoneNumber,
                x.IsActive,
                x.CreationDate
            })
            .FirstAsync();

        var roles = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == userId)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty
            ))
            .ToListAsync();

        var roleIds = roles.Select(r => r.RoleId).Distinct().ToList();

        var rolePerms = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Select(rp => rp.Permission?.Name)
            .ToList();

        var directPerms = await _userRepository.QueryUserPermissions()
            .Where(up => up.UserId == userId)
            .Select(up => up.Permission.Name)
            .ToListAsync();

        var allPermNames = directPerms
            .Concat(rolePerms)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissionsLite = allPermNames
            .Select(p => new PermissionLiteDto(p, p))
            .ToList();

        // FullName: نحاول استخدام claim إن وجد، وإلا نستخدم UserName
        string? fullName = fullNameFromRequest;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            // لو أردت القراءة من Claims store بوقت لاحق يمكنك إضافتها هنا
            fullName = u.UserName;
        }

        return new UserListItemDto
        {
            userId = u.Id,
            fullName = fullName,
            username = u.UserName ?? string.Empty,
            email = u.Email ?? string.Empty,
            phone = u.PhoneNumber,
            isActive = u.IsActive,
            createdAt = u.CreationDate,
            roles = roles.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList(),
            permissionsCount = permissionsLite.Count,
            Permissions = permissionsLite
        };
    }
}

