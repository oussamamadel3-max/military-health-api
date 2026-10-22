using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Application.Tests;

public class SessionLifecycleTests
{
    private const string RawRefresh = "raw-refresh-secret";
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Expired_refresh_token_is_rejected_with_the_unified_message()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var known = Row(raw, revoked: false, stamp: "stamp-1");
        known.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);

        var harness = TokenHarness.Rejecting(known);
        var error = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshAccessTokenAsync(raw));

        Assert.Equal(SessionRefresh.RejectedMessage, error.Message);
        Assert.Equal(SessionRefresh.Expired, error.ReasonCode);
        Assert.DoesNotContain(raw, error.Message, StringComparison.Ordinal);
        harness.Refresh.Verify(r => r.Add(It.IsAny<UserRefreshToken>()), Times.Never);
        harness.Refresh.Verify(
            r => r.RevokeActiveFamilyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        harness.Transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoked_or_previously_used_refresh_token_is_rejected_and_revokes_the_family()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var known = Row(raw, revoked: true, stamp: "stamp-1");
        known.RevokeReason = SessionRefresh.RotatedReason;

        var harness = TokenHarness.Rejecting(known);
        var error = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshAccessTokenAsync(raw));

        Assert.Equal(SessionRefresh.RejectedMessage, error.Message);
        Assert.Equal(SessionRefresh.Reused, error.ReasonCode);
        Assert.DoesNotContain(raw, error.Message, StringComparison.Ordinal);
        harness.Refresh.Verify(
            r => r.RevokeActiveFamilyAsync(known.TokenFamilyId, SessionRefresh.ReuseReason, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Refresh.Verify(r => r.Add(It.IsAny<UserRefreshToken>()), Times.Never);
        harness.Transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        harness.Transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Security_stamp_change_does_not_issue_a_new_session()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var consumed = Row(raw, revoked: false, stamp: "stamp-before");
        var user = ActiveUser(consumed.UserId, "stamp-after");
        var harness = TokenHarness.Consuming(consumed, user);

        var error = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshAccessTokenAsync(raw));

        Assert.Equal(SessionRefresh.RejectedMessage, error.Message);
        Assert.Equal(SessionRefresh.SecurityStamp, error.ReasonCode);
        Assert.Equal(consumed.UserId, error.SubjectUserId);
        harness.Refresh.Verify(r => r.Add(It.IsAny<UserRefreshToken>()), Times.Never);
        harness.Refresh.Verify(
            r => r.SetRevokeReasonAsync(consumed.Id, SessionRefresh.SecurityStamp, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Inactive_account_cannot_refresh()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var consumed = Row(raw, revoked: false, stamp: "stamp-1");
        var user = ActiveUser(consumed.UserId, "stamp-1");
        user.IsActive = false;
        var harness = TokenHarness.Consuming(consumed, user);

        var error = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshAccessTokenAsync(raw));

        Assert.Equal(SessionRefresh.RejectedMessage, error.Message);
        Assert.Equal(SessionRefresh.Inactive, error.ReasonCode);
        Assert.DoesNotContain("غير نشط", error.Message, StringComparison.Ordinal);
        harness.Refresh.Verify(r => r.Add(It.IsAny<UserRefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task Logout_then_refresh_is_rejected()
    {
        var raw = RefreshTokenProtector.GenerateRawToken();
        var row = Row(raw, revoked: false, stamp: "stamp-1");
        var harness = TokenHarness.ForRow(row);

        var revoked = await harness.Service.RevokeRefreshTokenAsync(raw);

        Assert.True(revoked);
        Assert.True(row.IsRevoked);
        Assert.Equal(SessionRefresh.LogoutReason, row.RevokeReason);
        Assert.Equal(RefreshTokenProtector.HashToken(raw), row.Token);
        Assert.NotEqual(raw, row.Token);

        var error = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshAccessTokenAsync(raw));

        Assert.Equal(SessionRefresh.Reused, error.ReasonCode);
        Assert.Equal(SessionRefresh.RejectedMessage, error.Message);
        harness.Refresh.Verify(
            r => r.RevokeActiveFamilyAsync(row.TokenFamilyId, SessionRefresh.ReuseReason, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Logout_revokes_the_current_token_and_audits_without_token_contents()
    {
        var harness = new AuthHarness();
        await harness.Service.LogoutAsync(RawRefresh, CenterA);

        harness.Tokens.Verify(t => t.RevokeRefreshTokenAsync(RawRefresh), Times.Once);
        var audit = Assert.Single(harness.Audits);
        Assert.Equal("Logout", audit.EventType);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal("تسجيل الخروج", audit.ReasonText);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        Assert.Equal(CenterA, audit.CenterId);
        AssertNoToken(audit, RawRefresh);
    }

    [Fact]
    public async Task Rejected_refresh_is_audited_without_token_contents_and_keeps_one_message()
    {
        var harness = new AuthHarness();
        harness.Tokens
            .Setup(t => t.RefreshAccessTokenAsync(RawRefresh, CenterA))
            .ThrowsAsync(new InvalidSessionException(SessionRefresh.RejectedMessage, SessionRefresh.Expired, "user-1"));

        var rejected = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshSessionAsync(RawRefresh, CenterA));
        var missing = await Assert.ThrowsAsync<InvalidSessionException>(() =>
            harness.Service.RefreshSessionAsync("  ", CenterA));

        Assert.Equal(SessionRefresh.RejectedMessage, rejected.Message);
        Assert.Equal(rejected.Message, missing.Message);
        Assert.Equal(2, harness.Audits.Count);
        Assert.All(harness.Audits, audit =>
        {
            Assert.Equal("Refresh", audit.EventType);
            Assert.Equal("FAILURE", audit.Result);
            Assert.Equal(CenterA, audit.CenterId);
            AssertNoToken(audit, RawRefresh);
        });
        Assert.Equal(SessionRefresh.ReasonText(SessionRefresh.Expired), harness.Audits[0].ReasonText);
        Assert.Equal("user-1", harness.Audits[0].ActorUserId);
        Assert.Equal(SessionRefresh.ReasonText(SessionRefresh.Missing), harness.Audits[1].ReasonText);
        harness.Tokens.Verify(t => t.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task Valid_refresh_returns_the_user_and_audits_success_without_token_contents()
    {
        var harness = new AuthHarness();
        harness.Tokens
            .Setup(t => t.RefreshAccessTokenAsync(RawRefresh, null))
            .ReturnsAsync(new TokenResponseDto
            {
                AccessToken = AccessTokenFor("user-1"),
                RefreshToken = "rotated-refresh"
            });

        var (tokens, userId) = await harness.Service.RefreshSessionAsync(RawRefresh);

        Assert.Equal("user-1", userId);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        var audit = Assert.Single(harness.Audits);
        Assert.Equal("Refresh", audit.EventType);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal(SessionRefresh.ReasonText(SessionRefresh.Succeeded), audit.ReasonText);
        AssertNoToken(audit, RawRefresh);
        AssertNoToken(audit, "rotated-refresh");
    }

    [Fact]
    public void Session_cookies_are_cleared_with_environment_security_flags()
    {
        var development = new DefaultHttpContext();
        Cookies(Environments.Development).SetAuthCookies(development.Response, "access-secret", RawRefresh);
        Cookies(Environments.Development).ClearAuthCookies(development.Response);
        var devCookies = development.Response.Headers.SetCookie.ToString();
        AssertDeleted(devCookies, AuthCookieNames.AccessToken);
        AssertDeleted(devCookies, AuthCookieNames.RefreshToken);
        AssertDeleted(devCookies, AuthCookieNames.CsrfSignature);
        Assert.Contains("httponly", devCookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", devCookies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", devCookies, StringComparison.OrdinalIgnoreCase);

        var production = new DefaultHttpContext();
        var productionCookies = Cookies(Environments.Production);
        productionCookies.SetAuthCookies(production.Response, "access-secret", RawRefresh);
        productionCookies.ClearAuthCookies(production.Response);
        var prodCookies = production.Response.Headers.SetCookie.ToString();
        AssertDeleted(prodCookies, AuthCookieNames.AccessToken);
        AssertDeleted(prodCookies, AuthCookieNames.RefreshToken);
        AssertDeleted(prodCookies, AuthCookieNames.CsrfSignature);
        Assert.Contains("secure", prodCookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", prodCookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", prodCookies, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertDeleted(string header, string name)
        => Assert.Contains(name + "=;", header, StringComparison.OrdinalIgnoreCase);

    private static void AssertNoToken(AuditEvent audit, string token)
    {
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
        Assert.DoesNotContain(token, audit.ReasonText ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshTokenProtector.HashToken(token), audit.ReasonText ?? "", StringComparison.Ordinal);
    }

    private static string AccessTokenFor(string userId)
    {
        var jwt = new JwtSecurityToken(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(JwtRegisteredClaimNames.Sub, userId)
        });
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static User ActiveUser(string userId, string stamp) => new()
    {
        Id = userId,
        UserName = "doctor",
        Email = "doctor@example.com",
        IsActive = true,
        SecurityStamp = stamp
    };

    private static UserRefreshToken Row(string raw, bool revoked, string stamp) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        Token = RefreshTokenProtector.HashToken(raw),
        TokenFamilyId = Guid.NewGuid(),
        SecurityStamp = stamp,
        ExpiryDate = DateTime.UtcNow.AddDays(1),
        IsRevoked = revoked
    };

    private static AuthCookieService Cookies(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);
        return new AuthCookieService(
            MsOptions.Create(new JwtSettings
            {
                ExpirationInMinutes = 15,
                RefreshTokenExpirationInDays = 7
            }),
            environment.Object);
    }

    private sealed class TokenHarness
    {
        public TokenService Service { get; }
        public Mock<IRefreshTokenRepository> Refresh { get; }
        public Mock<IUnitOfWork> UnitOfWork { get; }
        public Mock<IDbContextTransaction> Transaction { get; }

        private TokenHarness(
            TokenService service,
            Mock<IRefreshTokenRepository> refresh,
            Mock<IUnitOfWork> unitOfWork,
            Mock<IDbContextTransaction> transaction)
        {
            Service = service;
            Refresh = refresh;
            UnitOfWork = unitOfWork;
            Transaction = transaction;
        }

        public static TokenHarness Rejecting(UserRefreshToken known)
        {
            var refresh = new Mock<IRefreshTokenRepository>();
            refresh
                .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserRefreshToken?)null);
            refresh
                .Setup(r => r.GetByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(known);
            refresh
                .Setup(r => r.RevokeActiveFamilyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return Build(refresh, Mock.Of<IUserRepository>());
        }

        public static TokenHarness Consuming(UserRefreshToken consumed, User user)
        {
            var refresh = new Mock<IRefreshTokenRepository>();
            refresh
                .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(consumed);
            refresh
                .Setup(r => r.SetRevokeReasonAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var users = new Mock<IUserRepository>();
            users.Setup(r => r.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            return Build(refresh, users.Object);
        }

        public static TokenHarness ForRow(UserRefreshToken row)
        {
            var refresh = new Mock<IRefreshTokenRepository>();
            refresh
                .Setup(r => r.GetByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(row);
            refresh
                .Setup(r => r.MarkRevoked(It.IsAny<UserRefreshToken>()))
                .Callback<UserRefreshToken>(token => token.IsRevoked = true);
            refresh
                .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserRefreshToken?)null);
            refresh
                .Setup(r => r.RevokeActiveFamilyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return Build(refresh, Mock.Of<IUserRepository>());
        }

        private static TokenHarness Build(Mock<IRefreshTokenRepository> refresh, IUserRepository users)
        {
            var transaction = new Mock<IDbContextTransaction>();
            transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            transaction.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            transaction.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var access = new Mock<IAccessContextRepository>();
            access.Setup(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AccessSnapshot());

            var service = new TokenService(
                users,
                Mock.Of<IRoleRepository>(),
                refresh.Object,
                access.Object,
                unitOfWork.Object,
                MsOptions.Create(new JwtSettings
                {
                    Key = new string('k', 64),
                    Issuer = "test",
                    Audience = "test",
                    ExpirationInMinutes = 30,
                    RefreshTokenExpirationInDays = 7
                }));

            return new TokenHarness(service, refresh, unitOfWork, transaction);
        }
    }

    private sealed class AuthHarness
    {
        public AuthService Service { get; }
        public Mock<ITokenService> Tokens { get; }
        public List<AuditEvent> Audits { get; } = new();

        public AuthHarness()
        {
            var store = new Mock<IUserStore<User>>();
            var users = new Mock<UserManager<User>>(
                store.Object,
                Mock.Of<IOptions<IdentityOptions>>(),
                Mock.Of<IPasswordHasher<User>>(),
                Array.Empty<IUserValidator<User>>(),
                Array.Empty<IPasswordValidator<User>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<ILogger<UserManager<User>>>());

            var signIn = new Mock<SignInManager<User>>(
                users.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<User>>(),
                Mock.Of<IOptions<IdentityOptions>>(),
                Mock.Of<ILogger<SignInManager<User>>>(),
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<User>>());

            Tokens = new Mock<ITokenService>();
            Tokens.Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>())).ReturnsAsync(true);

            var audit = new Mock<IAuditLogRepository>();
            audit.Setup(a => a.Add(It.IsAny<AuditEvent>())).Callback<AuditEvent>(Audits.Add);
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var current = new Mock<ICurrentUserService>();
            current.SetupGet(c => c.UserId).Returns("user-1");
            current.SetupGet(c => c.IPAddress).Returns("127.0.0.1");
            current.SetupGet(c => c.UserAgent).Returns("test-agent");
            current.SetupGet(c => c.CorrelationId).Returns(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

            Service = new AuthService(
                users.Object,
                signIn.Object,
                Mock.Of<IPasswordHasher<User>>(),
                Tokens.Object,
                Mock.Of<IUserRepository>(),
                Mock.Of<IAccessContextRepository>(),
                Mock.Of<ICurrentCenterService>(),
                Mock.Of<IAttachmentRepository>(),
                Mock.Of<IMapper>(),
                Mock.Of<ILoginLockoutService>(),
                audit.Object,
                unitOfWork.Object,
                current.Object,
                Mock.Of<ILogger<AuthService>>());
        }
    }
}
