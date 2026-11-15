using Application.Attributes;
using Application.Common.Permissions;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Enums;
using Moq;

namespace Application.Tests;

public class FieldPermissionHelperTests
{
    [Fact]
    public async Task Apply_masks_protected_field_when_no_assignment_default_deny()
    {
        var fieldService = new Mock<IFieldPermissionService>();
        fieldService.Setup(s => s.GetFieldPermissionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<FieldPermissionFilterDto?>()))
            .ReturnsAsync(new PaginatedList<FieldPermissionDto>([], 0, 1, 10));

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var helper = new FieldPermissionHelper(fieldService.Object, users.Object);
        var dto = new HelperProbeDto { OpenValue = "open", SecretValue = "secret" };

        var result = await helper.ApplyFieldPermissionsAsync(dto, "u1", null, "Probe");

        Assert.Equal("open", result.OpenValue);
        Assert.Null(result.SecretValue);
    }

    [Fact]
    public async Task Apply_keeps_protected_field_when_read_assigned()
    {
        var fieldService = new Mock<IFieldPermissionService>();
        fieldService.Setup(s => s.GetFieldPermissionsAsync(1, 10000, It.Is<FieldPermissionFilterDto>(f => f.UserId == "u1")))
            .ReturnsAsync(new PaginatedList<FieldPermissionDto>(
            [
                new FieldPermissionDto
                {
                    EntityName = "Probe",
                    FieldName = "SecretValue",
                    AccessType = FieldAccessType.Read
                }
            ], 1, 1, 10));
        fieldService.Setup(s => s.GetFieldPermissionsAsync(1, 10000, It.Is<FieldPermissionFilterDto>(f => f.RoleId != null)))
            .ReturnsAsync(new PaginatedList<FieldPermissionDto>([], 0, 1, 10));

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "role-1" });

        var helper = new FieldPermissionHelper(fieldService.Object, users.Object);
        var dto = new HelperProbeDto { SecretValue = "secret" };

        var result = await helper.ApplyFieldPermissionsAsync(dto, "u1", null, "Probe");
        Assert.Equal("secret", result.SecretValue);
    }

    [Fact]
    public async Task CanWrite_false_for_read_only_and_true_for_readwrite()
    {
        var fieldService = new Mock<IFieldPermissionService>();
        fieldService.Setup(s => s.GetFieldPermissionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<FieldPermissionFilterDto?>()))
            .ReturnsAsync((int _, int __, FieldPermissionFilterDto? filter) =>
            {
                if (filter?.UserId == "u1")
                {
                    return new PaginatedList<FieldPermissionDto>(
                    [
                        new FieldPermissionDto
                        {
                            FieldName = "SecretValue",
                            AccessType = FieldAccessType.Read
                        }
                    ], 1, 1, 10);
                }

                return new PaginatedList<FieldPermissionDto>([], 0, 1, 10);
            });

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var helper = new FieldPermissionHelper(fieldService.Object, users.Object);
        Assert.False(await helper.CanWriteFieldAsync("u1", null, "Probe", "SecretValue"));
    }

    [Fact]
    public async Task ResolveRoleIds_prefers_database_ids_over_jwt_role_names()
    {
        var fieldService = new Mock<IFieldPermissionService>();
        FieldPermissionFilterDto? captured = null;
        fieldService.Setup(s => s.GetFieldPermissionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<FieldPermissionFilterDto?>()))
            .ReturnsAsync((int _, int __, FieldPermissionFilterDto? filter) =>
            {
                if (filter?.RoleId != null)
                    captured = filter;
                return new PaginatedList<FieldPermissionDto>([], 0, 1, 10);
            });

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "actual-role-guid" });

        var helper = new FieldPermissionHelper(fieldService.Object, users.Object);
        await helper.CanWriteFieldAsync("u1", ["SuperAdmin"], "Probe", "SecretValue");

        Assert.NotNull(captured);
        Assert.Equal("actual-role-guid", captured!.RoleId);
    }
}

public sealed class HelperProbeDto
{
    public string? OpenValue { get; set; }

    [FieldPermission("SecretValue")]
    public string? SecretValue { get; set; }
}
