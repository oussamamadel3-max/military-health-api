namespace Application.Interfaces.Services;

public interface IPermissionGate
{
    IReadOnlyCollection<string> GetPermissions();
    bool Has(string permission);
    bool HasAll(params string[] permissions);
    void EnsureAll(params string[] permissions);
}
