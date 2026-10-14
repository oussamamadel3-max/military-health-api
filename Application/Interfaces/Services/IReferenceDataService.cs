using Application.Common.Results;
using Application.Dtos.ReferenceData;

namespace Application.Interfaces.Services;

public interface IReferenceDataService
{
    IReadOnlyList<string> GetCatalogs();

    Task<PaginatedList<ReferenceDataItemDto>> GetAdminListAsync(
        string catalog,
        int pageNumber,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    /// <summary>قيم فعّالة فقط لنماذج الإدخال التشغيلية.</summary>
    Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(
        string catalog,
        CancellationToken cancellationToken = default);

    Task<ReferenceDataItemDto> GetByIdAsync(
        string catalog,
        string id,
        CancellationToken cancellationToken = default);

    Task<ReferenceDataItemDto> CreateAsync(
        string catalog,
        ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default);

    Task<ReferenceDataItemDto> UpdateAsync(
        string catalog,
        string id,
        ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default);

    Task<ReferenceDataItemDto> SetActiveAsync(
        string catalog,
        string id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
