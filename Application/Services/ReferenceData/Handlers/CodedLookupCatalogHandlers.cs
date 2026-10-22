using Application.Dtos.ReferenceData;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using System.Linq.Expressions;

namespace Application.Services.ReferenceData.Handlers;

public sealed class MaritalStatusCatalogHandler : CodedLookupCatalogHandler<MaritalStatus>
{
    private readonly IReferenceDataRepository _store;

    public MaritalStatusCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork) : base(unitOfWork)
        => _store = store;

    public override string Catalog => ReferenceCatalogs.MaritalStatuses;
    protected override string Label => "الحالة الاجتماعية";
    protected override IQueryable<MaritalStatus> Query(bool asNoTracking = true) => _store.MaritalStatuses(asNoTracking);
    protected override void Add(MaritalStatus entity) => _store.Add(entity);
    protected override short GetId(MaritalStatus entity) => entity.MaritalStatusId;
    protected override bool GetIsSystem(MaritalStatus entity) => entity.IsSystem;
    protected override short GetSortOrder(MaritalStatus entity) => entity.SortOrder;
    protected override void SetCode(MaritalStatus entity, string code) => entity.Code = code;
    protected override void SetName(MaritalStatus entity, string name) => entity.NameAr = name;
    protected override void SetDescription(MaritalStatus entity, string? description) => entity.Description = description;
    protected override void SetSortOrder(MaritalStatus entity, short sort) => entity.SortOrder = sort;
    protected override void SetIsActive(MaritalStatus entity, bool isActive) => entity.IsActive = isActive;
    protected override void TouchUpdatedAt(MaritalStatus entity) => entity.UpdatedAt = DateTimeOffset.UtcNow;
    protected override Expression<Func<MaritalStatus, bool>> IdNotEquals(short id) => x => x.MaritalStatusId != id;
    protected override Expression<Func<MaritalStatus, bool>> BuildIdEquals(short id) => x => x.MaritalStatusId == id;
    protected override Expression<Func<MaritalStatus, bool>> BuildIsActiveEquals(bool isActive) => x => x.IsActive == isActive;
    protected override Expression<Func<MaritalStatus, bool>> BuildSearch(string termLower)
        => x => x.Code.ToLower().Contains(termLower) || x.NameAr.ToLower().Contains(termLower);
    protected override IQueryable<MaritalStatus> ApplyDefaultOrder(IQueryable<MaritalStatus> query)
        => query.OrderBy(x => x.SortOrder).ThenBy(x => x.NameAr);

    protected override MaritalStatus CreateEntity(string code, string name, short sort, bool isActive, string? description)
        => new()
        {
            Code = code,
            NameAr = name,
            Description = description,
            SortOrder = sort,
            IsSystem = false,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    protected override ReferenceDataItemDto Map(MaritalStatus x) => new()
    {
        Id = x.MaritalStatusId.ToString(),
        Catalog = Catalog,
        Code = x.Code,
        NameAr = x.NameAr,
        Description = x.Description,
        SortOrder = x.SortOrder,
        IsSystem = x.IsSystem,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}

public sealed class BloodTypeCatalogHandler : CodedLookupCatalogHandler<BloodType>
{
    private readonly IReferenceDataRepository _store;

    public BloodTypeCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork) : base(unitOfWork)
        => _store = store;

    public override string Catalog => ReferenceCatalogs.BloodTypes;
    protected override string Label => "زمرة الدم";
    protected override IQueryable<BloodType> Query(bool asNoTracking = true) => _store.BloodTypes(asNoTracking);
    protected override void Add(BloodType entity) => _store.Add(entity);
    protected override short GetId(BloodType entity) => entity.BloodTypeId;
    protected override bool GetIsSystem(BloodType entity) => entity.IsSystem;
    protected override short GetSortOrder(BloodType entity) => entity.SortOrder;
    protected override void SetCode(BloodType entity, string code) => entity.Code = code;
    protected override void SetName(BloodType entity, string name) => entity.NameAr = name;
    protected override void SetDescription(BloodType entity, string? description) => entity.Description = description;
    protected override void SetSortOrder(BloodType entity, short sort) => entity.SortOrder = sort;
    protected override void SetIsActive(BloodType entity, bool isActive) => entity.IsActive = isActive;
    protected override void TouchUpdatedAt(BloodType entity) => entity.UpdatedAt = DateTimeOffset.UtcNow;
    protected override Expression<Func<BloodType, bool>> IdNotEquals(short id) => x => x.BloodTypeId != id;
    protected override Expression<Func<BloodType, bool>> BuildIdEquals(short id) => x => x.BloodTypeId == id;
    protected override Expression<Func<BloodType, bool>> BuildIsActiveEquals(bool isActive) => x => x.IsActive == isActive;
    protected override Expression<Func<BloodType, bool>> BuildSearch(string termLower)
        => x => x.Code.ToLower().Contains(termLower) || x.NameAr.ToLower().Contains(termLower);
    protected override IQueryable<BloodType> ApplyDefaultOrder(IQueryable<BloodType> query)
        => query.OrderBy(x => x.SortOrder).ThenBy(x => x.NameAr);

    protected override BloodType CreateEntity(string code, string name, short sort, bool isActive, string? description)
        => new()
        {
            Code = code,
            NameAr = name,
            Description = description,
            SortOrder = sort,
            IsSystem = false,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    protected override ReferenceDataItemDto Map(BloodType x) => new()
    {
        Id = x.BloodTypeId.ToString(),
        Catalog = Catalog,
        Code = x.Code,
        NameAr = x.NameAr,
        Description = x.Description,
        SortOrder = x.SortOrder,
        IsSystem = x.IsSystem,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}

public sealed class ConsultationTypeCatalogHandler : CodedLookupCatalogHandler<ConsultationType>
{
    private readonly IReferenceDataRepository _store;

    public ConsultationTypeCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork) : base(unitOfWork)
        => _store = store;

    public override string Catalog => ReferenceCatalogs.ConsultationTypes;
    protected override string Label => "نوع الاستشارة";
    protected override IQueryable<ConsultationType> Query(bool asNoTracking = true) => _store.ConsultationTypes(asNoTracking);
    protected override void Add(ConsultationType entity) => _store.Add(entity);
    protected override short GetId(ConsultationType entity) => entity.ConsultationTypeId;
    protected override bool GetIsSystem(ConsultationType entity) => entity.IsSystem;
    protected override short GetSortOrder(ConsultationType entity) => entity.SortOrder;
    protected override void SetCode(ConsultationType entity, string code) => entity.Code = code;
    protected override void SetName(ConsultationType entity, string name) => entity.NameAr = name;
    protected override void SetDescription(ConsultationType entity, string? description) => entity.Description = description;
    protected override void SetSortOrder(ConsultationType entity, short sort) => entity.SortOrder = sort;
    protected override void SetIsActive(ConsultationType entity, bool isActive) => entity.IsActive = isActive;
    protected override void TouchUpdatedAt(ConsultationType entity) => entity.UpdatedAt = DateTimeOffset.UtcNow;
    protected override Expression<Func<ConsultationType, bool>> IdNotEquals(short id) => x => x.ConsultationTypeId != id;
    protected override Expression<Func<ConsultationType, bool>> BuildIdEquals(short id) => x => x.ConsultationTypeId == id;
    protected override Expression<Func<ConsultationType, bool>> BuildIsActiveEquals(bool isActive) => x => x.IsActive == isActive;
    protected override Expression<Func<ConsultationType, bool>> BuildSearch(string termLower)
        => x => x.Code.ToLower().Contains(termLower) || x.NameAr.ToLower().Contains(termLower);
    protected override IQueryable<ConsultationType> ApplyDefaultOrder(IQueryable<ConsultationType> query)
        => query.OrderBy(x => x.SortOrder).ThenBy(x => x.NameAr);

    protected override ConsultationType CreateEntity(string code, string name, short sort, bool isActive, string? description)
        => new()
        {
            Code = code,
            NameAr = name,
            Description = description,
            SortOrder = sort,
            IsSystem = false,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    protected override ReferenceDataItemDto Map(ConsultationType x) => new()
    {
        Id = x.ConsultationTypeId.ToString(),
        Catalog = Catalog,
        Code = x.Code,
        NameAr = x.NameAr,
        Description = x.Description,
        SortOrder = x.SortOrder,
        IsSystem = x.IsSystem,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}

public sealed class InvestigationTypeCatalogHandler : CodedLookupCatalogHandler<InvestigationType>
{
    private readonly IReferenceDataRepository _store;

    public InvestigationTypeCatalogHandler(IReferenceDataRepository store, IUnitOfWork unitOfWork) : base(unitOfWork)
        => _store = store;

    public override string Catalog => ReferenceCatalogs.InvestigationTypes;
    protected override string Label => "نوع الاستقصاء";
    protected override IQueryable<InvestigationType> Query(bool asNoTracking = true) => _store.InvestigationTypes(asNoTracking);
    protected override void Add(InvestigationType entity) => _store.Add(entity);
    protected override short GetId(InvestigationType entity) => entity.InvestigationTypeId;
    protected override bool GetIsSystem(InvestigationType entity) => entity.IsSystem;
    protected override short GetSortOrder(InvestigationType entity) => entity.SortOrder;
    protected override void SetCode(InvestigationType entity, string code) => entity.Code = code;
    protected override void SetName(InvestigationType entity, string name) => entity.NameAr = name;
    protected override void SetDescription(InvestigationType entity, string? description) => entity.Description = description;
    protected override void SetSortOrder(InvestigationType entity, short sort) => entity.SortOrder = sort;
    protected override void SetIsActive(InvestigationType entity, bool isActive) => entity.IsActive = isActive;
    protected override void TouchUpdatedAt(InvestigationType entity) => entity.UpdatedAt = DateTimeOffset.UtcNow;
    protected override Expression<Func<InvestigationType, bool>> IdNotEquals(short id) => x => x.InvestigationTypeId != id;
    protected override Expression<Func<InvestigationType, bool>> BuildIdEquals(short id) => x => x.InvestigationTypeId == id;
    protected override Expression<Func<InvestigationType, bool>> BuildIsActiveEquals(bool isActive) => x => x.IsActive == isActive;
    protected override Expression<Func<InvestigationType, bool>> BuildSearch(string termLower)
        => x => x.Code.ToLower().Contains(termLower) || x.NameAr.ToLower().Contains(termLower);
    protected override IQueryable<InvestigationType> ApplyDefaultOrder(IQueryable<InvestigationType> query)
        => query.OrderBy(x => x.SortOrder).ThenBy(x => x.NameAr);

    protected override InvestigationType CreateEntity(string code, string name, short sort, bool isActive, string? description)
        => new()
        {
            Code = code,
            NameAr = name,
            Description = description,
            SortOrder = sort,
            IsSystem = false,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    protected override ReferenceDataItemDto Map(InvestigationType x) => new()
    {
        Id = x.InvestigationTypeId.ToString(),
        Catalog = Catalog,
        Code = x.Code,
        NameAr = x.NameAr,
        Description = x.Description,
        SortOrder = x.SortOrder,
        IsSystem = x.IsSystem,
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
