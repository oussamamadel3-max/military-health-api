using Application.Common.Permissions;
using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.Services.ReferenceData;

/// <summary>
/// واجهة موحدة لإدارة القوائم المرجعية؛ التفويض والتنسيق هنا، والمنطق الخاص بكل كتالوج في IReferenceCatalogHandler.
/// </summary>
public class ReferenceDataService : IReferenceDataService
{
    private readonly IReadOnlyDictionary<string, IReferenceCatalogHandler> _handlers;
    private readonly IPermissionGate _permissionGate;
    private readonly ICurrentUserService _currentUser;

    public ReferenceDataService(
        IEnumerable<IReferenceCatalogHandler> handlers,
        IPermissionGate permissionGate,
        ICurrentUserService currentUser)
    {
        _handlers = handlers.ToDictionary(h => h.Catalog, StringComparer.OrdinalIgnoreCase);
        _permissionGate = permissionGate;
        _currentUser = currentUser;
    }

    public IReadOnlyList<string> GetCatalogs()
    {
        EnsureManagePermission();
        return ReferenceCatalogs.All;
    }

    public async Task<PaginatedList<ReferenceDataItemDto>> GetAdminListAsync(
        string catalog,
        int pageNumber,
        int pageSize,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        ReferenceDataSupport.NormalizePaging(ref pageNumber, ref pageSize);
        return await Resolve(catalog).ListAdminAsync(pageNumber, pageSize, search, isActive, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceDataOptionDto>> GetActiveOptionsAsync(
        string catalog,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();

        return await Resolve(catalog).GetActiveOptionsAsync(cancellationToken);
    }

    public async Task<ReferenceDataItemDto> GetByIdAsync(
        string catalog,
        string id,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        return await Resolve(catalog).GetByIdAsync(id, cancellationToken);
    }

    public async Task<ReferenceDataItemDto> CreateAsync(
        string catalog,
        ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        ReferenceDataSupport.ValidateCommon(dto);
        return await Resolve(catalog).CreateAsync(dto, cancellationToken);
    }

    public async Task<ReferenceDataItemDto> UpdateAsync(
        string catalog,
        string id,
        ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        ReferenceDataSupport.ValidateCommon(dto);
        return await Resolve(catalog).UpdateAsync(id, dto, cancellationToken);
    }

    public async Task<ReferenceDataItemDto> SetActiveAsync(
        string catalog,
        string id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        return await Resolve(catalog).SetActiveAsync(id, isActive, cancellationToken);
    }

    private IReferenceCatalogHandler Resolve(string catalog)
    {
        var key = ReferenceDataSupport.NormalizeCatalog(catalog);
        if (!_handlers.TryGetValue(key, out var handler))
            throw new InvalidReferenceDataException("كتالوج مرجعي غير معروف.");
        return handler;
    }

    private void EnsureManagePermission()
        => _permissionGate.EnsureAll(MilitaryHealthPermissions.System.ReferenceData);
}
