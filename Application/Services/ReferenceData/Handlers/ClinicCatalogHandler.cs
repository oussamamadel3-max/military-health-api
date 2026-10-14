using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class ClinicCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;

    public ClinicCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork)
    {
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public string Catalog => ReferenceCatalogs.Clinics;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.Clinics().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(q.OrderBy(x => x.SortOrder), pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.Clinics()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.ClinicId.ToString(),
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
        var sort = dto.SortOrder ?? 0;
        if (await _store.Clinics().AnyAsync(x => x.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ReferenceDataDuplicateException($"رمز العيادة '{code}' مستخدم مسبقاً.");
        if (await _store.Clinics().AnyAsync(x => x.SortOrder == sort, cancellationToken))
            throw new ReferenceDataDuplicateException($"ترتيب العيادة '{sort}' مستخدم مسبقاً.");

        var entity = new Clinic
        {
            ClinicId = Guid.NewGuid(),
            Code = code,
            NameAr = dto.NameAr.Trim(),
            SortOrder = sort,
            CountsForFinal = dto.CountsForFinal ?? true,
            IsActive = dto.IsActive
        };
        _store.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        var sort = dto.SortOrder ?? entity.SortOrder;
        if (await _store.Clinics().AnyAsync(x => x.SortOrder == sort && x.ClinicId != entity.ClinicId, cancellationToken))
            throw new ReferenceDataDuplicateException($"ترتيب العيادة '{sort}' مستخدم مسبقاً.");

        entity.NameAr = dto.NameAr.Trim();
        entity.SortOrder = sort;
        entity.CountsForFinal = dto.CountsForFinal ?? entity.CountsForFinal;
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

    private async Task<Clinic> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var guid = ReferenceDataSupport.ParseGuidId(id);
        return await _store.Clinics(!tracking).FirstOrDefaultAsync(x => x.ClinicId == guid, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(Clinic x) => new()
    {
        Id = x.ClinicId.ToString(),
        Catalog = ReferenceCatalogs.Clinics,
        Code = x.Code,
        NameAr = x.NameAr,
        SortOrder = x.SortOrder,
        CountsForFinal = x.CountsForFinal,
        IsActive = x.IsActive
    };
}
