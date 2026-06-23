using Domain.Enums;

namespace Application.Interfaces.Services;

public interface IUserContextService
{
    string? UserId { get; }
    IReadOnlyList<string> RoleIds { get; }
    IReadOnlyDictionary<string, FieldAccessType> FieldPermissions { get; }
}


