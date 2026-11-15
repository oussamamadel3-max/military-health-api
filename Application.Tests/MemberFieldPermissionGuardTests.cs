using Application.Common.Options;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.Members;
using Domain.Enums;
using Moq;

namespace Application.Tests;

public class MemberFieldPermissionGuardTests
{
    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task When_enforcement_disabled_read_and_write_are_noops()
    {
        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var guard = CreateGuard(enabled: false, users.Object, "u1");

        var dto = new MemberDetailsDto { MotherName = "أم سرية" };
        await guard.ApplyReadToDetailsAsync(dto);
        Assert.Equal("أم سرية", dto.MotherName);

        await guard.EnsureMotherNameWriteAllowedAsync("قيمة", null, isCreate: true);
        await guard.EnsureMotherNameWriteAllowedAsync("قيمة جديدة", "قيمة قديمة", isCreate: false);
        users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(FieldAccessType.None, false)]
    [InlineData(FieldAccessType.Write, false)]
    [InlineData(FieldAccessType.Read, true)]
    [InlineData(FieldAccessType.ReadWrite, true)]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Read_masks_MotherName_according_to_access(FieldAccessType access, bool visible)
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        var dto = new MemberDetailsDto { MotherName = "أم سرية" };
        await guard.ApplyReadToDetailsAsync(dto);

        if (visible)
            Assert.Equal("أم سرية", dto.MotherName);
        else
            Assert.True(string.IsNullOrEmpty(dto.MotherName));
    }

    [Theory]
    [InlineData(FieldAccessType.None)]
    [InlineData(FieldAccessType.Read)]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_with_value_requires_write(FieldAccessType access)
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            guard.EnsureMotherNameWriteAllowedAsync("أم", null, isCreate: true));
    }

    [Theory]
    [InlineData(FieldAccessType.Write)]
    [InlineData(FieldAccessType.ReadWrite)]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_with_value_allowed_when_CanWrite(FieldAccessType access)
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        await guard.EnsureMotherNameWriteAllowedAsync("أم", null, isCreate: true);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_without_value_does_not_require_field_write()
    {
        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        await guard.EnsureMotherNameWriteAllowedAsync("  ", null, isCreate: true);
        await guard.EnsureMotherNameWriteAllowedAsync(null, null, isCreate: true);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_same_value_does_not_require_write()
    {
        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        await guard.EnsureMotherNameWriteAllowedAsync("  أم  ", "أم", isCreate: false);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_changed_or_cleared_value_requires_write()
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FieldAccessType.Read);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            guard.EnsureMotherNameWriteAllowedAsync("أم أخرى", "أم", isCreate: false));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            guard.EnsureMotherNameWriteAllowedAsync("", "أم", isCreate: false));
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Missing_user_id_defaults_to_deny_on_write()
    {
        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var guard = CreateGuard(enabled: true, users.Object, userId: "");

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            guard.EnsureMotherNameWriteAllowedAsync("أم", null, isCreate: true));
        users.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task Import_preview_masks_MotherName_when_Read_denied()
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FieldAccessType.Write);
        var guard = CreateGuard(enabled: true, users.Object, "u1");

        var rows = new List<MemberImportValidRow>
        {
            new()
            {
                RowNumber = 1,
                FullName = "منتسب",
                MotherName = "أم سرية",
                BirthDate = new DateOnly(2000, 1, 1),
                MaritalStatusId = 1,
                Occupation = "طالب",
                RecruitmentNumber = "R-1",
                RecruitmentCenterId = Guid.NewGuid()
            }
        };

        var masked = await guard.MaskImportPreviewRowsAsync(rows);
        Assert.Single(masked);
        Assert.True(string.IsNullOrEmpty(masked[0].MotherName));
        Assert.Equal("أم سرية", rows[0].MotherName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task Import_preview_keeps_MotherName_when_enforcement_off()
    {
        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var guard = CreateGuard(enabled: false, users.Object, "u1");
        var rows = new List<MemberImportValidRow>
        {
            new() { RowNumber = 1, FullName = "أ", MotherName = "أم", BirthDate = new DateOnly(2000, 1, 1),
                MaritalStatusId = 1, Occupation = "س", RecruitmentNumber = "R", RecruitmentCenterId = Guid.NewGuid() }
        };

        var result = await guard.MaskImportPreviewRowsAsync(rows);
        Assert.Equal("أم", result[0].MotherName);
        users.VerifyNoOtherCalls();
    }

    private static MemberFieldPermissionGuard CreateGuard(
        bool enabled,
        IUserPermissionService users,
        string userId)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns(userId);
        return new MemberFieldPermissionGuard(
            Microsoft.Extensions.Options.Options.Create(
                new FieldPermissionsOptions { EnforcementEnabled = enabled }),
            users,
            current.Object);
    }
}
