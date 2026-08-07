using Application.Dtos.AuthDtos;
using AutoMapper;
using Domain.Entities.Auth;

namespace Application.Mappings
{
    public class AuthProfile : Profile
    {
        public AuthProfile()
        {
            // User Mappings
            CreateMap<User, UserDto>().ReverseMap();
            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.SecurityStamp, opt => opt.Ignore())
                .ForMember(dest => dest.CreationDate, opt => opt.MapFrom(_ => DateTime.Now));
            CreateMap<UpdateUserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());

            // Role Mappings
            CreateMap<Role, RoleDto>().ReverseMap();
            CreateMap<Role, RoleDetailsDto>().ReverseMap();
            CreateMap<RoleStatusUpdateDto, Role>().ReverseMap();

            CreateMap<CreateRoleDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore());

            CreateMap<CreateRoleWithPermissionsDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore());

            CreateMap<UpdateRoleDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UserRoleAssociations, opt => opt.Ignore())
                .ForMember(dest => dest.RolePermissions, opt => opt.Ignore())
                .ForMember(dest => dest.RoleFieldPermissions, opt => opt.Ignore());

            // Permission Mappings
            CreateMap<Permission, PermissionWithRolesDto>()
                .ForMember(dest => dest.AssignedRoleIds, opt => opt.MapFrom(src => src.RolePermissions.Select(rp => rp.RoleId)))
                .ForMember(dest => dest.AssignedRolesCount, opt => opt.MapFrom(src => src.RolePermissions.Count));

            CreateMap<Permission, PermissionWithRolesResultDto>()
                .ForMember(dest => dest.Roles, opt => opt.MapFrom(src => src.RolePermissions.Select(rp => rp.Role)))
                .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src));
        }
    }
}
