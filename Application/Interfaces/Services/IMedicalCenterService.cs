using Application.Common.Results;
using Application.Dtos.Centers;

namespace Application.Interfaces.Services;

public interface IMedicalCenterService
{
    Task<PaginatedList<MedicalCenterListItemDto>> GetCentersAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<MedicalCenterDetailsDto> GetByIdAsync(Guid centerId, CancellationToken cancellationToken = default);

    Task<MedicalCenterDetailsDto> CreateAsync(CreateMedicalCenterDto dto, CancellationToken cancellationToken = default);

    Task<MedicalCenterDetailsDto> UpdateAsync(Guid centerId, UpdateMedicalCenterDto dto, CancellationToken cancellationToken = default);

    Task<MedicalCenterDetailsDto> ActivateAsync(Guid centerId, CancellationToken cancellationToken = default);

    Task<MedicalCenterDetailsDto> DeactivateAsync(Guid centerId, DeactivateMedicalCenterDto dto, CancellationToken cancellationToken = default);

    /// <summary>يرفض إنشاء إسناد/معاملة جديدة إذا كان المركز غير موجود أو معطّلاً.</summary>
    Task EnsureAcceptsNewRelationsAsync(Guid centerId, CancellationToken cancellationToken = default);
}
