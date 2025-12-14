using ArchiveProject.Domain.Enums;

namespace ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;

public interface IUserContextService
{
    string? UserId { get; }
    IReadOnlyList<string> RoleIds { get; }
    IReadOnlyDictionary<string, FieldAccessType> FieldPermissions { get; }
}


