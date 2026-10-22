using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace Application.Tests;

public class ServiceProtectionReferenceTests
{
    private const string LocalUpdate = "Member.Update";
    private const string CentralRead = "Reports.Read";
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task User_without_a_session_is_unauthorized()
    {
        var gate = GateFor(new DefaultHttpContext(), new AccessSnapshot(), centerId: CenterA, central: false);

        var error = await Assert.ThrowsAsync<UnauthenticatedUserException>(() =>
            gate.EnsureLocalAsync(LocalUpdate));
        var missingSession = Assert.Throws<UnauthenticatedUserException>(() => gate.EnsureAll(LocalUpdate));

        Assert.Equal(401, error.Status);
        Assert.Equal(401, missingSession.Status);
        Assert.Equal("UNAUTHORIZED_ACCESS", error.ErrorCode);
    }

    [Fact]
    public async Task Authenticated_user_without_the_permission_is_forbidden()
    {
        var harness = Harness.Local(CenterA, Snapshot());

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate));

        AssertForbidden(error, LocalUpdate);
        Assert.Contains(harness.Logger.Messages, message => message.Contains("permission-missing"));
        Assert.DoesNotContain(harness.Logger.Messages, message => message.Contains("ليس لديك الصلاحية المطلوبة."));
    }

    [Fact]
    public async Task Permission_for_another_center_is_forbidden()
    {
        var harness = Harness.Local(CenterA, Snapshot(
            Role("local", "Doctor", CenterB),
            Link("local", LocalUpdate)));

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate));

        AssertForbidden(error, LocalUpdate);
        Assert.DoesNotContain(CenterB.ToString(), error.Message);
    }

    [Fact]
    public async Task Local_operation_uses_the_session_center()
    {
        var harness = Harness.Local(CenterA, Snapshot(
            Role("local", "Doctor", CenterA),
            Link("local", LocalUpdate)));
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString($"?centerId={CenterA}");

        var centerId = await harness.Gate.EnsureLocalAsync(LocalUpdate, ClientCenterBinding.Read(http.Request));

        Assert.Equal(CenterA, centerId);
        Assert.Empty(harness.Logger.Levels);
    }

    [Fact]
    public async Task Client_center_is_not_trusted()
    {
        var harness = Harness.Local(CenterA, Snapshot(
            Role("local", "Doctor", CenterA),
            Link("local", LocalUpdate),
            Role("other", "Doctor", CenterB),
            Link("other", LocalUpdate)));
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString($"?centerId={CenterB}");
        http.Request.RouteValues["centerId"] = CenterA.ToString();

        Assert.Equal(Guid.Empty, ClientCenterBinding.Read(http.Request));

        http.Request.RouteValues.Clear();
        var requested = ClientCenterBinding.Read(http.Request);
        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate, requested));

        AssertForbidden(error, LocalUpdate);
        Assert.Contains(harness.Logger.Messages, message => message.Contains("client-center-mismatch"));
        harness.Access.Verify(a => a.IsActivelyAssignedAsync("user-1", CenterB, It.IsAny<CancellationToken>()), Times.Never);
        harness.Access.Verify(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Local_operation_without_an_active_center_is_forbidden()
    {
        var harness = Harness.Central(Snapshot(
            Role("central", "CentralAdmin", null),
            Link("central", LocalUpdate)));

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate, CenterA));

        AssertForbidden(error, LocalUpdate);
        Assert.Contains(harness.Logger.Messages, message => message.Contains("no-active-center"));
        harness.Access.Verify(
            a => a.IsActivelyAssignedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Central_permission_does_not_grant_a_local_update()
    {
        var harness = Harness.Local(CenterA, Snapshot(
            Role("central", "CentralAdmin", null),
            Link("central", CentralRead)));

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate));

        AssertForbidden(error, LocalUpdate);
    }

    [Fact]
    public async Task Central_operation_succeeds_from_an_unscoped_permission()
    {
        var harness = Harness.Central(Snapshot(
            Role("central", "CentralAdmin", null),
            Link("central", CentralRead)));

        await harness.Gate.EnsureCentralAsync(CentralRead);

        Assert.Empty(harness.Logger.Levels);
        harness.Access.Verify(
            a => a.IsActivelyAssignedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Center_scoped_permission_does_not_authorize_a_central_operation()
    {
        var harness = Harness.Local(CenterA, Snapshot(
            Role("local", "Doctor", CenterA),
            Link("local", CentralRead)));

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureCentralAsync(CentralRead));

        AssertForbidden(error, CentralRead);
        Assert.Equal(CenterA, await harness.Gate.EnsureLocalAsync(CentralRead));
    }

    [Fact]
    public async Task Role_name_alone_does_not_authorize()
    {
        var harness = Harness.Local(CenterA, Snapshot(Role("admin", "SuperAdmin", null)));

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureCentralAsync(CentralRead));

        AssertForbidden(error, CentralRead);
        Assert.DoesNotContain("SuperAdmin", error.Message);
    }

    [Fact]
    public async Task Inactive_role_and_permission_are_rejected()
    {
        var inactiveRole = Harness.Central(Snapshot(
            Role("central", "CentralAdmin", null, roleActive: false),
            Link("central", CentralRead)));
        var inactivePermission = Harness.Central(Snapshot(
            Role("central", "CentralAdmin", null),
            Link("central", CentralRead, permissionActive: false)));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => inactiveRole.Gate.EnsureCentralAsync(CentralRead));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => inactivePermission.Gate.EnsureCentralAsync(CentralRead));
    }

    [Fact]
    public async Task Revoked_assignment_rejects_the_local_operation()
    {
        var harness = Harness.Local(
            CenterA,
            Snapshot(Role("local", "Doctor", CenterA), Link("local", LocalUpdate)),
            assigned: false);

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Gate.EnsureLocalAsync(LocalUpdate));

        AssertForbidden(error, LocalUpdate);
        Assert.Contains(harness.Logger.Messages, message => message.Contains("assignment-inactive"));
        harness.Access.Verify(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Mutation_without_csrf_is_rejected()
    {
        var csrf = new Mock<ICsrfService>();

        var missing = Assert.Throws<CsrfTokenMissingException>(() =>
            CsrfMutationGuard.Enforce(
                "POST",
                "/api/reference/local",
                "user-1",
                headerPresent: false,
                headerToken: null,
                signature: null,
                csrf.Object));

        Assert.Equal(403, missing.Status);
        Assert.Equal("CSRF_TOKEN_MISSING", missing.ErrorCode);
        csrf.Verify(c => c.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        var jwt = Microsoft.Extensions.Options.Options.Create(new JwtSettings { Key = "reference-test-key" });
        var environment = new Mock<IWebHostEnvironment>();
        var service = new CsrfService(jwt, new AuthCookieService(jwt, environment.Object));
        Assert.False(service.Validate("user-1", "token", null));
        Assert.True(service.Validate("user-1", "token", service.ComputeSignature("user-1", "token")));

        CsrfMutationGuard.Enforce("GET", "/api/reference/local", null, false, null, null, csrf.Object);
        CsrfMutationGuard.Enforce("POST", "/api/Auth/login", null, false, null, null, csrf.Object);

        var unauthenticated = Assert.Throws<UnauthenticatedUserException>(() =>
            CsrfMutationGuard.Enforce("POST", "/api/reference/local", null, true, "token", "sig", csrf.Object));
        Assert.Equal(401, unauthenticated.Status);
    }

    private static void AssertForbidden(ForbiddenPermissionException error, string permission)
    {
        Assert.Equal(403, error.Status);
        Assert.Equal("FORBIDDEN_ACCESS", error.ErrorCode);
        Assert.Equal("ليس لديك الصلاحية المطلوبة.", error.Message);
        Assert.DoesNotContain(permission, error.Message);
    }

    private static PermissionGate GateFor(
        HttpContext http,
        AccessSnapshot snapshot,
        Guid? centerId,
        bool central)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var center = new Mock<ICurrentCenterService>();
        center.SetupGet(c => c.CenterId).Returns(centerId);
        center.SetupGet(c => c.IsCentralContext).Returns(central);
        return new PermissionGate(
            accessor.Object,
            Mock.Of<IUserPermissionService>(),
            access.Object,
            center.Object,
            new CaptureLogger<PermissionGate>());
    }

    private static AccessSnapshot Snapshot(
        params object[] entries)
    {
        var snapshot = new AccessSnapshot();
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case CenterPermissionEvaluator.RoleAssignment role:
                    snapshot.Roles.Add(role);
                    break;
                case CenterPermissionEvaluator.RolePermissionLink link:
                    snapshot.Links.Add(link);
                    break;
            }
        }

        return snapshot;
    }

    private static CenterPermissionEvaluator.RoleAssignment Role(
        string id,
        string name,
        Guid? center,
        bool roleActive = true)
        => new(id, name, center, true, roleActive);

    private static CenterPermissionEvaluator.RolePermissionLink Link(
        string roleId,
        string permission,
        bool permissionActive = true)
        => new(roleId, permission, true, permissionActive);

    private sealed class Harness
    {
        public CaptureLogger<PermissionGate> Logger { get; } = new();
        public Mock<IAccessContextRepository> Access { get; } = new();
        public PermissionGate Gate { get; }

        private Harness(AccessSnapshot snapshot, Guid? centerId, bool central, bool assigned)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "user-1"),
                        new Claim(ClaimTypes.Role, "SuperAdmin")
                    },
                    "test"))
            };
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(http);
            var center = new Mock<ICurrentCenterService>();
            center.SetupGet(c => c.CenterId).Returns(centerId);
            center.SetupGet(c => c.IsCentralContext).Returns(central);
            Access.Setup(a => a.LoadAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            if (centerId is Guid id)
            {
                Access.Setup(a => a.IsActivelyAssignedAsync("user-1", id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(assigned);
            }

            Gate = new PermissionGate(
                accessor.Object,
                Mock.Of<IUserPermissionService>(),
                Access.Object,
                center.Object,
                Logger);
        }

        public static Harness Local(Guid centerId, AccessSnapshot snapshot, bool assigned = true)
            => new(snapshot, centerId, central: false, assigned);

        public static Harness Central(AccessSnapshot snapshot)
            => new(snapshot, centerId: null, central: true, assigned: false);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = new();
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
