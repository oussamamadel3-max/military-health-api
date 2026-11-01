using Application.Dtos.Supervisor;

namespace Application.Interfaces.Services;

/// <summary>
/// إصدار القرار النهائي المعتمد للمنتسب ضمن المركز النشط (FR-053 / FR-054 / FR-055).
/// التعديل مسار لاحق (MH-P6-040).
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
}
