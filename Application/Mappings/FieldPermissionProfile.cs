using Application.Common.Utilities;
using Application.Dtos.AuthDtos;
using Domain.Entities.Auth;
using Mapster;

namespace Application.Mappings
{
    public class FieldPermissionProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<FieldPermission, FieldPermissionDto>()
                .Map(dest => dest.Description, src => src.Description ?? string.Empty)
                .Map(dest => dest.AccessTypeDescription, src => EnumHelper.GetEnumDescription(src.AccessType))
                .Ignore(dest => dest.Roles)
                .Ignore(dest => dest.Users);

            config.NewConfig<UpdateFieldPermissionDto, FieldPermission>()
                .Map(dest => dest.AccessType, src => src.AccessType)
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.EntityName)
                .Ignore(dest => dest.FieldName)
                .Ignore(dest => dest.Description)
                .Ignore(dest => dest.RoleFieldPermissions)
                .Ignore(dest => dest.UserFieldPermissions);
        }
    }
}
