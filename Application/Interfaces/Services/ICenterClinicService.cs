using Application.Common.Results;
using Application.Dtos.Centers;

namespace Application.Interfaces.Services;

public interface ICenterClinicService
{
    Task<PaginatedList<CenterClinicListItemDto>> GetByCenterAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<CenterClinicDetailsDto> LinkAsync(
        Guid centerId,
        LinkCenterClinicDto dto,
        CancellationToken cancellationToken = default);

    Task<CenterClinicDetailsDto> ActivateAsync(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default);

    Task<CenterClinicDetailsDto> DeactivateAsync(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default);

    /// <summary>يرفض إسناد طبيب إذا كانت علاقة المركز-العيادة غير موجودة أو معطّلة.</summary>
    Task EnsureAcceptsDoctorAssignmentAsync(
        Guid centerId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
}
