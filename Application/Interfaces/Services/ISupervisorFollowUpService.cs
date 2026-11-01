using Application.Common.Results;
using Application.Dtos.Supervisor;

namespace Application.Interfaces.Services;

/// <summary>
/// قائمة متابعة المشرف مع البحث والتصفية والترقيم ضمن المركز النشط (FR-057).
/// </summary>
public interface ISupervisorFollowUpService
{
    /// <summary>
    /// يعيد صفحة من ملفات المركز النشط مع حالات الاكتمال والقرار والمؤجلات،
    /// دون تفاصيل سريرية تخصصية أو مفاتيح تخزين.
    /// </summary>
    Task<PaginatedList<SupervisorFollowUpItemDto>> ListAsync(
        SupervisorFollowUpQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
