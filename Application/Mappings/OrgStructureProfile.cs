using Application.Dtos.CoreDtos;
using Mapster;
using Domain.Entities.Core;

namespace Application.Mappings;

public class OrgStructureProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<OrgUnitType, OrgUnitTypeDto>();
        config.NewConfig<CreateOrgUnitTypeDto, OrgUnitType>()
            .Ignore(d => d.OrgUnitTypeId)
            .Map(d => d.Code, s => s.Code.Trim())
            .Map(d => d.NameAr, s => s.NameAr.Trim());
        config.NewConfig<UpdateOrgUnitTypeDto, OrgUnitType>()
            .Ignore(d => d.OrgUnitTypeId)
            .Map(d => d.Code, s => s.Code.Trim())
            .Map(d => d.NameAr, s => s.NameAr.Trim());

        config.NewConfig<OrgUnit, OrgUnitDto>()
            .Map(d => d.OrgUnitTypeNameAr, s => s.OrgUnitType.NameAr)
            .Map(d => d.ParentNameAr, s => s.ParentOrgUnit == null ? null : s.ParentOrgUnit.NameAr);
        config.NewConfig<OrgUnit, OrgUnitTreeNodeDto>()
            .Map(d => d.OrgUnitTypeNameAr, s => s.OrgUnitType.NameAr)
            .Ignore(d => d.Children);
        config.NewConfig<OrgUnit, GuidDropDownDto>()
            .Map(d => d.Id, s => s.OrgUnitId)
            .Map(d => d.Name, s => s.NameAr);
        config.NewConfig<CreateOrgUnitDto, OrgUnit>()
            .Ignore(d => d.OrgUnitId)
            .Map(d => d.Code, s => string.IsNullOrWhiteSpace(s.Code) ? null : s.Code.Trim())
            .Map(d => d.NameAr, s => s.NameAr.Trim())
            .Map(d => d.Status, s => s.Status.Trim().ToUpperInvariant())
            .Ignore(d => d.ArchivedAt)
            .Ignore(d => d.OrgUnitType)
            .Ignore(d => d.ParentOrgUnit);
        config.NewConfig<UpdateOrgUnitDto, OrgUnit>()
            .Ignore(d => d.OrgUnitId)
            .Map(d => d.Code, s => string.IsNullOrWhiteSpace(s.Code) ? null : s.Code.Trim())
            .Map(d => d.NameAr, s => s.NameAr.Trim())
            .Map(d => d.Status, s => s.Status.Trim().ToUpperInvariant())
            .Ignore(d => d.ArchivedAt)
            .Ignore(d => d.OrgUnitType)
            .Ignore(d => d.ParentOrgUnit);

        config.NewConfig<UserOrgScope, UserOrgScopeDto>()
            .Map(d => d.UserName, s => s.User == null ? null : s.User.UserName)
            .Map(d => d.UserFullName, s => s.User == null ? null : s.User.FullName)
            .Map(d => d.PermissionName, s => s.Permission.Name)
            .Map(d => d.OrgUnitNameAr, s => s.OrgUnit.NameAr);
        config.NewConfig<CreateUserOrgScopeDto, UserOrgScope>()
            .Ignore(d => d.UserOrgScopeId)
            .Map(d => d.UserId, s => s.UserId.Trim())
            .Map(d => d.ScopeMode, s => s.ScopeMode.Trim().ToUpperInvariant())
            .Map(d => d.Status, s => s.Status.Trim().ToUpperInvariant())
            .Ignore(d => d.AssignedAt)
            .Ignore(d => d.AssignedByUserId)
            .Ignore(d => d.User)
            .Ignore(d => d.Permission)
            .Ignore(d => d.OrgUnit)
            .Ignore(d => d.AssignedByUser);
        config.NewConfig<UpdateUserOrgScopeDto, UserOrgScope>()
            .Ignore(d => d.UserOrgScopeId)
            .Ignore(d => d.UserId)
            .Map(d => d.ScopeMode, s => s.ScopeMode.Trim().ToUpperInvariant())
            .Map(d => d.Status, s => s.Status.Trim().ToUpperInvariant())
            .Ignore(d => d.AssignedAt)
            .Ignore(d => d.AssignedByUserId)
            .Ignore(d => d.User)
            .Ignore(d => d.Permission)
            .Ignore(d => d.OrgUnit)
            .Ignore(d => d.AssignedByUser);
    }
}
