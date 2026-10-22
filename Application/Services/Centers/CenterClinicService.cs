using Application.Common.Permissions;
using Application.Common.Results;
using Application.Common.Security;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Centers;

public class CenterClinicService : ICenterClinicService
{
    private readonly ICenterClinicRepository _links;
    private readonly IMedicalCenterRepository _centers;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionGate _permissionGate;

    public CenterClinicService(
        ICenterClinicRepository links,
        IMedicalCenterRepository centers,
        IReferenceDataRepository referenceData,
        IUnitOfWork unitOfWork,
        IPermissionGate permissionGate)
    {
        _links = links;
        _centers = centers;
        _referenceData = referenceData;
        _unitOfWork = unitOfWork;
        _permissionGate = permissionGate;
    }

    public async Task<PaginatedList<CenterClinicListItemDto>> GetByCenterAsync(
        Guid centerId,
        int pageNumber,
        int pageSize,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        if (await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken) is null)
            throw new MedicalCenterNotFoundException();

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 500) pageSize = 500;

        var query = _links.Query().Where(x => x.CenterId == centerId);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        query = query.OrderBy(x => x.Clinic.SortOrder).ThenBy(x => x.Clinic.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = page.Select(ToListItem).ToList();
        return new PaginatedList<CenterClinicListItemDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<CenterClinicDetailsDto> LinkAsync(
        Guid centerId,
        LinkCenterClinicDto dto,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        if (dto.ClinicId == Guid.Empty)
            throw new InvalidCenterClinicOperationException("معرّف العيادة مطلوب.");

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        var clinic = await _referenceData.Clinics().FirstOrDefaultAsync(c => c.ClinicId == dto.ClinicId, cancellationToken)
            ?? throw new ClinicNotFoundException();

        if (!clinic.IsActive)
            throw new InactiveClinicException();

        var existing = await _links.GetByCenterAndClinicAsync(
            centerId, dto.ClinicId, asNoTracking: false, cancellationToken);

        if (existing is not null)
        {
            if (existing.IsActive)
                throw new CenterClinicAlreadyExistsException();

            Reactivate(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDetails(existing);
        }

        var link = new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = dto.ClinicId,
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow,
            DeactivatedAt = null
        };

        _links.Add(link);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // سباق متزامن على القيد الفريد (CenterId, ClinicId)
            var raced = await _links.GetByCenterAndClinicAsync(
                centerId, dto.ClinicId, asNoTracking: true, cancellationToken);
            if (raced is not null)
                throw new CenterClinicAlreadyExistsException();
            throw;
        }

        var saved = await _links.GetByIdAsync(link.CenterClinicId, asNoTracking: true, cancellationToken)
            ?? throw new CenterClinicNotFoundException();
        return ToDetails(saved);
    }

    public async Task<CenterClinicDetailsDto> ActivateAsync(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        var link = await LoadOwnedLinkAsync(centerId, centerClinicId, cancellationToken);

        if (link.IsActive)
            return ToDetails(link);

        var center = await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken);
        MedicalCenterActivityGuard.EnsureAcceptsNewRelations(center);

        if (!link.Clinic.IsActive)
            throw new InactiveClinicException("لا يمكن تفعيل الربط بينما العيادة العامة معطّلة.");

        Reactivate(link);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetails(link);
    }

    public async Task<CenterClinicDetailsDto> DeactivateAsync(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default)
    {
        _permissionGate.EnsureAll(MilitaryHealthPermissions.Centers.Manage);

        var link = await LoadOwnedLinkAsync(centerId, centerClinicId, cancellationToken);

        if (!link.IsActive)
            return ToDetails(link);

        link.IsActive = false;
        link.DeactivatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDetails(link);
    }

    public async Task EnsureAcceptsDoctorAssignmentAsync(
        Guid centerId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var link = await _links.GetByCenterAndClinicAsync(
            centerId, clinicId, asNoTracking: true, cancellationToken);
        CenterClinicActivityGuard.EnsureAcceptsDoctorAssignment(link);
    }

    private async Task<CenterClinic> LoadOwnedLinkAsync(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken)
    {
        if (await _centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken) is null)
            throw new MedicalCenterNotFoundException();

        var link = await _links.GetByIdAsync(centerClinicId, asNoTracking: false, cancellationToken)
            ?? throw new CenterClinicNotFoundException();

        if (link.CenterId != centerId)
            throw new CenterClinicNotFoundException();

        return link;
    }

    private static void Reactivate(CenterClinic link)
    {
        link.IsActive = true;
        link.ActivatedAt = DateTimeOffset.UtcNow;
        link.DeactivatedAt = null;
    }

    private static CenterClinicListItemDto ToListItem(CenterClinic x) => new()
    {
        CenterClinicId = x.CenterClinicId,
        CenterId = x.CenterId,
        ClinicId = x.ClinicId,
        ClinicCode = x.Clinic.Code,
        ClinicNameAr = x.Clinic.NameAr,
        ClinicSortOrder = x.Clinic.SortOrder,
        IsActive = x.IsActive,
        ActivatedAt = x.ActivatedAt,
        DeactivatedAt = x.DeactivatedAt
    };

    private static CenterClinicDetailsDto ToDetails(CenterClinic x) => new()
    {
        CenterClinicId = x.CenterClinicId,
        CenterId = x.CenterId,
        CenterCode = x.Center.Code,
        CenterNameAr = x.Center.NameAr,
        ClinicId = x.ClinicId,
        ClinicCode = x.Clinic.Code,
        ClinicNameAr = x.Clinic.NameAr,
        ClinicSortOrder = x.Clinic.SortOrder,
        IsActive = x.IsActive,
        ActivatedAt = x.ActivatedAt,
        DeactivatedAt = x.DeactivatedAt
    };
}
