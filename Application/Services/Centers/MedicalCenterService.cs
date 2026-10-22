using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Core;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Centers;

public class MedicalCenterService : IMedicalCenterService
{
    private readonly IMedicalCenterRepository _centers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPermissionGate _permissionGate;

    public MedicalCenterService(
        IMedicalCenterRepository centers,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IPermissionGate permissionGate)
    {
        _centers = centers;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _permissionGate = permissionGate;
    }

    public async Task<PaginatedList<MedicalCenterListItemDto>> GetCentersAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 500) pageSize = 500;

        var query = _centers.Query();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Code.ToLower().Contains(term) ||
                c.NameAr.ToLower().Contains(term));
        }

        if (isActive.HasValue)
            query = query.Where(c => c.IsActive == isActive.Value);

        query = query.OrderBy(c => c.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<MedicalCenterListItemDto>(page.Count);
        foreach (var center in page)
            items.Add(await ToListItemAsync(center, cancellationToken));

        return new PaginatedList<MedicalCenterListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<MedicalCenterDetailsDto> GetByIdAsync(Guid centerId, CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken)
            ?? throw new MedicalCenterNotFoundException();

        return await ToDetailsAsync(center, cancellationToken);
    }

    public async Task<MedicalCenterDetailsDto> CreateAsync(CreateMedicalCenterDto dto, CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        ValidateCreate(dto);

        var code = dto.Code.Trim();
        if (await _centers.CodeExistsAsync(code, cancellationToken: cancellationToken))
            throw new MedicalCenterCodeAlreadyExistsException(code);

        var now = DateTimeOffset.UtcNow;
        var center = _mapper.Map<MedicalCenter>(dto);
        center.CenterId = Guid.NewGuid();
        center.Code = code;
        center.NameAr = dto.NameAr.Trim();
        center.LocationText = TrimOrNull(dto.LocationText);
        center.ParentOrgText = TrimOrNull(dto.ParentOrgText);
        center.ContactInfo = TrimOrNull(dto.ContactInfo);
        center.CreatedAt = now;
        center.UpdatedAt = now;

        if (center.IsActive)
            center.DeactivationReason = null;
        else
            center.DeactivationReason = dto.DeactivationReason!.Trim();

        _centers.Add(center);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToDetailsAsync(center, cancellationToken);
    }

    public async Task<MedicalCenterDetailsDto> UpdateAsync(
        Guid centerId,
        UpdateMedicalCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        if (string.IsNullOrWhiteSpace(dto.NameAr))
            throw new InvalidMedicalCenterDataException("اسم المركز مطلوب.");

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: false, cancellationToken)
            ?? throw new MedicalCenterNotFoundException();

        var preservedCode = center.Code;
        var preservedId = center.CenterId;
        _mapper.Map(dto, center);
        center.CenterId = preservedId;
        center.Code = preservedCode;
        center.NameAr = dto.NameAr.Trim();
        center.LocationText = TrimOrNull(dto.LocationText);
        center.ParentOrgText = TrimOrNull(dto.ParentOrgText);
        center.ContactInfo = TrimOrNull(dto.ContactInfo);
        center.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(center, cancellationToken);
    }

    public async Task<MedicalCenterDetailsDto> ActivateAsync(Guid centerId, CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: false, cancellationToken)
            ?? throw new MedicalCenterNotFoundException();

        if (center.IsActive)
            return await ToDetailsAsync(center, cancellationToken);

        center.IsActive = true;
        center.DeactivationReason = null;
        center.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(center, cancellationToken);
    }

    public async Task<MedicalCenterDetailsDto> DeactivateAsync(
        Guid centerId,
        DeactivateMedicalCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidMedicalCenterDataException("سبب التعطيل مطلوب.");

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: false, cancellationToken)
            ?? throw new MedicalCenterNotFoundException();

        if (!center.IsActive)
        {
            center.DeactivationReason = dto.Reason.Trim();
            center.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await ToDetailsAsync(center, cancellationToken);
        }

        center.IsActive = false;
        center.DeactivationReason = dto.Reason.Trim();
        center.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(center, cancellationToken);
    }

    public async Task EnsureAcceptsNewRelationsAsync(Guid centerId, CancellationToken cancellationToken = default)
    {
        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);
    }

    private static void ValidateCreate(CreateMedicalCenterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidMedicalCenterDataException("رمز المركز مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.NameAr))
            throw new InvalidMedicalCenterDataException("اسم المركز مطلوب.");
        if (!dto.IsActive && string.IsNullOrWhiteSpace(dto.DeactivationReason))
            throw new InvalidMedicalCenterDataException("سبب التعطيل مطلوب عند إنشاء مركز غير فعّال.");
    }

    private async Task<MedicalCenterListItemDto> ToListItemAsync(MedicalCenter center, CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<MedicalCenterListItemDto>(center);
        dto.UsersCount = await _centers.CountActiveUsersAsync(center.CenterId, cancellationToken);
        dto.ClinicsCount = await _centers.CountActiveClinicsAsync(center.CenterId, cancellationToken);
        return dto;
    }

    private async Task<MedicalCenterDetailsDto> ToDetailsAsync(MedicalCenter center, CancellationToken cancellationToken)
    {
        var dto = _mapper.Map<MedicalCenterDetailsDto>(center);
        dto.UsersCount = await _centers.CountActiveUsersAsync(center.CenterId, cancellationToken);
        dto.ClinicsCount = await _centers.CountActiveClinicsAsync(center.CenterId, cancellationToken);
        return dto;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
