namespace Application.Common.Persistence;

/// <summary>
/// بحث نصي داخل حمولة jsonb المخزَّنة كسلسلة في CLR.
/// تنفيذ الذاكرة للاختبارات؛ يُترجم عبر EF إلى <c>(jsonb)::text LIKE</c>.
/// </summary>
public static class AuditJsonbText
{
    /// <summary>
    /// يتحقق من احتواء تمثيل jsonb النصي على مقطع محدد دون استخدام عامل LIKE على نوع jsonb مباشرة.
    /// </summary>
    public static bool Contains(string? json, string substring)
        => json is not null
           && substring is not null
           && json.Contains(substring, StringComparison.Ordinal);
}
