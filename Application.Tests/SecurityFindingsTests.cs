using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Application.Tests;

public class RefreshTokenProtectorTests
{
    [Fact]
    public void HashToken_is_deterministic_and_not_raw()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var hash1 = RefreshTokenProtector.HashToken(raw);
        var hash2 = RefreshTokenProtector.HashToken(raw);

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(raw, hash1);
        Assert.Equal(64, hash1.Length); // SHA-256 hex
    }

    [Fact]
    public void GenerateRawToken_is_unique()
    {
        var a = RefreshTokenProtector.GenerateRawToken();
        var b = RefreshTokenProtector.GenerateRawToken();
        Assert.NotEqual(a, b);
    }
}

public class AccessTokenSessionGateTests
{
    [Fact]
    public void Stale_security_stamp_is_rejected_like_protected_endpoint()
    {
        var accepted = AccessTokenSessionGate.IsAccessTokenAccepted(
            userExists: true,
            isActive: true,
            tokenSecurityStamp: "stamp-before-invalidation",
            userSecurityStamp: "stamp-after-invalidation");

        Assert.False(accepted);
    }

    [Fact]
    public void Matching_active_stamp_is_accepted()
    {
        var accepted = AccessTokenSessionGate.IsAccessTokenAccepted(
            userExists: true,
            isActive: true,
            tokenSecurityStamp: "same",
            userSecurityStamp: "same");

        Assert.True(accepted);
    }

    [Fact]
    public void Inactive_user_is_rejected()
    {
        var accepted = AccessTokenSessionGate.IsAccessTokenAccepted(
            userExists: true,
            isActive: false,
            tokenSecurityStamp: "same",
            userSecurityStamp: "same");

        Assert.False(accepted);
    }
}

public class CreatedUserResponseDtoTests
{
    [Fact]
    public void Response_dto_has_no_password_property()
    {
        var props = typeof(CreatedUserResponseDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("password", props);
        Assert.Contains("username", props);
    }
}

public class TokenServiceRefreshTests
{
    [Fact]
    public async Task Refresh_rejects_when_token_already_consumed()
    {
        var refreshRepo = new Mock<IRefreshTokenRepository>();
        refreshRepo
            .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserRefreshToken?)null);

        var uow = CreateUnitOfWorkMock();
        var sut = CreateTokenService(refreshRepo.Object, uow.Object);

        await Assert.ThrowsAsync<InvalidSessionException>(() =>
            sut.RefreshAccessTokenAsync(RefreshTokenProtector.GenerateRawToken()));
    }

    [Fact]
    public async Task Refresh_succeeds_only_when_consume_returns_row()
    {
        var userId = Guid.NewGuid().ToString();
        var user = new User
        {
            Id = userId,
            UserName = "user1",
            Email = "u@test.com",
            IsActive = true,
            SecurityStamp = "stamp-1"
        };

        var consumed = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "hash",
            ExpiryDate = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };

        var refreshRepo = new Mock<IRefreshTokenRepository>();
        refreshRepo
            .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumed);
        refreshRepo.Setup(r => r.Add(It.IsAny<UserRefreshToken>()));

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        userRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        userRepo.Setup(r => r.GetActiveRoleNamesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "User" });
        userRepo.Setup(r => r.GetDirectPermissionNamesAsync(userId, true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        userRepo.Setup(r => r.GetActiveRoleIdsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        userRepo.Setup(r => r.GetUserFieldPermissionDetailsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>());

        var roleRepo = new Mock<IRoleRepository>();
        roleRepo.Setup(r => r.GetRolePermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermission>());
        roleRepo.Setup(r => r.GetRoleFieldPermissionDetailsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>());

        UserRefreshToken? stored = null;
        refreshRepo.Setup(r => r.Add(It.IsAny<UserRefreshToken>()))
            .Callback<UserRefreshToken>(t => stored = t);

        var uow = CreateUnitOfWorkMock();
        var sut = new TokenService(
            userRepo.Object,
            roleRepo.Object,
            refreshRepo.Object,
            uow.Object,
            MsOptions.Create(new JwtSettings
            {
                Key = new string('k', 64),
                Issuer = "test-issuer",
                Audience = "test-audience",
                ExpirationInMinutes = 30,
                RefreshTokenExpirationInDays = 7
            }));

        var raw = RefreshTokenProtector.GenerateRawToken();
        var result = await sut.RefreshAccessTokenAsync(raw);

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.NotNull(stored);
        Assert.Equal(RefreshTokenProtector.HashToken(result.RefreshToken), stored!.Token);
        Assert.NotEqual(result.RefreshToken, stored.Token);
    }

    private static TokenService CreateTokenService(IRefreshTokenRepository refreshRepo, IUnitOfWork uow)
    {
        return new TokenService(
            Mock.Of<IUserRepository>(),
            Mock.Of<IRoleRepository>(),
            refreshRepo,
            uow,
            MsOptions.Create(new JwtSettings
            {
                Key = new string('k', 64),
                Issuer = "test",
                Audience = "test",
                ExpirationInMinutes = 30,
                RefreshTokenExpirationInDays = 7
            }));
    }

    private static Mock<IUnitOfWork> CreateUnitOfWorkMock()
    {
        var tx = new Mock<IDbContextTransaction>();
        tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tx.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return uow;
    }
}
