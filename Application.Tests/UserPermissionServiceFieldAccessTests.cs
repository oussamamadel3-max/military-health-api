using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Enums;
using Moq;

namespace Application.Tests;

/// <summary>Phase C.1: دمج صلاحيات الحقل عبر UserPermissionService (أدوار + مستخدم).</summary>
public class UserPermissionServiceFieldAccessTests
{
    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task GetFieldAccessAsync_user_assignment_overrides_role_union()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUserFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FieldAccessType.None);
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "role-a", "role-b" });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRoleFieldAccessTypesAsync(
                It.IsAny<IEnumerable<string>>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldAccessType> { FieldAccessType.Read, FieldAccessType.Write });

        var service = CreateService(users.Object, roles.Object);
        var access = await service.GetFieldAccessAsync("u1", "Member", "MotherName");

        Assert.Equal(FieldAccessType.None, access);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task GetFieldAccessAsync_without_user_merges_roles_to_ReadWrite()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUserFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldAccessType?)null);
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "role-a", "role-b" });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRoleFieldAccessTypesAsync(
                It.IsAny<IEnumerable<string>>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldAccessType> { FieldAccessType.Read, FieldAccessType.Write });

        var service = CreateService(users.Object, roles.Object);
        var access = await service.GetFieldAccessAsync("u1", "Member", "MotherName");

        Assert.Equal(FieldAccessType.ReadWrite, access);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task GetFieldAccessAsync_without_any_assignment_defaults_to_None()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUserFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldAccessType?)null);
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var roles = new Mock<IRoleRepository>(MockBehavior.Strict);
        var service = CreateService(users.Object, roles.Object);
        var access = await service.GetFieldAccessAsync("u1", "Member", "MotherName");

        Assert.Equal(FieldAccessType.None, access);
        roles.VerifyNoOtherCalls();
    }

    private static UserPermissionService CreateService(IUserRepository users, IRoleRepository roles)
    {
        var access = new Mock<IAccessContextRepository>();
        var currentCenter = new Mock<ICurrentCenterService>();
        currentCenter.SetupGet(c => c.IsCentralContext).Returns(true);
        currentCenter.SetupGet(c => c.CenterId).Returns((Guid?)null);

        return new UserPermissionService(users, roles, access.Object, currentCenter.Object);
    }
}
