namespace Application.Common;

public static class CoreStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
    public const string Archived = "ARCHIVED";
    public const string Revoked = "REVOKED";

    public static readonly string[] OrgUnitStatuses = { Active, Inactive, Archived };
    public static readonly string[] UserOrgScopeStatuses = { Active, Inactive, Revoked };
}

public static class ScopeModes
{
    public const string Self = "SELF";
    public const string Subtree = "SUBTREE";
    public static readonly string[] All = { Self, Subtree };
}
