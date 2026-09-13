using Application.Common;
using Application.Common.Results;
using Application.Dtos.CoreDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using AutoMapper;
using Domain.Entities.Core;

namespace Application.Services.Core;

public class OrgUnitService : IOrgUnitService
{
    private readonly IOrgUnitRepository _units;
    private readonly IOrgUnitTypeRepository _types;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public OrgUnitService(IOrgUnitRepository units, IOrgUnitTypeRepository types, IUnitOfWork unitOfWork, IMapper mapper) =>
        (_units, _types, _unitOfWork, _mapper) = (units, types, unitOfWork, mapper);

    public async Task<OrgUnitDto> GetByIdAsync(Guid id) =>
        _mapper.Map<OrgUnitDto>(await _units.GetDetailedAsync(id) ?? throw new OrgUnitNotFoundException());

    public async Task<PaginatedList<OrgUnitDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, string? status = null)
    {
        (pageNumber, pageSize) = (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100));
        var page = await _units.GetPagedAsync(pageNumber, pageSize, q, status);
        return new(_mapper.Map<List<OrgUnitDto>>(page.Items), page.TotalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<GuidDropDownDto>> GetDropdownAsync() =>
        _mapper.Map<List<GuidDropDownDto>>(await _units.GetDropdownAsync());

    public async Task<IReadOnlyList<OrgUnitTreeNodeDto>> GetTreeAsync(string? status = null) =>
        OrgUnitTreeHelper.BuildTree(_mapper.Map<List<OrgUnitTreeNodeDto>>(await _units.GetTreeUnitsAsync(status)));

    public async Task<IReadOnlyList<OrgUnitDto>> GetChildrenAsync(Guid parentId)
    {
        if (!await _units.ExistsAsync(parentId)) throw new OrgUnitNotFoundException("الوحدة التنظيمية الأب غير موجودة.");
        return _mapper.Map<List<OrgUnitDto>>(await _units.GetChildrenAsync(parentId));
    }

    public async Task<IReadOnlyList<OrgUnitDto>> GetDescendantsAsync(Guid parentId)
    {
        if (!await _units.ExistsAsync(parentId)) throw new OrgUnitNotFoundException("الوحدة التنظيمية الأب غير موجودة.");
        var links = await _units.GetNonArchivedParentLinksAsync();
        var ids = OrgUnitTreeHelper.GetDescendantIds(parentId, OrgUnitTreeHelper.BuildChildrenMap(links));
        return ids.Count == 0 ? Array.Empty<OrgUnitDto>() : _mapper.Map<List<OrgUnitDto>>(await _units.GetByIdsAsync(ids));
    }

    public async Task<OrgUnitDto> CreateAsync(CreateOrgUnitDto dto)
    {
        await ValidateAsync(dto.NameAr, dto.OrgUnitTypeId, dto.ParentOrgUnitId, dto.Status, dto.Code, null);
        var entity = _mapper.Map<OrgUnit>(dto);
        entity.OrgUnitId = Guid.NewGuid();
        _units.Add(entity);
        await _unitOfWork.SaveChangesAsync();
        return await GetByIdAsync(entity.OrgUnitId);
    }

    public async Task UpdateAsync(Guid id, UpdateOrgUnitDto dto)
    {
        var entity = await _units.GetByIdAsync(id, true) ?? throw new OrgUnitNotFoundException();
        if (entity.Status == CoreStatuses.Archived)
            throw new InvalidOrgStructureDataException("لا يمكن تعديل وحدة تنظيمية مؤرشفة.");
        await ValidateAsync(dto.NameAr, dto.OrgUnitTypeId, dto.ParentOrgUnitId, dto.Status, dto.Code, id);
        _mapper.Map(dto, entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task SetStatusAsync(Guid id, string status)
    {
        var normalized = (status ?? "").Trim().ToUpperInvariant();
        if (!CoreStatuses.OrgUnitStatuses.Contains(normalized))
            throw new InvalidOrgStructureDataException("حالة الوحدة التنظيمية غير صالحة.");
        var entity = await _units.GetByIdAsync(id, true) ?? throw new OrgUnitNotFoundException();
        if (normalized == CoreStatuses.Archived && await _units.HasActiveChildrenAsync(id))
            throw new InvalidOrgStructureDataException("لا يمكن أرشفة وحدة تنظيمية لديها وحدات فرعية نشطة.");
        entity.ArchivedAt = normalized == CoreStatuses.Archived ? DateTime.Now : null;
        entity.Status = normalized;
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _units.GetByIdAsync(id, true) ?? throw new OrgUnitNotFoundException();
        if (await _units.HasAnyChildrenAsync(id))
            throw new InvalidOrgStructureDataException("لا يمكن حذف وحدة تنظيمية لديها وحدات فرعية.");
        if (await _units.HasUserOrgScopesAsync(id))
            throw new InvalidOrgStructureDataException("لا يمكن حذف وحدة تنظيمية مرتبطة بنطاقات مستخدمين.");
        _units.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task ValidateAsync(string name, Guid typeId, Guid? parentId, string status, string? code, Guid? excludeId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) errors.Add("اسم الوحدة التنظيمية مطلوب.");
        if (!CoreStatuses.OrgUnitStatuses.Contains((status ?? "").Trim().ToUpperInvariant()))
            errors.Add("حالة الوحدة التنظيمية غير صالحة.");
        if (await _types.GetByIdAsync(typeId) is null) errors.Add("نوع الوحدة التنظيمية غير موجود.");
        if (parentId.HasValue && !await _units.ExistsAsync(parentId.Value)) errors.Add("الوحدة التنظيمية الأب غير موجودة.");
        if (excludeId.HasValue && parentId == excludeId) errors.Add("لا يمكن أن تكون الوحدة التنظيمية أباً لنفسها.");
        if (excludeId.HasValue && parentId.HasValue)
        {
            var links = await _units.GetNonArchivedParentLinksAsync();
            if (OrgUnitTreeHelper.WouldCreateCycle(excludeId.Value, parentId, OrgUnitTreeHelper.BuildChildrenMap(links)))
                errors.Add("لا يمكن إنشاء تسلسل هرمي دائري.");
        }
        if (!string.IsNullOrWhiteSpace(code) && await _units.CodeExistsAsync(code.Trim(), excludeId))
            errors.Add("رمز الوحدة التنظيمية مستخدم مسبقاً.");
        if (!string.IsNullOrWhiteSpace(name) && await _units.SiblingNameExistsAsync(parentId, name.Trim(), excludeId))
            errors.Add("يوجد وحدة تنظيمية بنفس الاسم تحت نفس الأب.");
        if (errors.Count > 0) throw new InvalidOrgStructureDataException(string.Join("، ", errors));
    }
}
