using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AuthServices;

public partial class UserAssignmentService
{
    private sealed class CenterRoleAssignmentRow
    {
        public Guid RoleAssignmentId { get; init; }
        public Guid ClinicAssignmentId { get; init; }
        public string UserId { get; init; } = string.Empty;
        public string? UserName { get; init; }
        public string? FullName { get; init; }
        public bool UserIsActive { get; init; }
        public string RoleId { get; init; } = string.Empty;
        public string? RoleName { get; init; }
        public RoleScope RoleScope { get; init; }
        public Guid CenterId { get; init; }
        public string? CenterCode { get; init; }
        public string? CenterNameAr { get; init; }
        public Guid? ClinicId { get; init; }
        public string? ClinicCode { get; init; }
        public string? ClinicNameAr { get; init; }
        public string? SpecialtyTitle { get; init; }
        public bool IsRoleAssignmentActive { get; init; }
        public DateTimeOffset AssignedAt { get; init; }
        public DateTimeOffset LastChangedAt { get; init; }
        public string? AssignedByUserId { get; init; }
        public string? AssignedByUserName { get; init; }
        public string? AssignedByFullName { get; init; }
        public string? RevokeReason { get; init; }
    }

    private sealed class CenterDirectPermissionRow
    {
        public Guid AssignmentId { get; init; }
        public string UserId { get; init; } = string.Empty;
        public string? UserName { get; init; }
        public string? FullName { get; init; }
        public bool UserIsActive { get; init; }
        public Guid PermissionId { get; init; }
        public string? PermissionName { get; init; }
        public Guid? CenterId { get; init; }
        public string? CenterCode { get; init; }
        public string? CenterNameAr { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset GrantedAt { get; init; }
        public DateTimeOffset LastChangedAt { get; init; }
        public string? GrantedByUserId { get; init; }
        public string? GrantedByUserName { get; init; }
        public string? GrantedByFullName { get; init; }
        public string? RevokeReason { get; init; }
    }

    public async Task<PaginatedList<CenterRoleAssignmentListItemDto>> ListCenterRoleAssignmentsAsync(
        CenterRoleAssignmentListQuery query,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        var centerId = await ResolveListCenterIdAsync(query.CenterId, cancellationToken);
        var (pageNumber, pageSize) = NormalizeListPaging(query.PageNumber, query.PageSize);

        var rows = BuildCenterRoleAssignmentRows(centerId);
        rows = ApplyRoleListFilters(rows, query);

        rows = rows
            .OrderByDescending(r => r.LastChangedAt)
            .ThenByDescending(r => r.RoleAssignmentId)
            .ThenBy(r => r.ClinicAssignmentId)
            .ThenBy(r => r.UserId);

        var page = await PaginatedList<CenterRoleAssignmentRow>.CreateAsync(rows, pageNumber, pageSize);
        var items = page.Items.Select(MapRoleListItem).ToList();

        return new PaginatedList<CenterRoleAssignmentListItemDto>(
            items, page.TotalCount, page.PageNumber, pageSize);
    }

    public async Task<PaginatedList<CenterDirectPermissionListItemDto>> ListCenterDirectPermissionsAsync(
        CenterDirectPermissionListQuery query,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(SystemPermissions.UserManagement.Read);

        var centerId = await ResolveListCenterIdAsync(query.CenterId, cancellationToken);
        var (pageNumber, pageSize) = NormalizeListPaging(query.PageNumber, query.PageSize);

        var rows = BuildCenterDirectPermissionRows(centerId);
        rows = ApplyDirectListFilters(rows, query);

        rows = rows
            .OrderByDescending(r => r.LastChangedAt)
            .ThenByDescending(r => r.AssignmentId)
            .ThenBy(r => r.UserId);

        var page = await PaginatedList<CenterDirectPermissionRow>.CreateAsync(rows, pageNumber, pageSize);
        var items = page.Items.Select(MapDirectListItem).ToList();

        return new PaginatedList<CenterDirectPermissionListItemDto>(
            items, page.TotalCount, page.PageNumber, pageSize);
    }

    private async Task<Guid> ResolveListCenterIdAsync(Guid? requestedCenterId, CancellationToken cancellationToken)
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        Guid centerId;

        if (requestedCenterId is Guid requested && requested != Guid.Empty)
            centerId = requested;
        else if (!_currentCenter.IsCentralContext && _currentCenter.CenterId is Guid active)
            centerId = active;
        else
            throw new InvalidUserDataException("معرف المركز مطلوب.");

        await UserManagementScopeGuard.EnsureCanViewCenterDataAsync(
            _access,
            _centers,
            actorId,
            centerId,
            _currentCenter,
            SystemPermissions.UserManagement.Read,
            cancellationToken);

        return centerId;
    }

    private static (int PageNumber, int PageSize) NormalizeListPaging(int pageNumber, int pageSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;
        return (pageNumber, pageSize);
    }

    private IQueryable<CenterRoleAssignmentRow> BuildCenterRoleAssignmentRows(Guid centerId)
    {
        var clinicDoctor = UserClinicAssignmentService.ClinicDoctorRoleName;
        var clinicAssignments = _userRepository.QueryUserClinicAssignments();

        var roleNames = _roleRepository.QueryRoles();
        var baseQuery = _userRepository.QueryRoleAssociations()
            .Where(ura => ura.CenterId == centerId);

        var nonExploded =
            from ura in baseQuery
            join role in roleNames on ura.RoleId equals role.Id
            where !(ura.IsActive && role.Name == clinicDoctor)
            select new CenterRoleAssignmentRow
            {
                RoleAssignmentId = ura.Id,
                ClinicAssignmentId = Guid.Empty,
                UserId = ura.UserId,
                UserName = ura.User.UserName,
                FullName = ura.User.FullName,
                UserIsActive = ura.User.IsActive,
                RoleId = ura.RoleId,
                RoleName = role.Name,
                RoleScope = role.Scope,
                CenterId = ura.CenterId!.Value,
                CenterCode = ura.Center!.Code,
                CenterNameAr = ura.Center.NameAr,
                ClinicId = null,
                ClinicCode = null,
                ClinicNameAr = null,
                SpecialtyTitle = null,
                IsRoleAssignmentActive = ura.IsActive,
                AssignedAt = ura.AssignedAt,
                LastChangedAt = ura.RevokedAt ?? ura.AssignedAt,
                AssignedByUserId = ura.AssignedByUserId,
                AssignedByUserName = ura.AssignedByUser.UserName,
                AssignedByFullName = ura.AssignedByUser.FullName,
                RevokeReason = ura.RevokeReason
            };

        var exploded =
            from ura in baseQuery
            join role in roleNames on ura.RoleId equals role.Id
            where ura.IsActive && role.Name == clinicDoctor
            from uca in clinicAssignments
                .Where(uca =>
                    uca.UserId == ura.UserId
                    && uca.CenterId == centerId
                    && uca.IsActive)
                .DefaultIfEmpty()
            where uca != null
                || !clinicAssignments.Any(uca2 =>
                    uca2.UserId == ura.UserId
                    && uca2.CenterId == centerId
                    && uca2.IsActive)
            select new CenterRoleAssignmentRow
            {
                RoleAssignmentId = ura.Id,
                ClinicAssignmentId = uca != null ? uca.AssignmentId : Guid.Empty,
                UserId = ura.UserId,
                UserName = ura.User.UserName,
                FullName = ura.User.FullName,
                UserIsActive = ura.User.IsActive,
                RoleId = ura.RoleId,
                RoleName = role.Name,
                RoleScope = role.Scope,
                CenterId = ura.CenterId!.Value,
                CenterCode = ura.Center!.Code,
                CenterNameAr = ura.Center.NameAr,
                ClinicId = uca != null ? uca.ClinicId : null,
                ClinicCode = uca != null ? uca.Clinic.Code : null,
                ClinicNameAr = uca != null ? uca.Clinic.NameAr : null,
                SpecialtyTitle = uca != null ? uca.SpecialtyTitle : null,
                IsRoleAssignmentActive = ura.IsActive,
                AssignedAt = ura.AssignedAt,
                LastChangedAt = ura.RevokedAt ?? ura.AssignedAt,
                AssignedByUserId = ura.AssignedByUserId,
                AssignedByUserName = ura.AssignedByUser.UserName,
                AssignedByFullName = ura.AssignedByUser.FullName,
                RevokeReason = ura.RevokeReason
            };

        return nonExploded.Concat(exploded);
    }

    private static IQueryable<CenterRoleAssignmentRow> ApplyRoleListFilters(
        IQueryable<CenterRoleAssignmentRow> query,
        CenterRoleAssignmentListQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var s = filter.Q.Trim();
            query = query.Where(r =>
                (r.FullName != null && EF.Functions.ILike(r.FullName, $"%{s}%"))
                || (r.UserName != null && EF.Functions.ILike(r.UserName, $"%{s}%")));
        }

        if (!string.IsNullOrWhiteSpace(filter.RoleId))
        {
            var roleId = filter.RoleId.Trim();
            query = query.Where(r => r.RoleId == roleId);
        }

        if (filter.ClinicId is Guid clinicId && clinicId != Guid.Empty)
            query = query.Where(r => r.ClinicId == clinicId);

        if (filter.IsActive.HasValue)
            query = query.Where(r => r.IsRoleAssignmentActive == filter.IsActive.Value);

        if (filter.UserIsActive.HasValue)
            query = query.Where(r => r.UserIsActive == filter.UserIsActive.Value);

        return query;
    }

    private IQueryable<CenterDirectPermissionRow> BuildCenterDirectPermissionRows(Guid centerId)
    {
        return _userRepository.QueryUserPermissions()
            .Where(up => up.CenterId == centerId)
            .Select(up => new CenterDirectPermissionRow
            {
                AssignmentId = up.Id,
                UserId = up.UserId,
                UserName = up.User.UserName,
                FullName = up.User.FullName,
                UserIsActive = up.User.IsActive,
                PermissionId = up.PermissionId,
                PermissionName = up.Permission.Name,
                CenterId = up.CenterId,
                CenterCode = up.Center!.Code,
                CenterNameAr = up.Center.NameAr,
                IsActive = up.IsActive,
                GrantedAt = up.GrantedAt,
                LastChangedAt = up.RevokedAt ?? up.GrantedAt,
                GrantedByUserId = up.GrantedByUserId,
                GrantedByUserName = up.GrantedByUser.UserName,
                GrantedByFullName = up.GrantedByUser.FullName,
                RevokeReason = up.RevokeReason
            });
    }

    private static IQueryable<CenterDirectPermissionRow> ApplyDirectListFilters(
        IQueryable<CenterDirectPermissionRow> query,
        CenterDirectPermissionListQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var s = filter.Q.Trim();
            query = query.Where(r =>
                (r.FullName != null && EF.Functions.ILike(r.FullName, $"%{s}%"))
                || (r.UserName != null && EF.Functions.ILike(r.UserName, $"%{s}%"))
                || (r.PermissionName != null && EF.Functions.ILike(r.PermissionName, $"%{s}%")));
        }

        if (filter.PermissionId is Guid permissionId && permissionId != Guid.Empty)
            query = query.Where(r => r.PermissionId == permissionId);

        if (filter.IsActive.HasValue)
            query = query.Where(r => r.IsActive == filter.IsActive.Value);

        if (filter.UserIsActive.HasValue)
            query = query.Where(r => r.UserIsActive == filter.UserIsActive.Value);

        return query;
    }

    private static CenterRoleAssignmentListItemDto MapRoleListItem(CenterRoleAssignmentRow row)
    {
        var roleName = row.RoleName ?? string.Empty;
        var clinicAssignmentId = row.ClinicAssignmentId == Guid.Empty ? (Guid?)null : row.ClinicAssignmentId;
        var rowKey = clinicAssignmentId.HasValue
            ? $"{row.RoleAssignmentId}:{clinicAssignmentId.Value}"
            : $"{row.RoleAssignmentId}:{Guid.Empty}";

        return new CenterRoleAssignmentListItemDto
        {
            RowKey = rowKey,
            RoleAssignmentId = row.RoleAssignmentId,
            ClinicAssignmentId = clinicAssignmentId,
            UserId = row.UserId,
            UserName = row.UserName,
            FullName = row.FullName,
            RoleId = row.RoleId,
            RoleName = roleName,
            RoleNameAr = AuthDisplayNames.RoleAr(roleName),
            RoleScope = row.RoleScope,
            CenterId = row.CenterId,
            CenterCode = row.CenterCode,
            CenterNameAr = row.CenterNameAr,
            AssignmentKindAr = "دور محلي",
            ClinicId = row.ClinicId,
            ClinicCode = row.ClinicCode,
            ClinicNameAr = row.ClinicNameAr,
            SpecialtyTitle = row.SpecialtyTitle,
            IsRoleAssignmentActive = row.IsRoleAssignmentActive,
            UserIsActive = row.UserIsActive,
            AssignedAt = row.AssignedAt,
            LastChangedAt = row.LastChangedAt,
            AssignedByUserId = row.AssignedByUserId,
            AssignedByUserName = row.AssignedByUserName,
            AssignedByFullName = row.AssignedByFullName,
            RevokeReason = row.RevokeReason
        };
    }

    private static CenterDirectPermissionListItemDto MapDirectListItem(CenterDirectPermissionRow row)
    {
        var permissionName = row.PermissionName ?? string.Empty;
        return new CenterDirectPermissionListItemDto
        {
            AssignmentId = row.AssignmentId,
            UserId = row.UserId,
            UserName = row.UserName,
            FullName = row.FullName,
            PermissionId = row.PermissionId,
            PermissionName = permissionName,
            PermissionNameAr = AuthDisplayNames.PermissionAr(permissionName),
            CenterId = row.CenterId,
            CenterCode = row.CenterCode,
            CenterNameAr = row.CenterNameAr,
            IsCentralScope = false,
            IsActive = row.IsActive,
            UserIsActive = row.UserIsActive,
            GrantedAt = row.GrantedAt,
            LastChangedAt = row.LastChangedAt,
            GrantedByUserId = row.GrantedByUserId,
            GrantedByUserName = row.GrantedByUserName,
            GrantedByFullName = row.GrantedByFullName,
            RevokeReason = row.RevokeReason
        };
    }
}
