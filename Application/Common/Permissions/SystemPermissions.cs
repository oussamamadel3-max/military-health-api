namespace Application.Common.Permissions;

public static class SystemPermissions
{
    public static class UserManagement
    {
        public const string Read = "UserManagement.Read";
        public const string Create = "UserManagement.Create";
        public const string Edit = "UserManagement.Edit";
        public const string Delete = "UserManagement.Delete";
        public const string ChangeStatus = "UserManagement.ChangeStatus";
        public const string AssignRoles = "UserManagement.AssignRoles";
        public const string AssignPermissions = "UserManagement.AssignPermissions";
        public const string ResetPassword = "UserManagement.ResetPassword";
        public const string RemoveFieldPermissions = "UserManagement.RemoveFieldPermissions";
    }

    public static class RoleManagement
    {
        public const string Read = "RoleManagement.Read";
        public const string Create = "RoleManagement.Create";
        public const string Edit = "RoleManagement.Edit";
        public const string Delete = "RoleManagement.Delete";
        public const string ChangeStatus = "RoleManagement.ChangeStatus";
        public const string AssignPermissions = "RoleManagement.AssignPermissions";
    }

    public static class PermissionManagement
    {
        public const string Read = "PermissionManagement.Read";
        public const string ChangeStatus = "PermissionManagement.ChangeStatus";
    }

    public static class FieldPermissionManagement
    {
        public const string Read = "FieldPermissionManagement.Read";
        public const string Create = "FieldPermissionManagement.Create";
        public const string Edit = "FieldPermissionManagement.Edit";
        public const string Delete = "FieldPermissionManagement.Delete";
        public const string ChangeAccessType = "FieldPermissionManagement.ChangeAccessType";
    }

    public static class AuditLog
    {
        public const string Read = "AuditLog.Read";
        public const string Export = "AuditLog.Export";
    }

    public static class Dashboard
    {
        public const string Read = "Dashboard.Read";
        public const string Export = "Dashboard.Export";
    }

    /// <summary>
    /// صلاحيات الكتابة في وحدات IAM المستخدمة لتحديد «مسؤول IAM».
    /// </summary>
    public static readonly string[] IamWriteCapabilities =
    [
        UserManagement.Create, UserManagement.Edit, UserManagement.Delete,
        UserManagement.ChangeStatus, UserManagement.AssignRoles, UserManagement.AssignPermissions,
        UserManagement.ResetPassword, UserManagement.RemoveFieldPermissions,
        RoleManagement.Create, RoleManagement.Edit, RoleManagement.Delete,
        RoleManagement.ChangeStatus, RoleManagement.AssignPermissions,
        PermissionManagement.ChangeStatus,
        FieldPermissionManagement.Create, FieldPermissionManagement.Edit,
        FieldPermissionManagement.Delete, FieldPermissionManagement.ChangeAccessType,
    ];

    public static readonly string[] UserManagementWrite =
    [
        UserManagement.Create, UserManagement.Edit, UserManagement.Delete,
        UserManagement.ChangeStatus, UserManagement.AssignRoles, UserManagement.AssignPermissions,
        UserManagement.ResetPassword, UserManagement.RemoveFieldPermissions,
    ];

    public static readonly string[] RoleManagementWrite =
    [
        RoleManagement.Create, RoleManagement.Edit, RoleManagement.Delete,
        RoleManagement.ChangeStatus, RoleManagement.AssignPermissions,
    ];

    public static readonly string[] PermissionManagementWrite =
    [
        PermissionManagement.ChangeStatus,
    ];
}
