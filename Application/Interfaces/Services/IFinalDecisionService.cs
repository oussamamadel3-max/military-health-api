using Application.Dtos.Supervisor;

namespace Application.Interfaces.Services;

/// <summary>
/// إصدار وتعديل القرار النهائي المعتمد للمنتسب ضمن المركز النشط
/// (FR-053 / FR-054 / FR-055 / FR-056)، مع تأكيد مراجعة النسخة الحالية
/// كامتداد تشغيلي مكمّل لـ FR-058.
/// </summary>
public interface IFinalDecisionService
{
    /// <summary>
    /// يصدر قراراً نهائياً واحداً (قبول/رفض/مؤجل) بعد اكتمال العيادات الإلزامية،
    /// مع إلزام مبرر الاختلاف عند التعارض، وربط DeferredAction عند التأجيل.
    /// </summary>
    Task<FinalDecisionDto> IssueAsync(
        Guid memberId,
        IssueFinalDecisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعدّل القرار النهائي القائم دون إنشاء قرار ثانٍ، مع سجل تاريخي
    /// وإعادة حالة التصدير إلى غير مصدَّر (FR-056 / BR-016 / BR-021 / BR-022).
    /// </summary>
    Task<AmendFinalDecisionDto> AmendAsync(
        Guid memberId,
        AmendFinalDecisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يؤكّد مراجعة النسخة الحالية من ملف المنتسب دون تغيير القرار الطبي
    /// أو زيادة DataRevision (امتداد تشغيلي لـ FR-058).
    /// </summary>
    Task<ConfirmCurrentRevisionDto> ConfirmCurrentRevisionAsync(
        Guid memberId,
        ConfirmCurrentRevisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعيد سجل تغيّر النتيجة النهائية للمنتسب ضمن المركز النشط (الأحدث أولاً).
    /// </summary>
    Task<IReadOnlyList<FinalDecisionHistoryItemDto>> GetHistoryAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
