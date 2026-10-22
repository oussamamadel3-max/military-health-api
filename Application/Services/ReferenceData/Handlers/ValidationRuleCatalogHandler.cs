using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.ReferenceData.Handlers;

public sealed class ValidationRuleCatalogHandler : IReferenceCatalogHandler
{
    private readonly IReferenceDataRepository _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public ValidationRuleCatalogHandler(
        IReferenceDataRepository store,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _store = store;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public string Catalog => ReferenceCatalogs.ValidationRules;

    public async Task<PaginatedList<ReferenceDataItemDto>> ListAdminAsync(
        int pageNumber, int pageSize, string? search, bool? isActive, CancellationToken cancellationToken)
    {
        var q = _store.ValidationRules().AsQueryable();
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(x => x.Code.ToLower().Contains(t) || x.NameAr.ToLower().Contains(t) || x.TargetField.ToLower().Contains(t));
        }

        return await ReferenceDataSupport.ToPageAsync(
            q.OrderBy(x => x.Code).ThenByDescending(x => x.VersionNo),
            pageNumber,
            pageSize,
            Map,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(CancellationToken cancellationToken)
        => await _store.ValidationRules()
            .Where(x => x.IsActive && (x.ValidTo == null || x.ValidTo > DateTimeOffset.UtcNow))
            .OrderBy(x => x.Code).ThenByDescending(x => x.VersionNo)
            .Select(x => new ReferenceDataOptionDto
            {
                Id = x.ValidationRuleId.ToString(),
                Code = x.Code,
                NameAr = x.NameAr
            })
            .ToListAsync(cancellationToken);

    public async Task<ReferenceDataItemDto> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Map(await FindAsync(id, tracking: false, cancellationToken));

    public async Task<ReferenceDataItemDto> CreateAsync(ReferenceDataWriteDto dto, CancellationToken cancellationToken)
    {
        var code = dto.Code.Trim();
        if (string.IsNullOrWhiteSpace(dto.TargetEntity) || string.IsNullOrWhiteSpace(dto.TargetField))
            throw new InvalidReferenceDataException("الكيان والحقل المستهدفان مطلوبان لقاعدة التحقق.");
        if (dto.MinValue is null && dto.MaxValue is null)
            throw new InvalidReferenceDataException("يجب تحديد حد أدنى أو أعلى على الأقل لقاعدة التحقق.");
        if (dto.MinValue.HasValue && dto.MaxValue.HasValue && dto.MaxValue < dto.MinValue)
            throw new InvalidReferenceDataException("الحد الأعلى يجب أن يكون أكبر من أو يساوي الحد الأدنى.");

        var version = dto.VersionNo is > 0 ? dto.VersionNo.Value : 1;
        var validFrom = dto.ValidFrom ?? DateTimeOffset.UtcNow;
        if (dto.ValidTo.HasValue && dto.ValidTo <= validFrom)
            throw new InvalidReferenceDataException("تاريخ انتهاء الصلاحية يجب أن يكون بعد تاريخ البداية.");

        if (await _store.ValidationRules().AnyAsync(x => x.Code.ToLower() == code.ToLower() && x.VersionNo == version, cancellationToken))
            throw new ReferenceDataDuplicateException($"قاعدة التحقق '{code}' بالإصدار {version} موجودة مسبقاً.");

        var now = DateTimeOffset.UtcNow;
        var entity = new ValidationRule
        {
            ValidationRuleId = Guid.NewGuid(),
            Code = code,
            NameAr = dto.NameAr.Trim(),
            TargetEntity = dto.TargetEntity.Trim(),
            TargetField = dto.TargetField.Trim(),
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            Unit = ReferenceDataSupport.TrimOrNull(dto.Unit),
            VersionNo = version,
            ValidFrom = validFrom,
            ValidTo = dto.ValidTo,
            IsActive = dto.IsActive,
            UpdatedByUserId = ActorId(),
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
        if (dto.MinValue is null && dto.MaxValue is null)
            throw new InvalidReferenceDataException("يجب تحديد حد أدنى أو أعلى على الأقل لقاعدة التحقق.");
        if (dto.MinValue.HasValue && dto.MaxValue.HasValue && dto.MaxValue < dto.MinValue)
            throw new InvalidReferenceDataException("الحد الأعلى يجب أن يكون أكبر من أو يساوي الحد الأدنى.");

        var validFrom = dto.ValidFrom ?? entity.ValidFrom;
        var validTo = dto.ValidTo;
        if (validTo.HasValue && validTo <= validFrom)
            throw new InvalidReferenceDataException("تاريخ انتهاء الصلاحية يجب أن يكون بعد تاريخ البداية.");

        entity.NameAr = dto.NameAr.Trim();
        if (!string.IsNullOrWhiteSpace(dto.TargetEntity))
            entity.TargetEntity = dto.TargetEntity.Trim();
        if (!string.IsNullOrWhiteSpace(dto.TargetField))
            entity.TargetField = dto.TargetField.Trim();
        entity.MinValue = dto.MinValue;
        entity.MaxValue = dto.MaxValue;
        entity.Unit = ReferenceDataSupport.TrimOrNull(dto.Unit);
        entity.ValidFrom = validFrom;
        entity.ValidTo = validTo;
        entity.IsActive = dto.IsActive;
        entity.UpdatedByUserId = ActorId() ?? entity.UpdatedByUserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ReferenceDataItemDto> SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, tracking: true, cancellationToken);
        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedByUserId = ActorId() ?? entity.UpdatedByUserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    private string? ActorId()
        => string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0"
            ? null
            : _currentUser.UserId;

    private async Task<ValidationRule> FindAsync(string id, bool tracking, CancellationToken ct)
    {
        var guid = ReferenceDataSupport.ParseGuidId(id);
        return await _store.ValidationRules(!tracking).FirstOrDefaultAsync(x => x.ValidationRuleId == guid, ct)
            ?? throw new ReferenceDataNotFoundException();
    }

    private static ReferenceDataItemDto Map(ValidationRule x) => new()
    {
        Id = x.ValidationRuleId.ToString(),
        Catalog = ReferenceCatalogs.ValidationRules,
        Code = x.Code,
        NameAr = x.NameAr,
        TargetEntity = x.TargetEntity,
        TargetField = x.TargetField,
        MinValue = x.MinValue,
        MaxValue = x.MaxValue,
        Unit = x.Unit,
        VersionNo = x.VersionNo,
        ValidFrom = x.ValidFrom,
        ValidTo = x.ValidTo,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
