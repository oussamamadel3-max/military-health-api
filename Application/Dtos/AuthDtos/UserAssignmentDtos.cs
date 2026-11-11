using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

public class UserCenterAssignmentDto
{
    public Guid CenterId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool CenterIsActive { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}

public class UserRoleAssignmentDto
{
    public Guid AssignmentId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public Guid? CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public bool IsCentralScope { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public string? AssignedByUserId { get; set; }
    public string? AssignedByUserName { get; set; }
    public string? AssignedByFullName { get; set; }
    public string? RevokeReason { get; set; }
    public List<UserRoleClinicSummaryDto> Clinics { get; set; } = new();
}

public class UserAssignmentsDto
{
    public string UserId { get; set; } = string.Empty;
    public List<UserCenterAssignmentDto> Centers { get; set; } = new();
    public List<UserRoleAssignmentDto> Roles { get; set; } = new();
}

public class AssignUserCenterDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف المركز مطلوب.")]
    public Guid CenterId { get; set; }
}

public class RevokeUserCenterDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف المركز مطلوب.")]
    public Guid CenterId { get; set; }

    [Required(ErrorMessage = "سبب الإلغاء مطلوب.")]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = null!;

    /// <summary>
    /// مطلوب عند إلغاء آخر مركز فعّال للمستخدم (FR-019 A1).
    /// </summary>
    public bool ConfirmLastCenter { get; set; }
}

public class AssignUserRoleDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف الدور مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string RoleId { get; set; } = null!;

    /// <summary>
    /// فارغ = إسناد مركزي عام. قيمة = دور مقيد بذلك المركز.
    /// </summary>
    public Guid? CenterId { get; set; }
}

public class RevokeUserRoleDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف الدور مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string RoleId { get; set; } = null!;

    public Guid? CenterId { get; set; }

    [Required(ErrorMessage = "سبب الإلغاء مطلوب.")]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = null!;
}

public class UserClinicAssignmentDto
{
    public Guid AssignmentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public Guid CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public Guid ClinicId { get; set; }
    public string? ClinicCode { get; set; }
    public string? ClinicNameAr { get; set; }
    public string? SpecialtyTitle { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}

public class CenterDoctorDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public bool HasActiveCenterAssignment { get; set; }
    public int ActiveClinicAssignmentsCount { get; set; }
}

public class CenterClinicForAssignmentDto
{
    public Guid CenterClinicId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = string.Empty;
    public string ClinicNameAr { get; set; } = string.Empty;
    public short ClinicSortOrder { get; set; }
    public bool IsActive { get; set; }
    public int ActiveDoctorsCount { get; set; }
    public int EffectiveDoctorsCount { get; set; }
}

public class AssignUserClinicDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف المركز مطلوب.")]
    public Guid CenterId { get; set; }

    [Required(ErrorMessage = "معرف العيادة مطلوب.")]
    public Guid ClinicId { get; set; }
}

public class RevokeUserClinicDto
{
    [Required(ErrorMessage = "معرف الإسناد مطلوب.")]
    public Guid AssignmentId { get; set; }

    [Required(ErrorMessage = "سبب الإلغاء مطلوب.")]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = null!;
}
