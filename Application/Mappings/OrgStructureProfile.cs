using Application.Dtos.CoreDtos;
using AutoMapper;
using Domain.Entities.Core;

namespace Application.Mappings;

public class OrgStructureProfile : Profile
{
    public OrgStructureProfile()
    {
        CreateMap<OrgUnitType, OrgUnitTypeDto>();
        CreateMap<CreateOrgUnitTypeDto, OrgUnitType>()
            .ForMember(d => d.OrgUnitTypeId, o => o.Ignore())
            .ForMember(d => d.Code, o => o.MapFrom(s => s.Code.Trim()))
            .ForMember(d => d.NameAr, o => o.MapFrom(s => s.NameAr.Trim()));
        CreateMap<UpdateOrgUnitTypeDto, OrgUnitType>()
            .ForMember(d => d.OrgUnitTypeId, o => o.Ignore())
            .ForMember(d => d.Code, o => o.MapFrom(s => s.Code.Trim()))
            .ForMember(d => d.NameAr, o => o.MapFrom(s => s.NameAr.Trim()));

        CreateMap<OrgUnit, OrgUnitDto>()
            .ForMember(d => d.OrgUnitTypeNameAr, o => o.MapFrom(s => s.OrgUnitType.NameAr))
            .ForMember(d => d.ParentNameAr, o => o.MapFrom(s => s.ParentOrgUnit == null ? null : s.ParentOrgUnit.NameAr));
        CreateMap<OrgUnit, OrgUnitTreeNodeDto>()
            .ForMember(d => d.OrgUnitTypeNameAr, o => o.MapFrom(s => s.OrgUnitType.NameAr))
            .ForMember(d => d.Children, o => o.Ignore());
        CreateMap<OrgUnit, GuidDropDownDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.OrgUnitId))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.NameAr));
        CreateMap<CreateOrgUnitDto, OrgUnit>()
            .ForMember(d => d.OrgUnitId, o => o.Ignore())
            .ForMember(d => d.Code, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Code) ? null : s.Code.Trim()))
            .ForMember(d => d.NameAr, o => o.MapFrom(s => s.NameAr.Trim()))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.Trim().ToUpperInvariant()))
            .ForMember(d => d.ArchivedAt, o => o.Ignore())
            .ForMember(d => d.OrgUnitType, o => o.Ignore())
            .ForMember(d => d.ParentOrgUnit, o => o.Ignore());
        CreateMap<UpdateOrgUnitDto, OrgUnit>()
            .ForMember(d => d.OrgUnitId, o => o.Ignore())
            .ForMember(d => d.Code, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Code) ? null : s.Code.Trim()))
            .ForMember(d => d.NameAr, o => o.MapFrom(s => s.NameAr.Trim()))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.Trim().ToUpperInvariant()))
            .ForMember(d => d.ArchivedAt, o => o.Ignore())
            .ForMember(d => d.OrgUnitType, o => o.Ignore())
            .ForMember(d => d.ParentOrgUnit, o => o.Ignore());

        CreateMap<UserOrgScope, UserOrgScopeDto>()
            .ForMember(d => d.UserName, o => o.MapFrom(s => s.User == null ? null : s.User.UserName))
            .ForMember(d => d.UserFullName, o => o.MapFrom(s => s.User == null ? null : s.User.FullName))
            .ForMember(d => d.PermissionName, o => o.MapFrom(s => s.Permission.Name))
            .ForMember(d => d.OrgUnitNameAr, o => o.MapFrom(s => s.OrgUnit.NameAr));
        CreateMap<CreateUserOrgScopeDto, UserOrgScope>()
            .ForMember(d => d.UserOrgScopeId, o => o.Ignore())
            .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId.Trim()))
            .ForMember(d => d.ScopeMode, o => o.MapFrom(s => s.ScopeMode.Trim().ToUpperInvariant()))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.Trim().ToUpperInvariant()))
            .ForMember(d => d.AssignedAt, o => o.Ignore())
            .ForMember(d => d.AssignedByUserId, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore())
            .ForMember(d => d.Permission, o => o.Ignore())
            .ForMember(d => d.OrgUnit, o => o.Ignore())
            .ForMember(d => d.AssignedByUser, o => o.Ignore());
        CreateMap<UpdateUserOrgScopeDto, UserOrgScope>()
            .ForMember(d => d.UserOrgScopeId, o => o.Ignore())
            .ForMember(d => d.UserId, o => o.Ignore())
            .ForMember(d => d.ScopeMode, o => o.MapFrom(s => s.ScopeMode.Trim().ToUpperInvariant()))
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.Trim().ToUpperInvariant()))
            .ForMember(d => d.AssignedAt, o => o.Ignore())
            .ForMember(d => d.AssignedByUserId, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore())
            .ForMember(d => d.Permission, o => o.Ignore())
            .ForMember(d => d.OrgUnit, o => o.Ignore())
            .ForMember(d => d.AssignedByUser, o => o.Ignore());
    }
}
