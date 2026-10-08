using Application.Common.Localization;
using Application.Dtos.AuthDtos;
using Domain.Entities.Auth;
using Mapster;

namespace Application.Mappings
{
    public class AuthProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // User Mappings
            config.NewConfig<User, UserDto>().TwoWays();
            config.NewConfig<CreateUserDto, User>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.SecurityStamp)
                .Map(dest => dest.CreationDate, _ => DateTime.Now);
            config.NewConfig<UpdateUserDto, User>()
                .Ignore(dest => dest.Id);

            config.NewConfig<User, UserListItemDto>()
                .Map(dest => dest.userId, src => src.Id)
                .Map(dest => dest.fullName, src => src.FullName ?? string.Empty)
                .Map(dest => dest.username, src => src.UserName ?? string.Empty)
                .Map(dest => dest.email, src => src.Email ?? string.Empty)
                .Map(dest => dest.phone, src => src.PhoneNumber)
                .Map(dest => dest.isActive, src => src.IsActive)
                .Map(dest => dest.createdAt, src => src.CreationDate)
                .Ignore(dest => dest.rolesCount)
                .Ignore(dest => dest.permissionsCount)
                .Ignore(dest => dest.roles)
                .Ignore(dest => dest.Permissions);

            config.NewConfig<UpdateUserPatchRequest, User>()
                .Map(dest => dest.FullName, src => src.fullName)
                .Map(dest => dest.UserName, src => src.username)
                .Map(dest => dest.Email, src => src.email)
                .Map(dest => dest.PhoneNumber, src => src.phone)
                .Map(dest => dest.IsActive, src => src.isActive ?? false)
                .IgnoreNullValues(true)
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.SecurityStamp);

            config.NewConfig<User, DropdownUserDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Name, src => src.FullName ?? src.UserName ?? string.Empty);

            config.NewConfig<User, MeResponseDto>()
                .Map(dest => dest.UserId, src => src.Id)
                .Map(dest => dest.UserName, src => src.UserName ?? string.Empty)
                .Map(dest => dest.Email, src => src.Email ?? string.Empty)
                .Map(dest => dest.FullName, src => src.FullName ?? string.Empty)
                .Map(dest => dest.Phone, src => src.PhoneNumber)
                .Map(dest => dest.CreationDate, src => src.CreationDate)
                .Ignore(dest => dest.AttachmentOwnerId)
                .Ignore(dest => dest.ProfilePhotoAttachmentId)
                .Ignore(dest => dest.ProfilePhotoUrl)
                .Ignore(dest => dest.Roles)
                .Ignore(dest => dest.Permissions);

            // Role Mappings
            config.NewConfig<Role, RoleDto>().TwoWays();
            config.NewConfig<Role, RoleDetailsDto>().TwoWays();
            config.NewConfig<RoleStatusUpdateDto, Role>().TwoWays();

            config.NewConfig<Role, DropdownRoleDto>()
                .Map(dest => dest.roleId, src => src.Id)
                .Map(dest => dest.name, src => AuthDisplayNames.RoleAr(src.Name ?? string.Empty));

            config.NewConfig<CreateRoleDto, Role>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.CreatedAt)
                .Ignore(dest => dest.CreatedBy)
                .Ignore(dest => dest.UpdatedAt)
                .Ignore(dest => dest.UpdatedBy);

            config.NewConfig<CreateRoleWithPermissionsDto, Role>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.CreatedAt)
                .Ignore(dest => dest.CreatedBy)
                .Ignore(dest => dest.UpdatedAt)
                .Ignore(dest => dest.UpdatedBy);

            config.NewConfig<UpdateRoleDto, Role>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.IsActive)
                .Ignore(dest => dest.CreatedAt)
                .Ignore(dest => dest.CreatedBy)
                .Ignore(dest => dest.UpdatedAt)
                .Ignore(dest => dest.UpdatedBy)
                .Ignore(dest => dest.UserRoleAssociations)
                .Ignore(dest => dest.RolePermissions)
                .Ignore(dest => dest.RoleFieldPermissions);

            // Permission Mappings
            config.NewConfig<Permission, PermissionDto>();

            config.NewConfig<Permission, PermissionInfoDto>()
                .Map(dest => dest.Name, src => AuthDisplayNames.PermissionAr(src.Name));

            config.NewConfig<RolePermission, PermissionInfoDto>()
                .Map(dest => dest.Id, src => src.Permission!.Id)
                .Map(dest => dest.Name, src => AuthDisplayNames.PermissionAr(src.Permission!.Name));

            config.NewConfig<Permission, PermissionWithRolesDto>()
                .Map(dest => dest.AssignedRoleIds, src => src.RolePermissions.Select(rp => rp.RoleId))
                .Map(dest => dest.AssignedRolesCount, src => src.RolePermissions.Count);

            config.NewConfig<Permission, PermissionWithRolesResultDto>()
                .Map(dest => dest.Roles, src => src.RolePermissions.Select(rp => rp.Role))
                .Map(dest => dest.Permissions, src => new List<Permission> { src });

            config.NewConfig<Role, RoleInfoDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Name, src => src.Name ?? string.Empty);

            config.NewConfig<User, UserInfoDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Name, src => src.FullName ?? src.UserName ?? string.Empty);

            config.NewConfig<UserRoleAssociation, RoleLiteDto>()
                .Map(dest => dest.RoleId, src => src.RoleId)
                .Map(dest => dest.RoleName, src => AuthDisplayNames.RoleAr(src.Role != null ? src.Role.Name : null));

            config.NewConfig<Permission, PermissionLiteDto>()
                .Map(dest => dest.PermissionId, src => src.Id.ToString())
                .Map(dest => dest.PermissionName, src => AuthDisplayNames.PermissionAr(src.Name));

            config.NewConfig<RolePermission, PermissionLiteDto>()
                .Map(dest => dest.PermissionId, src => src.PermissionId.ToString())
                .Map(dest => dest.PermissionName, src => AuthDisplayNames.PermissionAr(src.Permission != null ? src.Permission.Name : null));

            config.NewConfig<UserPermission, PermissionLiteDto>()
                .Map(dest => dest.PermissionId, src => (src.PermissionId ?? Guid.Empty).ToString())
                .Map(dest => dest.PermissionName, src => AuthDisplayNames.PermissionAr(src.Permission != null ? src.Permission.Name : null));
        }
    }
}
