using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class RequiredActionTypeCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;

    public RequiredActionTypeCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork)
    {
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public string Catalog => ReferenceCatalogs.RequiredActionTypes;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.RequiredActionTypes().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(q.OrderBy(x => x.ActionTypeId), pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.RequiredActionTypes()
            .Where(x => x.IsActive)
            .OrderBy(x => x.ActionTypeId)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.ActionTypeId.ToString(),
                Code = x.Code,
                NameAr = x.NameAr
            })
            .ToListAsync(cancellationToken);

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public async Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var code = dto.Code.Trim();
        var name = dto.NameAr.Trim();
        if (await _store.RequiredActionTypes().AnyAsync(x => x.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ReferenceDataDuplicateException($"رمز الإجراء '{code}' مستخدم مسبقاً.");
        if (await _store.RequiredActionTypes().AnyAsync(x => x.NameAr == name, cancellationToken))
            throw new ReferenceDataDuplicateException($"اسم الإجراء '{name}' مستخدم مسبقاً.");

        var entity = new RequiredActionType
        {
            Code = code,
            NameAr = name,
            RequiresDescription = dto.RequiresDescription ?? false,
            RequiresTargetClinic = dto.RequiresTargetClinic ?? false,
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
        if (await _store.RequiredActionTypes().AnyAsync(x => x.NameAr == name && x.ActionTypeId != entity.ActionTypeId, cancellationToken))
            throw new ReferenceDataDuplicateException($"اسم الإجراء '{name}' مستخدم مسبقاً.");

        entity.NameAr = name;
        entity.RequiresDescription = dto.RequiresDescription ?? entity.RequiresDescription;
        entity.RequiresTargetClinic = dto.RequiresTargetClinic ?? entity.RequiresTargetClinic;
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

    private async Task<RequiredActionType> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var key = ReferenceDataSupport.ParseShortId(id);
        return await _store.RequiredActionTypes(!tracking).FirstOrDefaultAsync(x => x.ActionTypeId == key, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(RequiredActionType x) => new()
    {
        Id = x.ActionTypeId.ToString(),
        Catalog = ReferenceCatalogs.RequiredActionTypes,
        Code = x.Code,
        NameAr = x.NameAr,
        RequiresDescription = x.RequiresDescription,
        RequiresTargetClinic = x.RequiresTargetClinic,
        IsActive = x.IsActive
    };
}
