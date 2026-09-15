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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActorScopeProvider _actorScopeProvider;
    private readonly JwtSettings _jwtSettings;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITextInputSanitizer _textSanitizer;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;

    private static readonly HashSet<string> AllowedProfilePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

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
        IMapper mapper,
        IFileStorageService fileStorage)
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
        _fileStorage = fileStorage;
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
            .Where(up => up.PermissionId.HasValue && up.Permission?.Name != null)
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
        var (actor, actorIsSuperAdmin, actorIsBranchManager) = await GetActorAsync();

        var user = await _userRepository.QueryUsers()
            .FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new UserNotFoundException(id);

        if (!actorIsSuperAdmin && !actorIsBranchManager && actor.Id != user.Id)
            throw new ForbiddenPermissionException("غير مسموح بالوصول.");

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
            .Where(up => up.PermissionId.HasValue && up.Permission != null && !string.IsNullOrWhiteSpace(up.Permission.Name))
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

        if (dto.fullName is not null) dto.fullName = _textSanitizer.SanitizePlainText(dto.fullName) ?? dto.fullName;
        if (dto.username is not null) dto.username = _textSanitizer.SanitizePlainText(dto.username) ?? dto.username;
        if (dto.email is not null) dto.email = _textSanitizer.SanitizePlainText(dto.email) ?? dto.email;
        if (dto.phone is not null) dto.phone = _textSanitizer.SanitizePlainText(dto.phone) ?? dto.phone;

        var previousIsActive = user.IsActive;
        _mapper.Map(dto, user);
        if (!dto.isActive.HasValue)
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

        var roleAssociationEntities = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id)
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
        return updated;
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

    public async Task<string> UploadMyProfilePhotoAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new InvalidUserDataException("الملف فارغ أو غير محدد.");

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedProfilePhotoExtensions.Contains(extension))
            throw new InvalidUserDataException("يُسمح فقط بصور JPG أو PNG أو WEBP.");

        if (!_fileStorage.ValidateFile(file, out var validationError))
            throw new InvalidUserDataException(validationError);

        var user = await GetCurrentUserOrThrowAsync();

        if (!string.IsNullOrWhiteSpace(user.ProfilePhotoPath))
            await _fileStorage.DeleteFileAsync(user.ProfilePhotoPath);

        var (path, _) = await _fileStorage.SaveFileAsync(file, "UserProfile", 0);
        user.ProfilePhotoPath = path;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidUserDataException(string.Join(", ", result.Errors.Select(e => e.Description)));

        return BuildProfilePhotoUrl(user.ProfilePhotoPath);
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> GetMyProfilePhotoAsync()
    {
        var user = await GetCurrentUserOrThrowAsync();
        if (string.IsNullOrWhiteSpace(user.ProfilePhotoPath))
            throw new InvalidUserDataException("لا توجد صورة شخصية.");

        var fullPath = _fileStorage.GetFullPath(user.ProfilePhotoPath);
        if (!File.Exists(fullPath))
            throw new InvalidUserDataException("ملف الصورة غير موجود.");

        var bytes = await File.ReadAllBytesAsync(fullPath);
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return (bytes, contentType, fileName);
    }

    public async Task RemoveMyProfilePhotoAsync()
    {
        var user = await GetCurrentUserOrThrowAsync();
        if (string.IsNullOrWhiteSpace(user.ProfilePhotoPath))
            return;

        await _fileStorage.DeleteFileAsync(user.ProfilePhotoPath);
        user.ProfilePhotoPath = null;

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

    private static string BuildProfilePhotoUrl(string? profilePhotoPath)
    {
        if (string.IsNullOrWhiteSpace(profilePhotoPath))
            return string.Empty;

        return $"/api/User/MyProfilePhoto?v={Path.GetFileNameWithoutExtension(profilePhotoPath)}";
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

            await SyncRoleDerivedPermissionsAsync(user.Id);

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
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

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

            if (updatedUser.RoleIds?.Any() == true)
            {
                var foundRoleIds = await _roleRepository.GetActiveRoleIdsAsync(updatedUser.RoleIds);
                var notFound = updatedUser.RoleIds.Except(foundRoleIds).ToList();

                if (notFound.Any())
                    throw new InvalidUserDataException($"الأدوار التالية غير موجودة: {string.Join(", ", notFound)}");

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
            }

            await _unitOfWork.SaveChangesAsync();
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
        var users = await _userRepository.QueryUsers()
            .Where(u => u.IsActive)
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return _mapper.Map<List<DropdownUserDto>>(users);
    }
    #endregion

    private async Task SyncRoleDerivedPermissionsAsync(string userId)
    {
        var finalRoleIds = await _userRepository.GetActiveRoleIdsAsync(userId);

        var finalRolePermissionIds = await _roleRepository.GetActivePermissionIdsForRolesAsync(finalRoleIds);

        var existingUserPermissionIds = await _userRepository.GetDirectPermissionIdsAsync(userId);

        var toAddPerms = finalRolePermissionIds
            .Except(existingUserPermissionIds)
            .Select(pid => new UserPermission { UserId = userId, PermissionId = pid })
            .ToList();
        if (toAddPerms.Any())
            _userRepository.AddUserPermissions(toAddPerms);

        var stillNeededPerms = new HashSet<Guid>(finalRolePermissionIds);
        var userPermsToRemove = await _userRepository.GetUserPermissionsAsync(userId);
        userPermsToRemove = userPermsToRemove
            .Where(up => up.PermissionId != null && !stillNeededPerms.Contains(up.PermissionId!.Value))
            .ToList();
        if (userPermsToRemove.Any())
            _userRepository.RemoveUserPermissions(userPermsToRemove);

        await _unitOfWork.SaveChangesAsync();

        var finalFieldPermIds = await _roleRepository.GetFieldPermissionIdsForRolesAsync(finalRoleIds);

        var existingUserFieldPermIds = await _userRepository.GetDirectFieldPermissionIdsAsync(userId);

        var toAddFieldPerms = finalFieldPermIds
            .Except(existingUserFieldPermIds)
            .Select(fid => new UserFieldPermission { UserId = userId, FieldPermissionId = fid })
            .ToList();
        if (toAddFieldPerms.Any())
            _userRepository.AddUserFieldPermissions(toAddFieldPerms);

        var stillNeededFieldPerms = new HashSet<Guid>(finalFieldPermIds);
        var userFieldPermsToRemove = await _userRepository.GetUserFieldPermissionsAsync(userId);
        userFieldPermsToRemove = userFieldPermsToRemove
            .Where(ufp => !stillNeededFieldPerms.Contains(ufp.FieldPermissionId))
            .ToList();
        if (userFieldPermsToRemove.Any())
            _userRepository.RemoveUserFieldPermissions(userFieldPermsToRemove);

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<(User actor, bool isSuperAdmin, bool isBranchManager)> GetActorAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorScope = await _actorScopeProvider.GetScopeAsync(actorId);

        if (actorScope.Actor == null)
            throw new UnauthenticatedUserException();

        return (actorScope.Actor, actorScope.IsSuperAdmin, actorScope.IsBranchManager);
    }
}
