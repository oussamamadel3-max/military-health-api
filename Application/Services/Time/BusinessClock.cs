using System.Globalization;
using Application.Interfaces.Services;

namespace Application.Services.Time;

/// <summary>
/// ساعة العمل. في الإنتاج تُنشأ بلا معاملات فتأخذ <see cref="TimeZoneInfo.Local"/>،
/// أي المنطقة المضبوطة على عملية الـ API: منطقة Windows، أو <c>/etc/localtime</c> و<c>TZ</c> على Linux،
/// أو متغير <c>TZ</c> في Docker. صور aspnet الرسمية بلا <c>TZ</c> تبقى على UTC.
/// كل نسخ الـ API يجب أن تعمل على المنطقة نفسها حتى تتطابق الفلاتر اليومية.
/// الاختبارات تحقن <see cref="TimeZoneInfo"/> مباشرة، و<see cref="Utc"/> تُستخدم عندما لا تُحقن ساعة.
/// التخزين يبقى <see cref="DateTimeOffset"/>؛ هذه الساعة تحوّل حدود اليوم والعرض فقط.
/// </summary>
public sealed class BusinessClock : IBusinessClock
{
    /// <summary>ساعة ثابتة للاختبارات التي تبني الخدمات دون حقن منطقة.</summary>
    public static BusinessClock Utc { get; } = new(TimeZoneInfo.Utc);

    /// <summary>ساعة الإنتاج: منطقة عملية الخادم الحالية، مع قواعد التوقيت الصيفي للمنطقة.</summary>
    public BusinessClock()
        : this(TimeZoneInfo.Local)
    {
    }

    /// <summary>ينشئ ساعة لمنطقة محددة. الاختبارات تمرر منطقة مخصصة أو UTC.</summary>
    public BusinessClock(TimeZoneInfo timeZone)
    {
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    /// <inheritdoc />
    public TimeZoneInfo TimeZone { get; }

    /// <inheritdoc />
    public DateTimeOffset StartOfDay(DateOnly date)
        => At(date.ToDateTime(TimeOnly.MinValue));

    /// <inheritdoc />
    public DateTimeOffset EndOfDayExclusive(DateOnly date)
        => StartOfDay(date.AddDays(1));

    /// <inheritdoc />
    public DateTimeOffset At(DateTime localUnspecified)
    {
        var value = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        return new DateTimeOffset(value, TimeZone.GetUtcOffset(value));
    }

    /// <inheritdoc />
    public string Format(DateTimeOffset instant, string format)
        => Format(instant, TimeZone, format);

    /// <summary>ينسّق لحظة في منطقة عرض محددة دون استخدام منطقة الخادم.</summary>
    public static string Format(DateTimeOffset instant, TimeZoneInfo zone, string format)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(instant, zone).ToString(format, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public (DateTimeOffset? Start, DateTimeOffset? EndExclusive) NormalizeDateTimeRange(DateTime? from, DateTime? to)
    {
        DateTimeOffset? start = from.HasValue ? At(from.Value) : null;
        DateTimeOffset? end = null;
        if (to.HasValue)
        {
            end = to.Value.TimeOfDay == TimeSpan.Zero
                ? EndOfDayExclusive(DateOnly.FromDateTime(to.Value))
                : At(to.Value);
        }

        return (start, end);
    }
}
