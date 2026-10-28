using Application.Common.Results;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;

namespace Application.Interfaces.Services;

/// <summary>
/// مساحة عمل طبيب العيادة: العيادات المسندة، بحث/فتح الملف، الملاحظات العامة،
/// ومتابعة مؤجلات العيادة (FR-032 / FR-042 / FR-043 / FR-044 / شريحة فتح FR-045).
/// </summary>
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

    /// <summary>
    /// يُدخل أو يحدّث أو يُفرغ الملاحظة العامة للمنتسب ضمن المركز والعيادة المسندة،
    /// ويحفظ هوية الكاتب ويُفعّل أو يوقف التنبيه حسب فراغ النص (FR-043 / FR-044).
    /// </summary>
    Task<MemberGeneralNoteSavedDto> UpsertGeneralNoteAsync(
        Guid memberId,
        UpsertMemberGeneralNoteRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعيد قائمة الحالات المؤجلة غير المغلقة في العيادة ضمن المركز النشط مع التصفية والترقيم (FR-042).
    /// </summary>
    Task<PaginatedList<DeferredActionListItemDto>> ListDeferredActionsAsync(
        DeferredActionListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يعلّم الإجراء المطلوب مكتملاً مع حفظ المنفّذ والوقت دون تغيير تقييم العيادة (FR-042).
    /// </summary>
    Task<DeferredActionCompletedDto> CompleteDeferredActionAsync(
        Guid deferredActionId,
        Guid clinicId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
