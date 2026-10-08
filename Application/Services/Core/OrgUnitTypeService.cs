using Application.Common.Results;
using Application.Dtos.CoreDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using MapsterMapper;
using Domain.Entities.Core;

namespace Application.Services.Core;

public class OrgUnitTypeService : IOrgUnitTypeService
{
    private readonly IOrgUnitTypeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public OrgUnitTypeService(IOrgUnitTypeRepository repository, IUnitOfWork unitOfWork, IMapper mapper) =>
        (_repository, _unitOfWork, _mapper) = (repository, unitOfWork, mapper);

    public async Task<OrgUnitTypeDto> GetByIdAsync(Guid id) =>
        _mapper.Map<OrgUnitTypeDto>(await _repository.GetByIdAsync(id) ?? throw new OrgUnitTypeNotFoundException());

    public async Task<PaginatedList<OrgUnitTypeDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null)
    {
        (pageNumber, pageSize) = Normalize(pageNumber, pageSize);
        var page = await _repository.GetPagedAsync(pageNumber, pageSize, q, isActive);
        return new(_mapper.Map<List<OrgUnitTypeDto>>(page.Items), page.TotalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<OrgUnitTypeDto>> GetAllActiveAsync() =>
        _mapper.Map<List<OrgUnitTypeDto>>(await _repository.GetActiveAsync());

    public async Task<OrgUnitTypeDto> CreateAsync(CreateOrgUnitTypeDto dto)
    {
        Validate(dto.Code, dto.NameAr);
        var code = dto.Code.Trim();
        if (await _repository.CodeExistsAsync(code))
            throw new InvalidOrgStructureDataException("رمز نوع الوحدة التنظيمية مستخدم مسبقاً.");
        var entity = _mapper.Map<OrgUnitType>(dto);
        entity.OrgUnitTypeId = Guid.NewGuid();
        _repository.Add(entity);
        await _unitOfWork.SaveChangesAsync();
        return _mapper.Map<OrgUnitTypeDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateOrgUnitTypeDto dto)
    {
        Validate(dto.Code, dto.NameAr);
        var entity = await _repository.GetByIdAsync(id, true) ?? throw new OrgUnitTypeNotFoundException();
        if (await _repository.CodeExistsAsync(dto.Code.Trim(), id))
            throw new InvalidOrgStructureDataException("رمز نوع الوحدة التنظيمية مستخدم مسبقاً.");
        _mapper.Map(dto, entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var entity = await _repository.GetByIdAsync(id, true) ?? throw new OrgUnitTypeNotFoundException();
        entity.IsActive = isActive;
        await _unitOfWork.SaveChangesAsync();
    }

    private static void Validate(string code, string name)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(code)) errors.Add("رمز نوع الوحدة التنظيمية مطلوب.");
        if (string.IsNullOrWhiteSpace(name)) errors.Add("اسم نوع الوحدة التنظيمية مطلوب.");
        if (errors.Count > 0) throw new InvalidOrgStructureDataException(string.Join("، ", errors));
    }

    private static (int, int) Normalize(int page, int size) => (Math.Max(1, page), Math.Clamp(size, 1, 100));
}
