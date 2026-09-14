using Application.Common.Results;
using Application.Common.Utilities;
using Application.Dtos.DocumentDtos;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;

public class DocumentRepository : RepositoryBase<Document, long>, IDocumentRepository
{
    public DocumentRepository(AppDbContext context) : base(context) { }

    protected override DbSet<Document> DbSet => Context.Documents;
    protected override Expression<Func<Document, bool>> IdPredicate(long id) => d => d.DocumentId == id;

    public async Task<long> GetMaxDocumentNumberAsync(CancellationToken ct = default)
        => await Context.Documents.Select(d => (long?)d.DocumentNumber).MaxAsync(ct) ?? 0;

    public async Task<string?> GetTitleAsync(long documentId, CancellationToken ct = default)
        => await Context.Documents.AsNoTracking()
            .Where(d => d.DocumentId == documentId)
            .Select(d => d.Title)
            .FirstOrDefaultAsync(ct);

    public Task<PaginatedList<Document>> SearchAsync(DocumentSearchCriteria criteria, ActorScope scope, int page, int size, CancellationToken ct = default)
    {
        var query = ApplySorting(ApplyScope(ApplySearchFilters(Context.Documents.AsNoTracking(), criteria), scope), criteria);
        return GetPagedInternalAsync(query, page, size, ct);
    }

    public Task<PaginatedList<Document>> GetPagedAsync(ActorScope scope, int page, int size, string? title, CancellationToken ct = default)
    {
        var query = ApplyScope(Context.Documents.AsNoTracking(), scope);
        if (!string.IsNullOrWhiteSpace(title))
        {
            var t = SqlLikePatternEscape.EscapeForSqlServerLike(title.Trim());
            query = query.Where(d => d.Title.Contains(t));
        }

        query = query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DocumentId);
        return GetPagedInternalAsync(query, page, size, ct);
    }

    public async Task<IReadOnlyList<Document>> GetDropdownAsync(CancellationToken ct = default)
        => await Context.Documents.AsNoTracking()
            .OrderBy(d => d.DocumentId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Document>> ListAsync(ActorScope scope, DocumentSearchCriteria criteria, CancellationToken ct = default)
        => await ApplySorting(ApplyScope(ApplySearchFilters(Context.Documents.AsNoTracking(), criteria), scope), criteria)
            .Include(d => d.User)
            .ToListAsync(ct);

    public async Task<long> CountScopedAsync(ActorScope scope, DateTime? createdFrom, DateTime? createdToExclusive, CancellationToken ct = default)
    {
        var query = ApplyScope(Context.Documents.AsNoTracking(), scope);
        if (createdFrom.HasValue)
            query = query.Where(d => d.CreatedAt >= createdFrom.Value);
        if (createdToExclusive.HasValue)
            query = query.Where(d => d.CreatedAt < createdToExclusive.Value);
        return await query.LongCountAsync(ct);
    }

    public async Task<Dictionary<long, string>> GetTitlesScopedAsync(ActorScope scope, IEnumerable<long> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.Distinct().ToList();
        return await ApplyScope(Context.Documents.AsNoTracking(), scope)
            .Where(d => ids.Contains(d.DocumentId))
            .ToDictionaryAsync(d => d.DocumentId, d => d.Title, ct);
    }

    public async Task<IReadOnlyList<(int Year, int Month, long Count)>> GetGrowthByMonthScopedAsync(
        ActorScope scope, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        return await ApplyScope(Context.Documents.AsNoTracking(), scope)
            .Where(d => d.CreatedAt >= fromUtc && d.CreatedAt < toUtc)
            .GroupBy(d => new { d.CreatedAt.Year, d.CreatedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ValueTuple<int, int, long>(g.Key.Year, g.Key.Month, g.LongCount()))
            .ToListAsync(ct);
    }

    private static IQueryable<Document> ApplyScope(IQueryable<Document> query, ActorScope scope)
    {
        if (scope.IsSuperAdmin || scope.IsBranchManager) return query;
        return query.Where(d => d.UserId == scope.UserId);
    }

    private static IQueryable<Document> ApplySearchFilters(IQueryable<Document> query, DocumentSearchCriteria c)
    {
        if (!string.IsNullOrWhiteSpace(c.Title))
        {
            var t = SqlLikePatternEscape.EscapeForSqlServerLike(c.Title.Trim());
            query = query.Where(d => d.Title.Contains(t));
        }

        if (!string.IsNullOrWhiteSpace(c.Description))
        {
            var dsc = SqlLikePatternEscape.EscapeForSqlServerLike(c.Description.Trim());
            query = query.Where(d => d.Description.Contains(dsc));
        }

        if (c.CreatedFrom.HasValue)
            query = query.Where(d => d.CreatedAt >= c.CreatedFrom.Value);

        if (c.CreatedTo.HasValue)
        {
            var toExclusive = c.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(d => d.CreatedAt < toExclusive);
        }

        return query;
    }

    private static IQueryable<Document> ApplySorting(IQueryable<Document> query, DocumentSearchCriteria c)
    {
        var sortBy = (c.SortBy ?? "createdat").Trim().ToLowerInvariant();
        var desc = c.SortDescending;

        return sortBy switch
        {
            "title" => desc
                ? query.OrderByDescending(d => d.Title).ThenByDescending(d => d.CreatedAt)
                : query.OrderBy(d => d.Title).ThenByDescending(d => d.CreatedAt),
            "documentnumber" => desc
                ? query.OrderByDescending(d => d.DocumentNumber).ThenByDescending(d => d.CreatedAt)
                : query.OrderBy(d => d.DocumentNumber).ThenByDescending(d => d.CreatedAt),
            "createdat" => desc
                ? query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DocumentId)
                : query.OrderBy(d => d.CreatedAt).ThenByDescending(d => d.DocumentId),
            _ => query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DocumentId)
        };
    }

    private static async Task<PaginatedList<Document>> GetPagedInternalAsync(IQueryable<Document> query, int page, int size, CancellationToken ct)
    {
        var count = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PaginatedList<Document>(items, count, page, size);
    }
}
