using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class RecruitmentCenterCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;

    public RecruitmentCenterCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork)
    {
        _store = store;
        _unitOfWork = unitOfWork;
    }

    public string Catalog => ReferenceCatalogs.RecruitmentCenters;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.RecruitmentCenters().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(q.OrderBy(x => x.Code), pageNumber, pageSize, Map, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.RecruitmentCenters()
            .Where(x => x.IsActive)
            .OrderBy(x => x.NameAr)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.RecruitmentCenterId.ToString(),
                Code = x.Code,
                NameAr = x.NameAr
            })
            .ToListAsync(cancellationToken);

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public async Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var code = dto.Code.Trim();
        if (await _store.RecruitmentCenters().AnyAsync(x => x.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ReferenceDataDuplicateException($"رمز مركز التجنيد '{code}' مستخدم مسبقاً.");

        var now = DateTimeOffset.UtcNow;
        var entity = new RecruitmentCenter
        {
            RecruitmentCenterId = Guid.NewGuid(),
            Code = code,
            NameAr = dto.NameAr.Trim(),
            NameEn = ReferenceDataSupport.TrimOrNull(dto.NameEn),
            IsActive = dto.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        _store.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(string id, ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        entity.NameAr = dto.NameAr.Trim();
        entity.NameEn = ReferenceDataSupport.TrimOrNull(dto.NameEn);
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private async Task<RecruitmentCenter> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var guid = ReferenceDataSupport.ParseGuidId(id);
        return await _store.RecruitmentCenters(!tracking).FirstOrDefaultAsync(x => x.RecruitmentCenterId == guid, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(RecruitmentCenter x) => new()
    {
        Id = x.RecruitmentCenterId.ToString(),
        Catalog = ReferenceCatalogs.RecruitmentCenters,
        Code = x.Code,
        NameAr = x.NameAr,
        NameEn = x.NameEn,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
