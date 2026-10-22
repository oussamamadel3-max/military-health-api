using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Application.Services.ReferenceData.Handlers;

/// <summary>
/// معالج مشترك للقوائم ذات الرمز/الاسم/الترتيب/IsSystem (حالة اجتماعية، زمر دم، استشارات، استقصاءات).
/// </summary>
public abstract class CodedLookupCatalogHandler<TEntity> : IReferenceCatalogHandler
    where TEntity : class
{
    private readonly IUnitOfWork _unitOfWork;

    protected CodedLookupCatalogHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public abstract string Catalog { get; }
    protected abstract string Label { get; }

    protected abstract IQueryable<TEntity> Query(bool asNoTracking = true);
    protected abstract void Add(TEntity entity);
    protected abstract TEntity CreateEntity(string code, string name, short sort, bool isActive, string? description);
    protected abstract short GetId(TEntity entity);
    protected abstract bool GetIsSystem(TEntity entity);
    protected abstract void SetCode(TEntity entity, string code);
    protected abstract void SetName(TEntity entity, string name);
    protected abstract void SetDescription(TEntity entity, string? description);
    protected abstract void SetSortOrder(TEntity entity, short sort);
    protected abstract void SetIsActive(TEntity entity, bool isActive);
    protected abstract void TouchUpdatedAt(TEntity entity);
    protected abstract short GetSortOrder(TEntity entity);
    protected abstract ReferenceDataItemDto Map(TEntity entity);
    protected abstract Expression<Func<TEntity, bool>> IdNotEquals(short id);

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = Query();
        if (isActive.HasValue)
            q = q.Where(BuildIsActiveEquals(isActive.Value));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(BuildSearch(t));
        }

        q = ApplyDefaultOrder(q);
        return await ReferenceDataSupport.ToPageAsync(q, pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
    {
        var q = ApplyDefaultOrder(Query().Where(BuildIsActiveEquals(true)));
        var rows = await q.ToListAsync(cancellationToken);
        return rows.Select(x =>
        {
            var mapped = Map(x);
            return new ReferenceDataOptionDto
            {
                Id = mapped.Id,
                Code = mapped.Code,
                NameAr = mapped.NameAr,
                SortOrder = mapped.SortOrder
            };
        }).ToList();
    }

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public async Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var code = dto.Code.Trim();
        var name = dto.NameAr.Trim();
        var sort = dto.SortOrder ?? 0;
        var q = Query();
        if (await q.AnyAsync(ReferenceDataSupport.CodeEquals<TEntity>(code), cancellationToken))
            throw new ReferenceDataDuplicateException($"رمز {Label} '{code}' مستخدم مسبقاً.");
        if (await q.AnyAsync(ReferenceDataSupport.NameEquals<TEntity>(name), cancellationToken))
            throw new ReferenceDataDuplicateException($"اسم {Label} '{name}' مستخدم مسبقاً.");

        var entity = CreateEntity(code, name, sort, dto.IsActive, ReferenceDataSupport.TrimOrNull(dto.Description));
        Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        var name = dto.NameAr.Trim();
        if (await Query().Where(IdNotEquals(GetId(entity))).AnyAsync(ReferenceDataSupport.NameEquals<TEntity>(name), cancellationToken))
            throw new ReferenceDataDuplicateException($"الاسم '{name}' مستخدم مسبقاً.");

        if (!GetIsSystem(entity) && !string.Equals(Map(entity).Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var code = dto.Code.Trim();
            if (await Query().Where(IdNotEquals(GetId(entity))).AnyAsync(ReferenceDataSupport.CodeEquals<TEntity>(code), cancellationToken))
                throw new ReferenceDataDuplicateException($"رمز {Label} '{code}' مستخدم مسبقاً.");
            SetCode(entity, code);
        }

        SetName(entity, name);
        SetDescription(entity, ReferenceDataSupport.TrimOrNull(dto.Description));
        SetSortOrder(entity, dto.SortOrder ?? GetSortOrder(entity));
        SetIsActive(entity, dto.IsActive);
        TouchUpdatedAt(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        SetIsActive(entity, isActive);
        TouchUpdatedAt(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private async Task<TEntity> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var key = ReferenceDataSupport.ParseShortId(id);
        return await Query(!tracking).FirstOrDefaultAsync(BuildIdEquals(key), ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    protected abstract Expression<Func<TEntity, bool>> BuildIdEquals(short id);
    protected abstract Expression<Func<TEntity, bool>> BuildIsActiveEquals(bool isActive);
    protected abstract Expression<Func<TEntity, bool>> BuildSearch(string termLower);
    protected abstract IQueryable<TEntity> ApplyDefaultOrder(IQueryable<TEntity> query);
}
