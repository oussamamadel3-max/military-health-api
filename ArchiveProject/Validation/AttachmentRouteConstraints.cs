using System.Text.RegularExpressions;

namespace ArchiveProject.API.Validation;

public static class AttachmentRouteConstraints
{
    private static readonly Regex EntityTypePattern = new(
        @"^[A-Za-z][A-Za-z0-9_]{0,99}$",
        RegexOptions.Compiled);

    public static bool IsValidEntityType(string? entityType) =>
        !string.IsNullOrEmpty(entityType) && EntityTypePattern.IsMatch(entityType);
}
