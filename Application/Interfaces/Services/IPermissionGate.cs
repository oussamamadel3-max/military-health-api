namespace Application.Interfaces.Services;

public interface IPermissionGate
{
    IReadOnlyCollection<string> GetPermissions();
    bool Has(string permission);
    bool HasAll(params string[] permissions);
    void EnsureAll(params string[] permissions);
    Task<Guid> EnsureLocalAsync(string permission, Guid? clientCenterId = null, CancellationToken cancellationToken = default);
    Task EnsureCentralAsync(string permission, CancellationToken cancellationToken = default);
}
