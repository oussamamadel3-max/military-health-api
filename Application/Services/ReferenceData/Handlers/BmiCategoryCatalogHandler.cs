using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class BmiCategoryCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;

    public BmiCategoryCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork)
    {
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public string Catalog => ReferenceCatalogs.BmiCategories;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.BmiCategories().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(q.OrderBy(x => x.SortOrder), pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.BmiCategories()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.CategoryId.ToString(),
                Code = x.Code,
                NameAr = x.NameAr,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public async Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var code = dto.Code.Trim();
        var name = dto.NameAr.Trim();
        var sort = dto.SortOrder ?? 0;
        if (dto.MinBmi is null)
            throw new InvalidReferenceDataException("الحد الأدنى لمؤشر كتلة الجسم مطلوب.");
        if (dto.MinBmi < 0 || (dto.MaxBmi.HasValue && dto.MaxBmi <= dto.MinBmi))
            throw new InvalidReferenceDataException("حدود مؤشر كتلة الجسم غير صالحة.");

        if (await _store.BmiCategories().AnyAsync(x => x.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ReferenceDataDuplicateException($"رمز تصنيف BMI '{code}' مستخدم مسبقاً.");
        if (await _store.BmiCategories().AnyAsync(x => x.NameAr == name, cancellationToken))
            throw new ReferenceDataDuplicateException($"اسم تصنيف BMI '{name}' مستخدم مسبقاً.");
        if (await _store.BmiCategories().AnyAsync(x => x.SortOrder == sort, cancellationToken))
            throw new ReferenceDataDuplicateException($"ترتيب تصنيف BMI '{sort}' مستخدم مسبقاً.");

        var entity = new BmiCategory
        {
            Code = code,
            NameAr = name,
            MinBmi = dto.MinBmi.Value,
            MaxBmi = dto.MaxBmi,
            SortOrder = sort,
            IsActive = dto.IsActive
        };
        _store.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        var name = dto.NameAr.Trim();
        var sort = dto.SortOrder ?? entity.SortOrder;
        var min = dto.MinBmi ?? entity.MinBmi;
        var max = dto.MaxBmi;
        if (min < 0 || (max.HasValue && max <= min))
            throw new InvalidReferenceDataException("حدود مؤشر كتلة الجسم غير صالحة.");
        if (await _store.BmiCategories().AnyAsync(x => x.NameAr == name && x.CategoryId != entity.CategoryId, cancellationToken))
            throw new ReferenceDataDuplicateException($"اسم تصنيف BMI '{name}' مستخدم مسبقاً.");
        if (await _store.BmiCategories().AnyAsync(x => x.SortOrder == sort && x.CategoryId != entity.CategoryId, cancellationToken))
            throw new ReferenceDataDuplicateException($"ترتيب تصنيف BMI '{sort}' مستخدم مسبقاً.");

        entity.NameAr = name;
        entity.MinBmi = min;
        entity.MaxBmi = max;
        entity.SortOrder = sort;
        entity.IsActive = dto.IsActive;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        entity.IsActive = isActive;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private async Task<BmiCategory> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var key = ReferenceDataSupport.ParseShortId(id);
        return await _store.BmiCategories(!tracking).FirstOrDefaultAsync(x => x.CategoryId == key, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(BmiCategory x) => new()
    {
        Id = x.CategoryId.ToString(),
        Catalog = ReferenceCatalogs.BmiCategories,
        Code = x.Code,
        NameAr = x.NameAr,
        MinBmi = x.MinBmi,
        MaxBmi = x.MaxBmi,
        SortOrder = x.SortOrder,
        IsActive = x.IsActive
    };
}
