namespace API.Validation;

public static class AttachmentRouteConstraints
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "Member", "Consultation", "Investigation", "UserProfile"
    };

    public static bool IsValidEntityType(string? entityType) =>
        !string.IsNullOrEmpty(entityType) && Supported.Contains(entityType);
}
