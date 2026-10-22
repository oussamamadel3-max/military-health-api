using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IUserClinicAssignmentService
{
    Task<PaginatedList<CenterClinicForAssignmentDto>> GetCenterClinicsAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<PaginatedList<CenterDoctorDto>> GetCenterDoctorsAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PaginatedList<UserClinicAssignmentDto>> GetClinicDoctorsAsync(
        Guid centerId,
        Guid clinicId,
        int pageNumber,
        int pageSize,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<List<UserClinicAssignmentDto>> GetUserClinicAssignmentsAsync(
        string userId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<UserClinicAssignmentDto> AssignAsync(
        AssignUserClinicDto dto,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        RevokeUserClinicDto dto,
        CancellationToken cancellationToken = default);
}
