using System.ComponentModel.DataAnnotations;
using Application.Common.Permissions;
using PasswordPolicyRules = Application.Common.PasswordPolicy;
using Application.Common.Security;
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

public class ChangeOwnPasswordTests
{
    private const string StrongPassword = "Str0ng!Pass";
    private const string AnotherStrongPassword = "An0ther!Pass";

    [Fact]
    public async Task Wrong_current_password_rejects_without_invalidating_sessions()
    {
        var harness = CreateHarness();
        harness.UserManager
            .Setup(m => m.CheckPasswordAsync(harness.User, "wrong-current"))
            .ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ChangeOwnPasswordAsync(ValidRequest(current: "wrong-current")));

        Assert.Contains("كلمة المرور الحالية غير صحيحة", error.Message);
        harness.UserManager.Verify(
            m => m.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        harness.UserManager.Verify(
            m => m.UpdateSecurityStampAsync(It.IsAny<User>()),
            Times.Never);
        harness.Tokens.Verify(r => r.MarkRevoked(It.IsAny<UserRefreshToken>()), Times.Never);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reusing_current_password_rejects_without_invalidating_sessions()
    {
        var harness = CreateHarness();
        var stampBefore = harness.User.SecurityStamp;
        var hashBefore = harness.User.PasswordHash;
        // Same plaintext for current and new — both verify against the stored hash.
        harness.UserManager
            .Setup(m => m.CheckPasswordAsync(harness.User, StrongPassword))
            .ReturnsAsync(true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ChangeOwnPasswordAsync(ValidRequest(current: StrongPassword, next: StrongPassword)));

        Assert.Equal(PasswordPolicyRules.SameAsCurrentMessage, error.Message);
        Assert.Equal(stampBefore, harness.User.SecurityStamp);
        Assert.Equal(hashBefore, harness.User.PasswordHash);
        harness.UserManager.Verify(
            m => m.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        harness.UserManager.Verify(
            m => m.UpdateSecurityStampAsync(It.IsAny<User>()),
            Times.Never);
        harness.Tokens.Verify(r => r.MarkRevoked(It.IsAny<UserRefreshToken>()), Times.Never);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected_before_identity_change()
    {
        var harness = CreateHarness();

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ChangeOwnPasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = "Current!1",
                NewPassword = "weak",
                ConfirmPassword = "weak"
            }));

        Assert.Equal(PasswordPolicyRules.ComplexityErrorMessage, error.Message);
        harness.UserManager.Verify(
            m => m.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Confirm_password_mismatch_is_rejected()
    {
        var harness = CreateHarness();

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            harness.Service.ChangeOwnPasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = "Current!1",
                NewPassword = StrongPassword,
                ConfirmPassword = AnotherStrongPassword
            }));

        Assert.Equal(PasswordPolicyRules.ConfirmationMismatchMessage, error.Message);
        harness.UserManager.Verify(
            m => m.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Successful_change_updates_security_stamp_and_revokes_refresh_tokens()
    {
        var harness = CreateHarness();
        var oldStamp = harness.User.SecurityStamp;
        SetupSuccessfulPasswordChecks(harness, current: "Current!1", next: StrongPassword);
        harness.UserManager
            .Setup(m => m.ChangePasswordAsync(harness.User, "Current!1", StrongPassword))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateSecurityStampAsync(harness.User))
            .Callback(() => harness.User.SecurityStamp = "stamp-after-change")
            .ReturnsAsync(IdentityResult.Success);

        await harness.Service.ChangeOwnPasswordAsync(ValidRequest());

        harness.UserManager.Verify(
            m => m.ChangePasswordAsync(harness.User, "Current!1", StrongPassword),
            Times.Once);
        harness.UserManager.Verify(m => m.UpdateSecurityStampAsync(harness.User), Times.Once);
        Assert.NotNull(harness.User.UpdatedAt);
        Assert.True(harness.RefreshToken.IsRevoked);
        Assert.NotNull(harness.RefreshToken.RevokedAt);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.False(AccessTokenSessionGate.IsAccessTokenAccepted(
            userExists: true,
            isActive: true,
            tokenSecurityStamp: oldStamp,
            userSecurityStamp: harness.User.SecurityStamp));
    }

    [Fact]
    public async Task Successful_change_clears_must_change_password_flag()
    {
        var harness = CreateHarness();
        harness.User.MustChangePassword = true;
        SetupSuccessfulPasswordChecks(harness, current: "Current!1", next: StrongPassword);
        harness.UserManager
            .Setup(m => m.ChangePasswordAsync(harness.User, "Current!1", StrongPassword))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateAsync(harness.User))
            .ReturnsAsync(IdentityResult.Success);
        harness.UserManager
            .Setup(m => m.UpdateSecurityStampAsync(harness.User))
            .ReturnsAsync(IdentityResult.Success);

        await harness.Service.ChangeOwnPasswordAsync(ValidRequest());

        Assert.False(harness.User.MustChangePassword);
        harness.UserManager.Verify(m => m.UpdateAsync(harness.User), Times.Once);
    }

    private static void SetupSuccessfulPasswordChecks(Harness harness, string current, string next)
    {
        harness.UserManager
            .Setup(m => m.CheckPasswordAsync(harness.User, current))
            .ReturnsAsync(true);
        harness.UserManager
            .Setup(m => m.CheckPasswordAsync(harness.User, next))
            .ReturnsAsync(false);
    }

    [Fact]
    public void Dto_rejects_weak_password_and_confirm_mismatch()
    {
        var weak = new ChangePasswordRequest
        {
            CurrentPassword = "Current!1",
            NewPassword = "password",
            ConfirmPassword = "password"
        };
        Assert.Contains(
            Validate(weak),
            e => e.ErrorMessage == PasswordPolicyRules.ComplexityErrorMessage);

        var mismatch = new ChangePasswordRequest
        {
            CurrentPassword = "Current!1",
            NewPassword = StrongPassword,
            ConfirmPassword = AnotherStrongPassword
        };
        Assert.Contains(
            Validate(mismatch),
            e => e.ErrorMessage == PasswordPolicyRules.ConfirmationMismatchMessage);
    }

    [Fact]
    public void Password_policy_accepts_compliant_secrets_and_rejects_overlong()
    {
        Assert.True(PasswordPolicyRules.IsCompliant(StrongPassword));
        Assert.False(PasswordPolicyRules.IsCompliant("Short1!"));
        Assert.False(PasswordPolicyRules.IsCompliant(new string('a', 129) + "A1!"));
    }

    private static ChangePasswordRequest ValidRequest(
        string current = "Current!1",
        string next = StrongPassword) =>
        new()
        {
            CurrentPassword = current,
            NewPassword = next,
            ConfirmPassword = next
        };

    private static List<ValidationResult> Validate(ChangePasswordRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    private static Harness CreateHarness()
    {
        var user = new User
        {
            Id = "user-1",
            UserName = "doctor",
            IsActive = true,
            SecurityStamp = "stamp-before-change"
        };
        var refreshToken = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "hash",
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        manager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var tokens = new Mock<IRefreshTokenRepository>();
        tokens.Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserRefreshToken> { refreshToken });
        tokens.Setup(r => r.MarkRevoked(It.IsAny<UserRefreshToken>())).Callback<UserRefreshToken>(item =>
        {
            item.IsRevoked = true;
            item.RevokedAt = DateTimeOffset.UtcNow;
        });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(user.Id, user.UserName!, "127.0.0.1");

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot());

        var service = new UserService(
            manager.Object,
            null!,
            users.Object,
            null!,
            null!,
            null!,
            tokens.Object,
            access.Object,
            uow.Object,
            Mock.Of<IPermissionGate>(),
            null!,
            currentUser,
            null!,
            null!,
            null!,
            new TestCenter(null, true));

        return new Harness(service, manager, tokens, uow, user, refreshToken);
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

    private sealed record Harness(
        UserService Service,
        Mock<UserManager<User>> UserManager,
        Mock<IRefreshTokenRepository> Tokens,
        Mock<IUnitOfWork> UnitOfWork,
        User User,
        UserRefreshToken RefreshToken);
}
