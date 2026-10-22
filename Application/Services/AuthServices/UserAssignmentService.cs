using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices;

public class UserAssignmentService : IUserAssignmentService
{
    private readonly UserManager<User> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IMedicalCenterRepository _centers;
    private readonly IAccessContextRepository _access;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentCenterService _currentCenter;

    public UserAssignmentService(
        UserManager<User> userManager,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IMedicalCenterRepository centers,
        IAccessContextRepository access,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        ICurrentUserService currentUserService,
        ICurrentCenterService currentCenter)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _centers = centers;
        _access = access;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _currentUserService = currentUserService;
        _currentCenter = currentCenter;
    }

    public async Task<UserAssignmentsDto> GetAssignmentsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        var user = await RequireUserAsync(userId);
        await EnsureCanManageTargetAsync(user.Id, cancellationToken);

        var centers = await _userRepository.QueryUserCenters()
            .Where(uc => uc.UserId == user.Id)
            .Include(uc => uc.Center)
            .OrderByDescending(uc => uc.IsActive)
            .ThenBy(uc => uc.Center.NameAr)
            .Select(uc => new UserCenterAssignmentDto
            {
                CenterId = uc.CenterId,
                Code = uc.Center.Code,
                NameAr = uc.Center.NameAr,
                IsActive = uc.IsActive,
                CenterIsActive = uc.Center.IsActive,
                AssignedAt = uc.AssignedAt,
                RevokedAt = uc.RevokedAt,
                RevokeReason = uc.RevokeReason
            })
            .ToListAsync(cancellationToken);

        var roles = await _userRepository.QueryRoleAssociations()
            .Where(ura => ura.UserId == user.Id)
            .Include(ura => ura.Role)
            .Include(ura => ura.Center)
            .OrderByDescending(ura => ura.IsActive)
            .ThenBy(ura => ura.Role.Name)
            .ToListAsync(cancellationToken);

        return new UserAssignmentsDto
        {
            UserId = user.Id,
            Centers = centers,
            Roles = roles.Select(ToRoleDto).ToList()
        };
    }

    public async Task<UserCenterAssignmentDto> AssignCenterAsync(
        AssignUserCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserCenters.Manage);

        if (dto.CenterId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        var user = await RequireUserAsync(dto.UserId);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);

        await EnsureCanManageTargetAsync(user.Id, cancellationToken);
        await UserManagementScopeGuard.EnsureCanAssignCenterAsync(
            _access, actorId, dto.CenterId, cancellationToken);

        var center = await _centers.GetByIdAsync(dto.CenterId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await _userRepository.QueryUserCenters(asNoTracking: false)
                .FirstOrDefaultAsync(
                    uc => uc.UserId == user.Id && uc.CenterId == dto.CenterId,
                    cancellationToken);

            if (existing is { IsActive: true })
            {
                await tx.CommitAsync(cancellationToken);
                return ToCenterDto(existing, center!);
            }

            var assignedAt = DateTimeOffset.UtcNow;
            if (existing is not null)
            {
                IamRetention.Reactivate(existing, actorId, assignedAt);
            }
            else
            {
                _userRepository.AddUserCenters(new[]
                {
                    new UserCenter
                    {
                        UserId = user.Id,
                        CenterId = dto.CenterId,
                        IsActive = true,
                        AssignedByUserId = actorId,
                        AssignedAt = assignedAt
                    }
                });
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);
            await tx.CommitAsync(cancellationToken);

            var saved = await _userRepository.QueryUserCenters()
                .Include(uc => uc.Center)
                .FirstAsync(uc => uc.UserId == user.Id && uc.CenterId == dto.CenterId, cancellationToken);
            return ToCenterDto(saved, saved.Center);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RevokeCenterAsync(
        RevokeUserCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserCenters.Manage);

        if (dto.CenterId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        var reason = (dto.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidUserDataException("سبب الإلغاء مطلوب.");

        var user = await RequireUserAsync(dto.UserId);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);

        await EnsureCanManageTargetAsync(user.Id, cancellationToken);
        await UserManagementScopeGuard.EnsureCanAssignCenterAsync(
            _access, actorId, dto.CenterId, cancellationToken);

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await _userRepository.QueryUserCenters(asNoTracking: false)
                .FirstOrDefaultAsync(
                    uc => uc.UserId == user.Id && uc.CenterId == dto.CenterId && uc.IsActive,
                    cancellationToken)
                ?? throw new InvalidUserDataException("لا يوجد إسناد فعّال لهذا المركز.");

            var activeCount = await _userRepository.QueryUserCenters()
                .CountAsync(uc => uc.UserId == user.Id && uc.IsActive, cancellationToken);

            if (activeCount <= 1 && !dto.ConfirmLastCenter)
                throw new InvalidUserDataException(
                    "إلغاء آخر مركز فعّال يجعل الحساب غير قابل للعمل التشغيلي. أعد الطلب مع تأكيد إلغاء آخر مركز.");

            IamRetention.Revoke(row, actorId, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<UserRoleAssignmentDto> AssignRoleAsync(
        AssignUserRoleDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignRoles);

        if (string.IsNullOrWhiteSpace(dto.RoleId))
            throw new InvalidUserDataException("معرف الدور غير صالح.");

        var user = await RequireUserAsync(dto.UserId);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);

        await EnsureCanManageTargetAsync(user.Id, cancellationToken);

        var role = await _roleRepository.QueryRoles()
            .FirstOrDefaultAsync(r => r.Id == dto.RoleId, cancellationToken)
            ?? throw new RoleNotFoundException("الدور غير موجود.");

        var actorIsCentral = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(
            _access, actorId, cancellationToken);
        AssignableRolePolicy.EnsureAssignable(actorIsCentral, role.Name, role.IsActive, role.Scope);

        var scopeCenterId = AssignableRolePolicy.ResolveSyncScope(
            actorIsCentral,
            [role.Scope],
            dto.CenterId,
            _currentCenter.CenterId,
            _currentCenter.IsCentralContext);

        if (scopeCenterId is Guid centerId)
        {
            var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
            MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
        }

        await UserManagementScopeGuard.EnsureCanAssignRoleScopeAsync(
            _access, actorId, scopeCenterId, cancellationToken);

        if (scopeCenterId is Guid assignedCenterId &&
            !await _access.IsActivelyAssignedAsync(user.Id, assignedCenterId, cancellationToken))
        {
            throw new InvalidUserDataException(
                "لا يمكن إسناد دور مقيد بمركز دون وجود إسناد فعّال للمستخدم إلى ذلك المركز.");
        }

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingActive = await _userRepository.QueryRoleAssociations(asNoTracking: false)
                .FirstOrDefaultAsync(
                    ura => ura.UserId == user.Id
                        && ura.RoleId == role.Id
                        && ura.CenterId == scopeCenterId
                        && ura.IsActive,
                    cancellationToken);

            if (existingActive is not null)
            {
                await tx.CommitAsync(cancellationToken);
                var centerInfo = existingActive.CenterId is Guid cid
                    ? await _centers.GetByIdAsync(cid, asNoTracking: true, cancellationToken)
                    : null;
                return ToRoleDto(existingActive, role.Name, centerInfo?.Code, centerInfo?.NameAr);
            }

            var assignedAt = DateTimeOffset.UtcNow;
            _userRepository.AddRoleAssociations(new[]
            {
                new UserRoleAssociation
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = role.Id,
                    CenterId = scopeCenterId,
                    IsActive = true,
                    AssignedByUserId = actorId,
                    AssignedAt = assignedAt
                }
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // الصلاحيات الفعلية تُحسب من UserRoleAssociation + RolePermission عبر CenterPermissionEvaluator.
            // لا تُنسخ صلاحيات الدور المقيد بمركز إلى UserPermission المركزية (منع التسريب).

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, user.Id, cancellationToken);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, user.Id, afterPerms, cancellationToken);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync(cancellationToken);

            var saved = await _userRepository.QueryRoleAssociations()
                .Include(ura => ura.Role)
                .Include(ura => ura.Center)
                .FirstAsync(
                    ura => ura.UserId == user.Id
                        && ura.RoleId == role.Id
                        && ura.CenterId == scopeCenterId
                        && ura.IsActive,
                    cancellationToken);
            return ToRoleDto(saved);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            var raced = await _userRepository.QueryRoleAssociations()
                .AnyAsync(
                    ura => ura.UserId == user.Id
                        && ura.RoleId == role.Id
                        && ura.CenterId == scopeCenterId
                        && ura.IsActive,
                    cancellationToken);
            if (raced)
                throw new InvalidUserDataException("يوجد إسناد فعّال مكرر لهذا الدور والنطاق.");
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RevokeRoleAsync(
        RevokeUserRoleDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignRoles);

        var reason = (dto.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidUserDataException("سبب الإلغاء مطلوب.");

        var user = await RequireUserAsync(dto.UserId);
        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);

        await EnsureCanManageTargetAsync(user.Id, cancellationToken);

        var role = await _roleRepository.QueryRoles()
            .FirstOrDefaultAsync(r => r.Id == dto.RoleId, cancellationToken)
            ?? throw new RoleNotFoundException("الدور غير موجود.");

        var actorIsCentral = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(
            _access, actorId, cancellationToken);
        if (!actorIsCentral)
            AssignableRolePolicy.EnsureAssignable(actorIsCentral: false, role.Name, roleIsActive: true, role.Scope);

        var scopeCenterId = AssignableRolePolicy.ResolveRevokeScope(
            actorIsCentral,
            role.Scope,
            dto.CenterId,
            _currentCenter.CenterId,
            _currentCenter.IsCentralContext);

        if (!AssignableRolePolicy.MayRevoke(actorIsCentral, role.Name, scopeCenterId, role.Scope))
            throw new ForbiddenPermissionException("لا يمكنك إلغاء هذا الدور ضمن حدود تفويضك.");

        await UserManagementScopeGuard.EnsureCanAssignRoleScopeAsync(
            _access, actorId, scopeCenterId, cancellationToken);

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await _userRepository.QueryRoleAssociations(asNoTracking: false)
                .FirstOrDefaultAsync(
                    ura => ura.UserId == user.Id
                        && ura.RoleId == dto.RoleId
                        && ura.CenterId == scopeCenterId
                        && ura.IsActive,
                    cancellationToken)
                ?? throw new InvalidUserDataException("لا يوجد إسناد دور فعّال مطابق.");

            IamRetention.Revoke(row, actorId, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var afterPerms = await IamAdminGuard.GetEffectivePermissionNamesAsync(
                _userRepository, _roleRepository, user.Id, cancellationToken);
            await IamAdminGuard.EnsureNotLastIamAdminAsync(
                _userRepository, _roleRepository, user.Id, afterPerms, cancellationToken);

            await SecurityStampHelper.InvalidateUserTokensAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, user.Id);

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<List<DropdownRoleDto>> GetAssignableRolesAsync(CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.AssignRoles);

        var actorId = _currentUserService.UserId ?? string.Empty;
        var actorIsCentral = await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(
            _access, actorId, cancellationToken);

        var query = _roleRepository.QueryRoles().Where(r => r.IsActive);
        if (!actorIsCentral)
        {
            var allowed = AssignableRolePolicy.LocalOperationalRoleNames.ToArray();
            query = query.Where(r => r.Name != null && allowed.Contains(r.Name));
        }

        return await query
            .OrderBy(r => r.Name)
            .Select(r => new DropdownRoleDto
            {
                roleId = r.Id,
                name = r.Name ?? string.Empty
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<User> RequireUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        return await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);
    }

    private async Task EnsureCanManageTargetAsync(string targetUserId, CancellationToken cancellationToken)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanManageTargetAsync(
            _access, _userRepository, actorId, targetUserId, cancellationToken);
    }

    private static UserCenterAssignmentDto ToCenterDto(UserCenter row, Domain.Entities.Core.MedicalCenter center) => new()
    {
        CenterId = row.CenterId,
        Code = center.Code,
        NameAr = center.NameAr,
        IsActive = row.IsActive,
        CenterIsActive = center.IsActive,
        AssignedAt = row.AssignedAt,
        RevokedAt = row.RevokedAt,
        RevokeReason = row.RevokeReason
    };

    private static UserRoleAssignmentDto ToRoleDto(UserRoleAssociation row) =>
        ToRoleDto(row, row.Role?.Name, row.Center?.Code, row.Center?.NameAr);

    private static UserRoleAssignmentDto ToRoleDto(
        UserRoleAssociation row,
        string? roleName,
        string? centerCode,
        string? centerNameAr) => new()
    {
        AssignmentId = row.Id,
        RoleId = row.RoleId,
        RoleName = AuthDisplayNames.RoleAr(roleName),
        CenterId = row.CenterId,
        CenterCode = centerCode,
        CenterNameAr = centerNameAr,
        IsCentralScope = row.CenterId is null,
        IsActive = row.IsActive,
        AssignedAt = row.AssignedAt,
        RevokedAt = row.RevokedAt,
        RevokeReason = row.RevokeReason
    };
}
