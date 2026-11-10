using System.ComponentModel.DataAnnotations;
using Domain.Enums;

namespace Application.Dtos.AuthDtos;

public class CenterRoleAssignmentListQuery
{
    public Guid? CenterId { get; set; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 500)]
    public int PageSize { get; set; } = 10;

    public string? Q { get; set; }
    public string? RoleId { get; set; }
    public Guid? ClinicId { get; set; }
    public bool? IsActive { get; set; }
    public bool? UserIsActive { get; set; }
}

public class CenterDirectPermissionListQuery
{
    public Guid? CenterId { get; set; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 500)]
    public int PageSize { get; set; } = 10;

    public string? Q { get; set; }
    public Guid? PermissionId { get; set; }
    public bool? IsActive { get; set; }
    public bool? UserIsActive { get; set; }
}

public class CenterRoleAssignmentListItemDto
{
    public string RowKey { get; set; } = string.Empty;
    public Guid RoleAssignmentId { get; set; }
    public Guid? ClinicAssignmentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleNameAr { get; set; } = string.Empty;
    public RoleScope RoleScope { get; set; }
    public Guid CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public string AssignmentKindAr { get; set; } = string.Empty;
    public Guid? ClinicId { get; set; }
    public string? ClinicCode { get; set; }
    public string? ClinicNameAr { get; set; }
    public string? SpecialtyTitle { get; set; }
    public bool IsRoleAssignmentActive { get; set; }
    public bool UserIsActive { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public string? AssignedByUserId { get; set; }
    public string? AssignedByUserName { get; set; }
    public string? AssignedByFullName { get; set; }
    public string? RevokeReason { get; set; }
}

public class CenterDirectPermissionListItemDto
{
    public Guid AssignmentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public Guid PermissionId { get; set; }
    public string PermissionName { get; set; } = string.Empty;
    public string PermissionNameAr { get; set; } = string.Empty;
    public Guid? CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public bool IsCentralScope { get; set; }
    public bool IsActive { get; set; }
    public bool UserIsActive { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public string? GrantedByUserId { get; set; }
    public string? GrantedByUserName { get; set; }
    public string? GrantedByFullName { get; set; }
    public string? RevokeReason { get; set; }
}

public class UserRoleClinicSummaryDto
{
    public Guid ClinicAssignmentId { get; set; }
    public Guid ClinicId { get; set; }
    public string? ClinicCode { get; set; }
    public string? ClinicNameAr { get; set; }
    public string? SpecialtyTitle { get; set; }
    public bool IsActive { get; set; }
}
