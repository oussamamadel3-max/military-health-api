using ArchiveProject.Domain.Enums;
using System.Collections.Generic;

namespace ArchiveProject.Application.Dtos.AuthDtos;

public class FieldPermissionLoginDto
{
    public Guid Id { get; set; }

    public string FieldName { get; set; } = string.Empty;
    public FieldAccessType AccessType { get; set; } = FieldAccessType.Read;
}
public class FieldPermissionDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public FieldAccessType AccessType { get; set; } = FieldAccessType.Read;
    public string AccessTypeDescription { get; set; } = string.Empty;
    public List<RoleInfoDto>? Roles { get; set; } = new();
    public List<UserInfoDto>? Users { get; set; } = new();
}


public class CreateFieldPermissionDto
{
    public string EntityName { get; set; } = string.Empty;
    public List<string> FieldNames { get; set; } = new();
    public FieldAccessType AccessType { get; set; } = FieldAccessType.Read;
    public List<string>? RoleIds { get; set; } = new();
    public List<string>? UserIds { get; set; } = new();
}

public class CreateFieldPermissionsResultDto
{
    public string EntityName { get; set; } = string.Empty;
    public FieldAccessType AccessType { get; set; } = FieldAccessType.Read;
    public List<FieldPermissionDto> Permissions { get; set; } = new();
}

public class EntityInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class FieldInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class ChangeFieldPermissionAccessTypeDto
{
    public FieldAccessType AccessType { get; set; }
}

public class UpdateFieldPermissionDto
{
    public FieldAccessType AccessType { get; set; }
    public List<string>? RoleIds { get; set; } = new();
    public List<string>? UserIds { get; set; } = new();
}

public class FieldAccessTypeDto
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class FieldPermissionFilterDto
{
    public string? RoleId { get; set; }
    public string? UserId { get; set; }
    public string? EntityName { get; set; }
    public string? FieldName { get; set; }
    public FieldAccessType? AccessType { get; set; }
}

public class RoleInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class UserInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

