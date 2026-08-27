namespace Application.Interfaces.Services;

public sealed record OrgAccessResult(bool HasPermission, bool HasGlobalAccess, IReadOnlyList<Guid> OrgUnitIds);

public interface IOrgAccessService
{
    Task<OrgAccessResult> GetAccessAsync(string userId, string permissionName, CancellationToken ct = default);
}
