using Domain.Entities.Auth;

namespace Application.Common.Security;

/// <summary>
/// Sensitive IAM rows are deactivated or revoked in place.
/// A later grant reactivates the latest row for the same natural key and clears the revocation fields.
/// AuditEvent keeps the previous revocation. UserCenter uses this policy because its primary key is (UserId, CenterId).
/// </summary>
public static class IamRetention
{
    public const string RoleWithdrawn = "ROLE_WITHDRAWN";
    public const string DirectPermissionWithdrawn = "DIRECT_PERMISSION_WITHDRAWN";
    public const string RolePermissionWithdrawn = "ROLE_PERMISSION_WITHDRAWN";
    public const string CenterRevoked = "CENTER_REVOKED";
    public const string ClinicRevoked = "CLINIC_REVOKED";
    public const string ObsoletePermission = "OBSOLETE_PERMISSION";
    public const string PermissionRenamed = "PERMISSION_RENAMED";

    public static void DeactivateUser(User user) => user.IsActive = false;

    public static void DeactivateRole(Role role, string? actorId)
    {
        role.IsActive = false;
        role.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(actorId))
            role.UpdatedBy = actorId;
    }

    public static void DeactivatePermission(Permission permission) => permission.IsActive = false;

    public static void Revoke(UserRoleAssociation row, string actorId, string reason)
    {
        if (!row.IsActive)
            return;

        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = actorId;
        row.RevokeReason = reason;
    }

    public static void Revoke(UserPermission row, string actorId, string reason)
    {
        if (!row.IsActive)
            return;

        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = actorId;
        row.RevokeReason = reason;
    }

    public static void Revoke(RolePermission row, string actorId, string reason)
    {
        if (!row.IsActive)
            return;

        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = actorId;
        row.RevokeReason = reason;
    }

    public static void Revoke(UserCenter row, string actorId, string reason)
    {
        if (!row.IsActive)
            return;

        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = actorId;
        row.RevokeReason = reason;
    }

    public static void Revoke(UserClinicAssignment row, string actorId, string reason)
    {
        if (!row.IsActive)
            return;

        row.IsActive = false;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedByUserId = actorId;
        row.RevokeReason = reason;
    }

    public static void Reactivate(UserRoleAssociation row, string actorId, DateTimeOffset assignedAt)
    {
        row.IsActive = true;
        row.AssignedByUserId = actorId;
        row.AssignedAt = assignedAt;
        row.RevokedAt = null;
        row.RevokedByUserId = null;
        row.RevokeReason = null;
    }

    public static void Reactivate(UserPermission row, string actorId, DateTimeOffset grantedAt)
    {
        row.IsActive = true;
        row.GrantedByUserId = actorId;
        row.GrantedAt = grantedAt;
        row.RevokedAt = null;
        row.RevokedByUserId = null;
        row.RevokeReason = null;
    }

    public static void Reactivate(RolePermission row, string actorId, DateTimeOffset grantedAt)
    {
        row.IsActive = true;
        row.GrantedByUserId = actorId;
        row.GrantedAt = grantedAt;
        row.RevokedAt = null;
        row.RevokedByUserId = null;
        row.RevokeReason = null;
    }

    public static void Reactivate(UserCenter row, string actorId, DateTimeOffset assignedAt)
    {
        row.IsActive = true;
        row.AssignedByUserId = actorId;
        row.AssignedAt = assignedAt;
        row.RevokedAt = null;
        row.RevokedByUserId = null;
        row.RevokeReason = null;
    }

    public static void Reactivate(UserClinicAssignment row, string actorId, DateTimeOffset assignedAt)
    {
        row.IsActive = true;
        row.AssignedByUserId = actorId;
        row.AssignedAt = assignedAt;
        row.RevokedAt = null;
        row.RevokedByUserId = null;
        row.RevokeReason = null;
    }
}
