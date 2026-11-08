using System.Text.Json;
using Domain.Entities.Audit;

namespace Application.Common.Localization;

/// <summary>قراءة مساعدة لقيم JSON وReasonText في أحداث التدقيق دون كشف أسرار.</summary>
internal static class AuditEventValueReader
{
    public static bool ReasonMentions(AuditEvent e, string field)
        => (e.ReasonText ?? string.Empty).Contains(field, StringComparison.OrdinalIgnoreCase);

    public static bool ReasonOnlyMentions(AuditEvent e, string field)
    {
        var reason = e.ReasonText ?? string.Empty;
        const string marker = "الخصائص التي جرى تعديل عليها:";
        var idx = reason.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return false;
        var list = reason[(idx + marker.Length)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Length == 1 && string.Equals(list[0], field, StringComparison.OrdinalIgnoreCase);
    }

    public static string? SummarizeChangedFields(string? reasonText)
    {
        if (string.IsNullOrWhiteSpace(reasonText)) return null;
        const string marker = "الخصائص التي جرى تعديل عليها:";
        var idx = reasonText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return reasonText;
        return "الحقول: " + reasonText[(idx + marker.Length)..].Trim();
    }

    public static bool IsDeactivation(AuditEvent e)
        => string.Equals(e.EventType, "Modified", StringComparison.OrdinalIgnoreCase)
           && ReasonMentions(e, "IsActive")
           && ReadBool(e.NewValues, "IsActive") == false;

    public static bool? ReadBool(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(property, out var el))
                return null;
            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(property, out var el))
                return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
