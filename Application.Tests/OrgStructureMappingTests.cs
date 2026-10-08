using Application.Dtos.CoreDtos;
using Application.Mappings;
using Domain.Entities.Core;
using Mapster;
using MapsterMapper;

namespace Application.Tests;

public class OrgStructureMappingTests
{
    private static Mapper CreateMapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(OrgStructureProfile).Assembly);
        config.Compile();
        return new Mapper(config);
    }

    [Fact]
    public void UpdateOrgUnit_NormalizesInputAndPreservesIdentityAndNavigation()
    {
        var id = Guid.NewGuid();
        var archivedAt = DateTime.UtcNow;
        var type = new OrgUnitType { NameAr = "Type" };
        var entity = new OrgUnit { OrgUnitId = id, ArchivedAt = archivedAt, OrgUnitType = type };

        CreateMapper().Map(new UpdateOrgUnitDto { Code = "  HQ  ", NameAr = "  Unit  ", Status = " active " }, entity);

        Assert.Equal(id, entity.OrgUnitId);
        Assert.Equal(archivedAt, entity.ArchivedAt);
        Assert.Same(type, entity.OrgUnitType);
        Assert.Equal("HQ", entity.Code);
        Assert.Equal("Unit", entity.NameAr);
        Assert.Equal("ACTIVE", entity.Status);
    }

    [Fact]
    public void UpdateScope_PreservesAssignmentAndUserWhileUpdatingScope()
    {
        var id = Guid.NewGuid();
        var assignedAt = DateTime.UtcNow;
        var entity = new UserOrgScope { UserOrgScopeId = id, UserId = "user", AssignedAt = assignedAt, AssignedByUserId = "admin" };

        CreateMapper().Map(new UpdateUserOrgScopeDto { ScopeMode = " subtree ", Status = " active " }, entity);

        Assert.Equal(id, entity.UserOrgScopeId);
        Assert.Equal("user", entity.UserId);
        Assert.Equal("admin", entity.AssignedByUserId);
        Assert.Equal(assignedAt, entity.AssignedAt);
        Assert.Equal("SUBTREE", entity.ScopeMode);
        Assert.Equal("ACTIVE", entity.Status);
    }
}
