namespace Application.Interfaces.Services;

/// <summary>
/// ساعة العمل المشتركة لحدود اليوم وتحويل العرض. التخزين يبقى UTC في <see cref="DateTimeOffset"/>.
/// التنفيذ الإنتاجي يستخدم منطقة عملية الخادم، لا معرفاً من الإعدادات.
/// </summary>
public interface IBusinessClock
{
    /// <summary>المنطقة المستخدمة لحساب اليوم والعرض.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>بداية اليوم التقويمي في منطقة الساعة، شاملة، وقيمتها المطلقة على UTC.</summary>
    DateTimeOffset StartOfDay(DateOnly date);

    /// <summary>بداية اليوم التالي في منطقة الساعة، حصرية، وقيمتها المطلقة على UTC.</summary>
    DateTimeOffset EndOfDayExclusive(DateOnly date);

    /// <summary>لحظة محلية غير محددة النوع داخل منطقة الساعة.</summary>
    DateTimeOffset At(DateTime localUnspecified);

    /// <summary>نص العرض للحظة مخزّنة، بأرقام لاتينية ثابتة، دون تغيير القيمة المخزّنة.</summary>
    string Format(DateTimeOffset instant, string format);

    /// <summary>
    /// يحوّل مجالاً من تواريخ غير محددة النوع إلى فترة منطقة الساعة.
    /// منتصف الليل في تاريخ النهاية يعني نهاية ذلك اليوم حصرية.
    /// </summary>
    (DateTimeOffset? Start, DateTimeOffset? EndExclusive) NormalizeDateTimeRange(DateTime? from, DateTime? to);
}
