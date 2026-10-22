using Application.Common.Results;
using Application.Dtos.ReferenceData;

namespace Application.Services.ReferenceData;

public interface IReferenceCatalogHandler
{
    string Catalog { get; }

    Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken);
}
