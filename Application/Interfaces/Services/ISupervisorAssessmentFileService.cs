using Application.Dtos.Supervisor;

namespace Application.Interfaces.Services;

/// <summary>
/// ملف التقييم الطبي المجمّع للمشرف ضمن المركز النشط (FR-051 / FR-052).
/// </summary>
public interface ISupervisorAssessmentFileService
{
    /// <summary>
    /// يفتح ملف المنتسب المجمّع بعد التحقق من الجلسة والمركز النشط وصلاحية القرار النهائي،
    /// دون قفل تحرير عيادة ودون مسار عرض موازٍ لعقود المرحلة الخامسة.
    /// </summary>
    Task<SupervisorAssessmentFileDto> GetAssessmentFileAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
