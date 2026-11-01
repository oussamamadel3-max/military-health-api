using Application.Dtos.Supervisor;

namespace Application.Interfaces.Services;

/// <summary>
/// إصدار القرار النهائي المعتمد للمنتسب ضمن المركز النشط (FR-053 / FR-054).
/// التأجيل والتعديل مساران لاحقان (MH-P6-030 / 040).
/// </summary>
public interface IFinalDecisionService
{
    /// <summary>
    /// يصدر قراراً نهائياً واحداً (قبول/رفض) بعد اكتمال العيادات الإلزامية،
    /// مع إلزام مبرر الاختلاف عند التعارض مع تقييم عيادة.
    /// </summary>
    Task<FinalDecisionDto> IssueAsync(
        Guid memberId,
        IssueFinalDecisionRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
