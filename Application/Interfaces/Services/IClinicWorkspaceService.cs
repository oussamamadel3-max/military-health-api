using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;

namespace Application.Interfaces.Services;

/// <summary>مساحة عمل طبيب العيادة: العيادات المسندة، بحث المنتسب، وملخص فتح الملف (FR-032 / شريحة فتح FR-045).</summary>
public interface IClinicWorkspaceService
{
    /// <summary>يعيد العيادات المسندة بفعالية للمستخدم الحالي ضمن المركز النشط.</summary>
    Task<IReadOnlyList<DoctorClinicDto>> ListMyClinicsAsync(
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يبحث عن المنتسبين في المركز النشط لعيادة مسندة دون اكتساب قفل تحرير.</summary>
    Task<PaginatedList<ClinicMemberSearchItemDto>> SearchMembersAsync(
        Guid clinicId,
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يفتح ملخص ملف المنتسب في سياق العيادة، ويكتسب/يحدّث قفل التحرير عند التوفر،
    /// ويسجّل حدث تدقيق اطلاع حساس من العيادة.
    /// </summary>
    Task<ClinicMemberSummaryDto> OpenMemberAsync(
        Guid memberId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
