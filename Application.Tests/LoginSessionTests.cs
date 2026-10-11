using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Options;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;

namespace Application.Tests;

public class LoginCenterContextTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Single_center_is_adopted_for_the_new_session()
    {
        var centers = new[] { Center("A", CenterA) };

        var adopted = LoginCenterContext.CenterIdForNewSession(centers);
        var decision = LoginCenterContext.Describe(centers, adopted);

        Assert.Equal(CenterA, adopted);
        Assert.Equal(LoginCenterContext.Automatic, decision.Status);
        Assert.Equal(CenterA, decision.ActiveCenter!.CenterId);
    }

    [Fact]
    public void Several_centers_require_selection_until_one_is_active()
    {
        var centers = new[] { Center("A", CenterA), Center("B", CenterB) };

        Assert.Null(LoginCenterContext.CenterIdForNewSession(centers));
        var pending = LoginCenterContext.Describe(centers, null);
        Assert.Equal(LoginCenterContext.SelectionRequired, pending.Status);
        Assert.Null(pending.ActiveCenter);

        var selected = LoginCenterContext.Describe(centers, CenterB);
        Assert.Equal(LoginCenterContext.Active, selected.Status);
        Assert.Equal(CenterB, selected.ActiveCenter!.CenterId);
    }

    [Fact]
    public void No_center_and_missing_permissions_block_operational_access()
    {
        var none = LoginCenterContext.Describe(Array.Empty<AssignedCenterDto>(), null);
        Assert.Equal(LoginCenterContext.None, none.Status);
        Assert.False(LoginCenterContext.AllowsOperationalAccess(none.Status, 0));
        Assert.Equal(LoginCenterContext.NoCenterMessage, LoginCenterContext.AccountMessage(none.Status, false));

        Assert.False(LoginCenterContext.AllowsOperationalAccess(LoginCenterContext.Automatic, 0));
        Assert.Equal(
            LoginCenterContext.NoPermissionMessage,
            LoginCenterContext.AccountMessage(LoginCenterContext.Automatic, false));
        Assert.False(LoginCenterContext.AllowsOperationalAccess(LoginCenterContext.SelectionRequired, 3));
        Assert.Null(LoginCenterContext.AccountMessage(LoginCenterContext.SelectionRequired, false));
    }

    [Fact]
    public void Stale_session_center_is_not_treated_as_active()
    {
        var centers = new[] { Center("B", CenterB) };
        var decision = LoginCenterContext.Describe(centers, CenterA);

        Assert.Equal(LoginCenterContext.SelectionRequired, decision.Status);
        Assert.Null(decision.ActiveCenter);
    }

    private static AssignedCenterDto Center(string code, Guid id) => new()
    {
        CenterId = id,
        Code = code,
        NameAr = "مركز " + code
    };
}

public class LoginSessionTests
{
    private const string Password = "Secret#123";
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Successful_login_returns_user_roles_and_effective_permissions_for_the_only_center()
    {
        var harness = new Harness(ActiveUser(), SignInResult.Success, new[] { Center("A", CenterA) });
        harness.UseEffectiveAccess(CenterA);

        var result = await harness.Service.LoginAsync(Request("doctor"));

        harness.Tokens.Verify(t => t.GenerateTokensAsync("user-1", CenterA), Times.Once);
        Assert.Equal("user-1", result.Session.UserId);
        Assert.Equal("doctor", result.Session.UserName);
        Assert.Equal("طبيب المركز", result.Session.FullName);
        Assert.Equal("doctor@example.com", result.Session.Email);
        Assert.Equal("0500000000", result.Session.Phone);
        Assert.Equal(LoginCenterContext.Automatic, result.Session.CenterStatus);
        Assert.Equal(CenterA, result.Session.ActiveCenter!.CenterId);
        Assert.True(result.Session.OperationalAccess);
        Assert.Null(result.Session.AccountMessage);
        Assert.Equal(new[] { "Doctor" }, result.Session.Roles);
        Assert.Equal(new[] { "Clinic.Read" }, result.Session.Permissions);
        Assert.DoesNotContain("Doctor", result.Session.Permissions);
        Assert.DoesNotContain("Clinic.Delete", result.Session.Permissions);
        Assert.DoesNotContain("Clinic.Hidden", result.Session.Permissions);
        AssertNoSecrets(result.Session);
        var audit = Assert.Single(harness.Audits);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal("Login", audit.EventType);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        Assert.Equal(CenterA, audit.CenterId);
        AssertNoSecrets(audit);
    }

    [Fact]
    public async Task Wrong_unknown_and_disabled_logins_share_one_message_and_create_no_session()
    {
        var unknown = new Harness(null, SignInResult.Failed, Array.Empty<AssignedCenterDto>());
        var unknownError = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            unknown.Service.LoginAsync(Request("missing-user")));

        var wrong = new Harness(ActiveUser(), SignInResult.Failed, new[] { Center("A", CenterA) });
        var wrongError = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            wrong.Service.LoginAsync(Request("doctor")));

        var disabledUser = ActiveUser();
        disabledUser.IsActive = false;
        var disabled = new Harness(disabledUser, SignInResult.Success, new[] { Center("A", CenterA) });
        var disabledError = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            disabled.Service.LoginAsync(Request("doctor")));

        Assert.Equal(unknownError.Message, wrongError.Message);
        Assert.Equal(unknownError.Message, disabledError.Message);
        Assert.DoesNotContain("missing-user", unknownError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor", wrongError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor", disabledError.Message, StringComparison.OrdinalIgnoreCase);

        unknown.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        wrong.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        disabled.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        Assert.All(unknown.Audits.Concat(wrong.Audits).Concat(disabled.Audits), audit =>
        {
            Assert.Equal("FAILURE", audit.Result);
            AssertNoSecrets(audit);
        });
        Assert.Equal("فشل تسجيل الدخول لحساب غير فعّال", disabled.Audits.Single().ReasonText);
        Assert.Equal("فشل تسجيل الدخول", wrong.Audits.Single().ReasonText);
        Assert.Equal("فشل تسجيل الدخول", unknown.Audits.Single().ReasonText);
    }

    [Fact]
    public async Task Locked_account_creates_no_session()
    {
        var harness = new Harness(ActiveUser(), SignInResult.Failed, new[] { Center("A", CenterA) }, lockedNow: true);

        var error = await Assert.ThrowsAsync<UserAccountLockedException>(() =>
            harness.Service.LoginAsync(Request("doctor")));

        Assert.Equal(30, error.RetryAfterSeconds);
        Assert.DoesNotContain("doctor", error.Message, StringComparison.OrdinalIgnoreCase);
        harness.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        harness.SignIn.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        var audit = Assert.Single(harness.Audits);
        Assert.Equal("FAILURE", audit.Result);
        Assert.Equal("فشل تسجيل الدخول بسبب القفل المؤقت", audit.ReasonText);
        AssertNoSecrets(audit);
    }

    [Fact]
    public async Task User_with_several_centers_stays_in_selection_without_an_active_center()
    {
        var harness = new Harness(
            ActiveUser(),
            SignInResult.Success,
            new[] { Center("A", CenterA), Center("B", CenterB) });
        harness.Tokens
            .Setup(t => t.GenerateTokensAsync("user-1", null))
            .ReturnsAsync(new TokenGenerationResultDto
            {
                AccessToken = "access-token-value",
                RefreshToken = "refresh-token-value",
                Roles = new List<string>(),
                Permissions = new List<string> { "Central.Read" }
            });

        var result = await harness.Service.LoginAsync(Request("doctor"));

        Assert.Equal(LoginCenterContext.SelectionRequired, result.Session.CenterStatus);
        Assert.Null(result.Session.ActiveCenter);
        Assert.Equal(2, result.Session.Centers.Count);
        Assert.False(result.Session.OperationalAccess);
        Assert.Null(result.Session.AccountMessage);
        Assert.Contains("Central.Read", result.Session.Permissions);
        AssertNoSecrets(result.Session);
        Assert.Equal(CenterClaimTypes.Central, Assert.Single(harness.Audits).ContextScope);
    }

    [Fact]
    public async Task User_without_a_center_or_operational_permissions_cannot_enter_operations()
    {
        var noCenter = new Harness(ActiveUser(), SignInResult.Success, Array.Empty<AssignedCenterDto>());
        noCenter.Tokens
            .Setup(t => t.GenerateTokensAsync("user-1", null))
            .ReturnsAsync(Issued(Array.Empty<string>(), new[] { "Central.Read" }));

        var noCenterResult = await noCenter.Service.LoginAsync(Request("doctor"));
        Assert.Equal(LoginCenterContext.None, noCenterResult.Session.CenterStatus);
        Assert.Null(noCenterResult.Session.ActiveCenter);
        Assert.False(noCenterResult.Session.OperationalAccess);
        Assert.Equal(LoginCenterContext.NoCenterMessage, noCenterResult.Session.AccountMessage);

        var noPermissions = new Harness(ActiveUser(), SignInResult.Success, new[] { Center("A", CenterA) });
        noPermissions.Tokens
            .Setup(t => t.GenerateTokensAsync("user-1", CenterA))
            .ReturnsAsync(Issued(new[] { "Doctor" }, Array.Empty<string>()));

        var noPermissionsResult = await noPermissions.Service.LoginAsync(Request("doctor"));
        Assert.Equal(LoginCenterContext.Automatic, noPermissionsResult.Session.CenterStatus);
        Assert.Equal(CenterA, noPermissionsResult.Session.ActiveCenter!.CenterId);
        Assert.False(noPermissionsResult.Session.OperationalAccess);
        Assert.Equal(LoginCenterContext.NoPermissionMessage, noPermissionsResult.Session.AccountMessage);
        Assert.Empty(noPermissionsResult.Session.Permissions);
    }

    [Fact]
    public void Login_keeps_five_attempt_lockout_and_the_login_rate_limit()
    {
        Assert.Equal(5, new LoginLockoutSettings().MaxFailedAccessAttempts);

        var root = RepoRoot();
        var settings = File.ReadAllText(Path.Combine(root, "API", "appsettings.json"));
        Assert.Contains("\"MaxFailedAccessAttempts\": 5", settings, StringComparison.Ordinal);
        Assert.Contains("\"PermitLimit\": 5", settings, StringComparison.Ordinal);
        Assert.Contains("\"WindowSeconds\": 60", settings, StringComparison.Ordinal);

        var controller = File.ReadAllText(Path.Combine(root, "API", "Controllers", "AuthControllers", "AuthController.cs"));
        Assert.Contains("[EnableRateLimiting(RateLimitPolicies.Login)]", controller, StringComparison.Ordinal);
        Assert.Contains("_cookieService.SetAuthCookies", controller, StringComparison.Ordinal);
        Assert.Contains("_csrfService.IssueCsrf", controller, StringComparison.Ordinal);
        Assert.Contains("result.Session", controller, StringComparison.Ordinal);
    }

    private static RequestToken Request(string userName) => new()
    {
        UserName = userName,
        PassWord = Password
    };

    private static User ActiveUser() => new()
    {
        Id = "user-1",
        UserName = "doctor",
        Email = "doctor@example.com",
        FullName = "طبيب المركز",
        PhoneNumber = "0500000000",
        IsActive = true
    };

    private static AssignedCenterDto Center(string code, Guid id) => new()
    {
        CenterId = id,
        Code = code,
        NameAr = "مركز " + code
    };

    private static TokenGenerationResultDto Issued(IReadOnlyList<string> roles, IReadOnlyList<string> permissions) => new()
    {
        AccessToken = "access-token-value",
        RefreshToken = "refresh-token-value",
        Roles = roles.ToList(),
        Permissions = permissions.ToList()
    };

    private static void AssertNoSecrets(LoginResponseDto session)
    {
        var json = JsonSerializer.Serialize(session);
        Assert.DoesNotContain(Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain("access-token-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-value", json, StringComparison.Ordinal);
    }

    private static void AssertNoSecrets(AuditEvent audit)
    {
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
        Assert.DoesNotContain(Password, audit.ReasonText ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("access-token-value", audit.ReasonText ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-value", audit.ReasonText ?? "", StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Backend.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class Harness
    {
        public Mock<ITokenService> Tokens { get; }
        public Mock<SignInManager<User>> SignIn { get; }
        public List<AuditEvent> Audits { get; } = new();
        public AuthService Service { get; }

        public Harness(
            User? user,
            SignInResult signInResult,
            IReadOnlyList<AssignedCenterDto> centers,
            bool lockedNow = false)
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
            users.Setup(m => m.FindByNameAsync(It.IsAny<string>())).ReturnsAsync(user);
            users.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
            users.Setup(m => m.IsLockedOutAsync(It.IsAny<User>())).ReturnsAsync(lockedNow);

            SignIn = new Mock<SignInManager<User>>(
                users.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<User>>(),
                Mock.Of<IOptions<IdentityOptions>>(),
                Mock.Of<ILogger<SignInManager<User>>>(),
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<User>>());
            SignIn
                .Setup(s => s.CheckPasswordSignInAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(signInResult);

            Tokens = new Mock<ITokenService>();
            Tokens
                .Setup(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
                .ReturnsAsync(Issued(new[] { "Doctor" }, new[] { "Clinic.Read" }));

            var access = new Mock<IAccessContextRepository>();
            access
                .Setup(a => a.GetActiveCentersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(centers);

            var lockout = new Mock<ILoginLockoutService>();
            lockout.Setup(l => l.GetRetryAfterSeconds(It.IsAny<User>())).Returns(30);
            lockout.Setup(l => l.EnsureTierDecayAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            lockout.Setup(l => l.ResetAfterSuccessfulLoginAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            lockout.Setup(l => l.ApplyProgressiveLockoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(30);

            var audit = new Mock<IAuditLogRepository>();
            audit.Setup(a => a.Add(It.IsAny<AuditEvent>())).Callback<AuditEvent>(Audits.Add);
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var current = new Mock<ICurrentUserService>();
            current.SetupGet(c => c.IPAddress).Returns("127.0.0.1");
            current.SetupGet(c => c.UserAgent).Returns("test-agent");
            current.SetupGet(c => c.CorrelationId).Returns(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

            Service = new AuthService(
                users.Object,
                SignIn.Object,
                Mock.Of<IPasswordHasher<User>>(),
                Tokens.Object,
                Mock.Of<IUserRepository>(),
                access.Object,
                Mock.Of<ICurrentCenterService>(),
                Mock.Of<IAttachmentRepository>(),
                Mock.Of<IMapper>(),
                lockout.Object,
                audit.Object,
                unitOfWork.Object,
                current.Object,
                Mock.Of<ILogger<AuthService>>());
        }

        public void UseEffectiveAccess(Guid centerId)
        {
            var roles = new[]
            {
                new CenterPermissionEvaluator.RoleAssignment("r1", "Doctor", centerId, true, true)
            };
            var directs = new[]
            {
                new CenterPermissionEvaluator.DirectAssignment("Clinic.Delete", centerId, false, true)
            };
            var links = new[]
            {
                new CenterPermissionEvaluator.RolePermissionLink("r1", "Clinic.Read", true, true),
                new CenterPermissionEvaluator.RolePermissionLink("r1", "Clinic.Hidden", false, true)
            };
            Tokens
                .Setup(t => t.GenerateTokensAsync("user-1", centerId))
                .ReturnsAsync(Issued(
                    CenterPermissionEvaluator.ApplicableRoleNames(roles, centerId),
                    CenterPermissionEvaluator.Resolve(roles, directs, links, centerId)));
        }
    }
}
