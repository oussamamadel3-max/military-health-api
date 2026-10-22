using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class ResultStatusCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;

    public ResultStatusCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork)
    {
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public string Catalog => ReferenceCatalogs.ResultStatuses;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.ResultStatuses().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(q.OrderBy(x => x.StatusId), pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.ResultStatuses()
            .Where(x => x.IsActive)
            .OrderBy(x => x.StatusId)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.StatusId.ToString(),
                Code = x.Code,
                NameAr = x.NameAr
            })
            .ToListAsync(cancellationToken);

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
        => throw new ReferenceDataLockedException(
            "حالات النتيجة الأساسية (مقبول/مرفوض/مؤجل) ثابتة ولا يُسمح بإضافة حالات جديدة.");

    public Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken)
        => throw new ReferenceDataLockedException();

    public async Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity.IsLocked)
            throw new ReferenceDataLockedException("لا يجوز تعطيل أو تعديل حالات النتيجة الأساسية المقفلة.");

        entity.IsActive = isActive;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private async Task<ResultStatus> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var key = ReferenceDataSupport.ParseShortId(id);
        return await _store.ResultStatuses(!tracking).FirstOrDefaultAsync(x => x.StatusId == key, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(ResultStatus x) => new()
    {
        Id = x.StatusId.ToString(),
        Catalog = ReferenceCatalogs.ResultStatuses,
        Code = x.Code,
        NameAr = x.NameAr,
        IsActive = x.IsActive,
        IsLocked = x.IsLocked
    };
}
