using Application.Common.Results;
using Application.Dtos.DocumentDtos;
using Application.Interfaces;
using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IDocumentRepository : IRepository<Document, long>
{
    Task<long> GetMaxDocumentNumberAsync(CancellationToken ct = default);
    Task<string?> GetTitleAsync(long documentId, CancellationToken ct = default);
    Task<PaginatedList<Document>> SearchAsync(DocumentSearchCriteria criteria, ActorScope scope, int page, int size, CancellationToken ct = default);
    Task<PaginatedList<Document>> GetPagedAsync(ActorScope scope, int page, int size, string? title, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> GetDropdownAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListAsync(ActorScope scope, DocumentSearchCriteria criteria, CancellationToken ct = default);
    Task<long> CountScopedAsync(ActorScope scope, DateTime? createdFrom, DateTime? createdToExclusive, CancellationToken ct = default);
    Task<Dictionary<long, string>> GetTitlesScopedAsync(ActorScope scope, IEnumerable<long> documentIds, CancellationToken ct = default);
    Task<IReadOnlyList<(int Year, int Month, long Count)>> GetGrowthByMonthScopedAsync(ActorScope scope, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
