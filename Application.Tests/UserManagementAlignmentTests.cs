using System.ComponentModel.DataAnnotations;
using Application.Common.Permissions;
using Application.Common.Security;
using PasswordPolicyRules = Application.Common.PasswordPolicy;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

public class UserManagementAlignmentTests
{
    private const string StrongPassword = "Str0ng!Pass";
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Reset_password_sets_must_change_flag_and_invalidates_sessions()
    {
        var harness = CreateAdminHarness(unrestricted: true);
        harness.Target.IsActive = true;
        harness.UserManager
            .Setup(m => m.GeneratePasswordResetTokenAsync(harness.Target))
            .ReturnsAsync("reset-token");
        harness.UserManager
            .Setup(m => m.ResetPasswordAsync(harness.Target, "reset-token", StrongPassword))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateAsync(harness.Target))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateSecurityStampAsync(harness.Target))
            .Callback(() => harness.Target.SecurityStamp = "stamp-after-reset")
            .ReturnsAsync(IdentityResult.Success);

        await harness.Service.ResetUserPasswordAsync(new AdminChangePasswordRequest
        {
            UserId = harness.Target.Id,
            NewPassword = StrongPassword,
            ConfirmPassword = StrongPassword
        });

        Assert.True(harness.Target.MustChangePassword);
        Assert.True(harness.RefreshToken.IsRevoked);
        harness.Gate.Verify(g => g.EnsureAll(SystemPermissions.UserManagement.ResetPassword), Times.Once);
    }

    [Fact]
    public async Task Reset_password_rejects_inactive_account()
    {
        var harness = CreateAdminHarness(unrestricted: true);
        harness.Target.IsActive = false;

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ResetUserPasswordAsync(new AdminChangePasswordRequest
            {
                UserId = harness.Target.Id,
                NewPassword = StrongPassword,
                ConfirmPassword = StrongPassword
            }));

        Assert.Contains("تفعيل", error.Message);
        harness.UserManager.Verify(
            m => m.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Reset_password_rejects_confirm_mismatch_and_weak_password()
    {
        var harness = CreateAdminHarness(unrestricted: true);

        var mismatch = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ResetUserPasswordAsync(new AdminChangePasswordRequest
            {
                UserId = harness.Target.Id,
                NewPassword = StrongPassword,
                ConfirmPassword = "An0ther!Pass"
            }));
        Assert.Equal(PasswordPolicyRules.ConfirmationMismatchMessage, mismatch.Message);

        var weak = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ResetUserPasswordAsync(new AdminChangePasswordRequest
            {
                UserId = harness.Target.Id,
                NewPassword = "weak",
                ConfirmPassword = "weak"
            }));
        Assert.Equal(PasswordPolicyRules.ComplexityErrorMessage, weak.Message);
    }

    [Fact]
    public async Task Center_manager_cannot_reset_password_outside_assigned_centers()
    {
        var harness = CreateAdminHarness(unrestricted: false, sharesCenter: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.ResetUserPasswordAsync(new AdminChangePasswordRequest
            {
                UserId = harness.Target.Id,
                NewPassword = StrongPassword,
                ConfirmPassword = StrongPassword
            }));

        harness.UserManager.Verify(
            m => m.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Center_manager_cannot_manage_central_user()
    {
        var harness = CreateAdminHarness(unrestricted: false, sharesCenter: true, targetIsCentral: true);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.UpdateUserStatusAsync(harness.Target.Id, new UpdateUserStatusRequest { isActive = false }));
    }

    [Fact]
    public async Task Disable_user_invalidates_sessions_activate_does_not()
    {
        var harness = CreateAdminHarness(unrestricted: true);
        harness.Target.IsActive = true;
        harness.UserManager
            .Setup(m => m.UpdateAsync(harness.Target))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateSecurityStampAsync(harness.Target))
            .ReturnsAsync(IdentityResult.Success);

        await harness.Service.UpdateUserStatusAsync(
            harness.Target.Id, new UpdateUserStatusRequest { isActive = false });
        Assert.True(harness.RefreshToken.IsRevoked);

        harness.RefreshToken.IsRevoked = false;
        harness.RefreshToken.RevokedAt = null;
        harness.Target.IsActive = false;

        await harness.Service.UpdateUserStatusAsync(
            harness.Target.Id, new UpdateUserStatusRequest { isActive = true });
        Assert.False(harness.RefreshToken.IsRevoked);
    }

    [Fact]
    public void Login_session_blocks_operational_access_when_must_change_password()
    {
        Assert.False(LoginCenterContext.AllowsOperationalAccess(
            LoginCenterContext.Automatic, 3, mustChangePassword: true));
        Assert.Equal(
            LoginCenterContext.MustChangePasswordMessage,
            LoginCenterContext.AccountMessage(LoginCenterContext.Automatic, false, true));
    }

    [Fact]
    public void Admin_change_password_dto_requires_confirm()
    {
        var dto = new AdminChangePasswordRequest
        {
            UserId = "user-1",
            NewPassword = StrongPassword,
            ConfirmPassword = "An0ther!Pass"
        };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), results, true));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminChangePasswordRequest.ConfirmPassword)));
    }

    [Fact]
    public void Scope_guard_allows_central_actor_unrestricted()
    {
        Assert.True(CenterPermissionEvaluator.HasCentralAuthority(
            new[]
            {
                new CenterPermissionEvaluator.RoleAssignment("r1", "SuperAdmin", null, true, true)
            },
            Array.Empty<CenterPermissionEvaluator.DirectAssignment>()));
    }

    private static AdminHarness CreateAdminHarness(
        bool unrestricted,
        bool sharesCenter = true,
        bool targetIsCentral = false)
    {
        var target = new User
        {
            Id = "target-1",
            UserName = "nurse",
            IsActive = true,
            SecurityStamp = "stamp-1",
            MustChangePassword = false
        };
        var refreshToken = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = target.Id,
            Token = "hash",
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(target.Id)).ReturnsAsync(target);

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        users.Setup(r => r.FindByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        users.Setup(r => r.GetDirectPermissionNamesAsync(target.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.GetActiveRoleIdsAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        users.Setup(r => r.SharesActiveCenterAsync("actor-1", target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sharesCenter);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());

        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.GetActiveByUserIdAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserRefreshToken> { refreshToken });
        tokens.Setup(r => r.MarkRevoked(It.IsAny<UserRefreshToken>())).Callback<UserRefreshToken>(item =>
        {
            item.IsRevoked = true;
            item.RevokedAt = DateTimeOffset.UtcNow;
        });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync("actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles = unrestricted
                    ? new List<CenterPermissionEvaluator.RoleAssignment>
                    {
                        new("role-central", "SuperAdmin", null, true, true)
                    }
                    : new List<CenterPermissionEvaluator.RoleAssignment>
                    {
                        new("role-local", "CenterManager", CenterA, true, true)
                    }
            });
        access.Setup(a => a.LoadAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles = targetIsCentral
                    ? new List<CenterPermissionEvaluator.RoleAssignment>
                    {
                        new("role-central", "SuperAdmin", null, true, true)
                    }
                    : new List<CenterPermissionEvaluator.RoleAssignment>
                    {
                        new("role-local", "ReceptionClerk", CenterB, true, true)
                    }
            });

        var gate = new Mock<IPermissionGate>();
        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "manager", "127.0.0.1");

        var service = new UserService(
            manager.Object,
            null!,
            users.Object,
            roles.Object,
            null!,
            null!,
            tokens.Object,
            access.Object,
            uow.Object,
            gate.Object,
            null!,
            actor,
            null!,
            null!);

        return new AdminHarness(service, manager, gate, target, refreshToken);
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
    }

    private sealed record AdminHarness(
        UserService Service,
        Mock<UserManager<User>> UserManager,
        Mock<IPermissionGate> Gate,
        User Target,
        UserRefreshToken RefreshToken);
}
