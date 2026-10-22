using Application.Common.Permissions;
using Application.Common.Results;
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

public class UserClinicAssignmentService : IUserClinicAssignmentService
{
    public const string ClinicDoctorRoleName = "ClinicDoctor";

    private readonly UserManager<User> _userManager;
    private readonly IUserRepository _userRepository;
    private readonly IMedicalCenterRepository _centers;
    private readonly ICenterClinicRepository _centerClinics;
    private readonly IAccessContextRepository _access;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUserService;

    public UserClinicAssignmentService(
        UserManager<User> userManager,
        IUserRepository userRepository,
        IMedicalCenterRepository centers,
        ICenterClinicRepository centerClinics,
        IAccessContextRepository access,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _centers = centers;
        _centerClinics = centerClinics;
        _access = access;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedList<CenterClinicForAssignmentDto>> GetCenterClinicsAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);
        await EnsureActorCanManageCenterAsync(centerId, cancellationToken);

        if (await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken) is null)
            throw new MedicalCenterNotFoundException();

        NormalizePaging(ref pageNumber, ref pageSize);

        var query = _centerClinics.Query().Where(x => x.CenterId == centerId);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        query = query.OrderBy(x => x.Clinic.SortOrder).ThenBy(x => x.Clinic.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var clinicIds = page.Select(x => x.ClinicId).ToList();
        var doctorCounts = await _userRepository.QueryUserClinicAssignments()
            .Where(a => a.CenterId == centerId
                && clinicIds.Contains(a.ClinicId)
                && a.IsActive)
            .GroupBy(a => a.ClinicId)
            .Select(g => new { ClinicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Count, cancellationToken);

        var items = page.Select(x => new CenterClinicForAssignmentDto
        {
            CenterClinicId = x.CenterClinicId,
            CenterId = x.CenterId,
            ClinicId = x.ClinicId,
            ClinicCode = x.Clinic.Code,
            ClinicNameAr = x.Clinic.NameAr,
            ClinicSortOrder = x.Clinic.SortOrder,
            IsActive = x.IsActive,
            ActiveDoctorsCount = doctorCounts.GetValueOrDefault(x.ClinicId)
        }).ToList();

        return new PaginatedList<CenterClinicForAssignmentDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PaginatedList<CenterDoctorDto>> GetCenterDoctorsAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);
        await EnsureActorCanManageCenterAsync(centerId, cancellationToken);

        if (await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken) is null)
            throw new MedicalCenterNotFoundException();

        NormalizePaging(ref pageNumber, ref pageSize);

        var doctorUserIdsQuery = _userRepository.QueryRoleAssociations()
            .Where(ura => ura.IsActive
                && ura.CenterId == centerId
                && ura.Role.Name == ClinicDoctorRoleName
                && ura.Role.IsActive)
            .Select(ura => ura.UserId)
            .Distinct();

        var totalCount = await doctorUserIdsQuery.CountAsync(cancellationToken);
        var userIds = await doctorUserIdsQuery
            .OrderBy(id => id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var users = await _userRepository.QueryUsers()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var activeCenterUserIds = await _userRepository.QueryUserCenters()
            .Where(uc => uc.CenterId == centerId && uc.IsActive && userIds.Contains(uc.UserId))
            .Select(uc => uc.UserId)
            .ToListAsync(cancellationToken);
        var activeCenterSet = activeCenterUserIds.ToHashSet(StringComparer.Ordinal);

        var clinicCounts = await _userRepository.QueryUserClinicAssignments()
            .Where(a => a.CenterId == centerId && a.IsActive && userIds.Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var items = userIds.Select(id =>
        {
            users.TryGetValue(id, out var user);
            return new CenterDoctorDto
            {
                UserId = id,
                UserName = user?.UserName,
                FullName = user?.FullName,
                IsActive = user?.IsActive ?? false,
                HasActiveCenterAssignment = activeCenterSet.Contains(id),
                ActiveClinicAssignmentsCount = clinicCounts.GetValueOrDefault(id)
            };
        }).ToList();

        return new PaginatedList<CenterDoctorDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PaginatedList<UserClinicAssignmentDto>> GetClinicDoctorsAsync(
        Guid centerId,
        Guid clinicId,
        int pageNumber,
        int pageSize,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);
        await EnsureActorCanManageCenterAsync(centerId, cancellationToken);

        if (await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken) is null)
            throw new MedicalCenterNotFoundException();

        NormalizePaging(ref pageNumber, ref pageSize);

        var query = _userRepository.QueryUserClinicAssignments()
            .Include(a => a.User)
            .Include(a => a.Center)
            .Include(a => a.Clinic)
            .Where(a => a.CenterId == centerId && a.ClinicId == clinicId);

        if (activeOnly)
            query = query.Where(a => a.IsActive);

        query = query
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.User.FullName ?? a.User.UserName);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<UserClinicAssignmentDto>(
            page.Select(ToDto).ToList(),
            totalCount,
            pageNumber,
            pageSize);
    }

    public async Task<List<UserClinicAssignmentDto>> GetUserClinicAssignmentsAsync(
        string userId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);

        var user = await RequireUserAsync(userId);
        await EnsureCanManageTargetAsync(user.Id, cancellationToken);

        var query = _userRepository.QueryUserClinicAssignments()
            .Where(a => a.UserId == user.Id);

        if (activeOnly)
            query = query.Where(a => a.IsActive);

        var rows = await query
            .Include(a => a.User)
            .Include(a => a.Center)
            .Include(a => a.Clinic)
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.Clinic.SortOrder)
            .ThenBy(a => a.Clinic.Code)
            .ToListAsync(cancellationToken);

        var actorId = _currentUserService.UserId ?? string.Empty;
        if (!await UserManagementScopeGuard.ActorHasUnrestrictedScopeAsync(_access, actorId, cancellationToken))
        {
            var actorCenters = await _userRepository.QueryUserCenters()
                .Where(uc => uc.UserId == actorId && uc.IsActive)
                .Select(uc => uc.CenterId)
                .ToListAsync(cancellationToken);
            var allowed = actorCenters.ToHashSet();
            rows = rows.Where(r => allowed.Contains(r.CenterId)).ToList();
        }

        return rows.Select(ToDto).ToList();
    }

    public async Task<UserClinicAssignmentDto> AssignAsync(
        AssignUserClinicDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);

        if (dto.CenterId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");
        if (dto.ClinicId == Guid.Empty)
            throw new InvalidUserDataException("معرف العيادة غير صالح.");

        var user = await RequireUserAsync(dto.UserId);
        if (!user.IsActive)
            throw new InvalidUserDataException("لا يمكن إسناد طبيب غير فعّال إلى عيادة.");

        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);
        await EnsureCanManageTargetAsync(user.Id, cancellationToken);
        await EnsureActorCanManageCenterAsync(dto.CenterId, cancellationToken);

        var center = await _centers.GetByIdAsync(dto.CenterId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        if (!await _access.IsActivelyAssignedAsync(user.Id, dto.CenterId, cancellationToken))
            throw new InvalidUserDataException(
                "لا يمكن إسناد الطبيب إلى عيادة دون وجود إسناد فعّال إلى المركز نفسه.");

        var hasDoctorRole = await _userRepository.QueryRoleAssociations()
            .AnyAsync(
                ura => ura.UserId == user.Id
                    && ura.IsActive
                    && ura.CenterId == dto.CenterId
                    && ura.Role.Name == ClinicDoctorRoleName
                    && ura.Role.IsActive,
                cancellationToken);
        if (!hasDoctorRole)
            throw new InvalidUserDataException(
                "لا يمكن إسناد المستخدم إلى عيادة ما لم يكن مسنداً بدور طبيب العيادة في ذلك المركز.");

        var link = await _centerClinics.GetByCenterAndClinicAsync(
            dto.CenterId, dto.ClinicId, asNoTracking: true, cancellationToken);
        CenterClinicActivityGuard.EnsureAcceptsDoctorAssignment(link);
        if (link!.Clinic is null || !link.Clinic.IsActive)
            throw new InactiveClinicException("لا يمكن إسناد طبيب إلى عيادة معطّلة.");

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingActive = await _userRepository.QueryUserClinicAssignments(asNoTracking: false)
                .FirstOrDefaultAsync(
                    a => a.UserId == user.Id
                        && a.CenterId == dto.CenterId
                        && a.ClinicId == dto.ClinicId
                        && a.IsActive,
                    cancellationToken);

            if (existingActive is not null)
                throw new InvalidUserDataException("يوجد إسناد فعّال مكرر لهذا الطبيب والعيادة.");

            var assignedAt = DateTimeOffset.UtcNow;
            var specialty = link.Clinic.NameAr;

            var latestInactive = await _userRepository.QueryUserClinicAssignments(asNoTracking: false)
                .Where(a => a.UserId == user.Id
                    && a.CenterId == dto.CenterId
                    && a.ClinicId == dto.ClinicId
                    && !a.IsActive)
                .OrderByDescending(a => a.RevokedAt)
                .ThenByDescending(a => a.AssignedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestInactive is not null)
            {
                IamRetention.Reactivate(latestInactive, actorId, assignedAt);
                latestInactive.SpecialtyTitle = specialty;
            }
            else
            {
                _userRepository.AddUserClinicAssignment(new UserClinicAssignment
                {
                    AssignmentId = Guid.NewGuid(),
                    UserId = user.Id,
                    CenterId = dto.CenterId,
                    ClinicId = dto.ClinicId,
                    SpecialtyTitle = specialty,
                    IsActive = true,
                    AssignedAt = assignedAt,
                    AssignedByUserId = actorId
                });
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var saved = await _userRepository.QueryUserClinicAssignments()
                .Include(a => a.User)
                .Include(a => a.Center)
                .Include(a => a.Clinic)
                .FirstAsync(
                    a => a.UserId == user.Id
                        && a.CenterId == dto.CenterId
                        && a.ClinicId == dto.ClinicId
                        && a.IsActive,
                    cancellationToken);
            return ToDto(saved);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            var raced = await _userRepository.QueryUserClinicAssignments()
                .AnyAsync(
                    a => a.UserId == user.Id
                        && a.CenterId == dto.CenterId
                        && a.ClinicId == dto.ClinicId
                        && a.IsActive,
                    cancellationToken);
            if (raced)
                throw new InvalidUserDataException("يوجد إسناد فعّال مكرر لهذا الطبيب والعيادة.");
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RevokeAsync(
        RevokeUserClinicDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.UserClinics.Manage);

        if (dto.AssignmentId == Guid.Empty)
            throw new InvalidUserDataException("معرف الإسناد غير صالح.");

        var reason = (dto.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidUserDataException("سبب الإلغاء مطلوب.");

        var actorId = AssignmentStamp.RequireActor(_currentUserService.UserId);

        using var tx = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await _userRepository.QueryUserClinicAssignments(asNoTracking: false)
                .FirstOrDefaultAsync(a => a.AssignmentId == dto.AssignmentId, cancellationToken)
                ?? throw new InvalidUserDataException("إسناد العيادة غير موجود.");

            await EnsureCanManageTargetAsync(row.UserId, cancellationToken);
            await EnsureActorCanManageCenterAsync(row.CenterId, cancellationToken);

            if (!row.IsActive)
                throw new InvalidUserDataException("إسناد العيادة ملغى مسبقاً.");

            IamRetention.Revoke(row, actorId, reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureActorCanManageCenterAsync(Guid centerId, CancellationToken cancellationToken)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanAssignCenterAsync(
            _access, actorId, centerId, cancellationToken);
    }

    private async Task EnsureCanManageTargetAsync(string targetUserId, CancellationToken cancellationToken)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        await UserManagementScopeGuard.EnsureCanManageTargetAsync(
            _access, _userRepository, actorId, targetUserId, cancellationToken);
    }

    private async Task<User> RequireUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidUserDataException("معرف المستخدم غير صالح.");

        return await _userManager.FindByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);
    }

    private static void NormalizePaging(ref int pageNumber, ref int pageSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 500) pageSize = 500;
    }

    private static UserClinicAssignmentDto ToDto(UserClinicAssignment row) => new()
    {
        AssignmentId = row.AssignmentId,
        UserId = row.UserId,
        UserName = row.User?.UserName,
        FullName = row.User?.FullName,
        CenterId = row.CenterId,
        CenterCode = row.Center?.Code,
        CenterNameAr = row.Center?.NameAr,
        ClinicId = row.ClinicId,
        ClinicCode = row.Clinic?.Code,
        ClinicNameAr = row.Clinic?.NameAr,
        SpecialtyTitle = row.SpecialtyTitle,
        IsActive = row.IsActive,
        AssignedAt = row.AssignedAt,
        RevokedAt = row.RevokedAt,
        RevokeReason = row.RevokeReason
    };
}
