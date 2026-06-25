using System.Text.Json;

namespace API.Infrastructure;

/// <summary>
/// خيارات JSON موحّدة لاستجابات الـ API خارج مسار Controllers (Middleware، Filters).
/// </summary>
internal static class ApiJsonSerializerOptions
{
    public static JsonSerializerOptions CamelCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
