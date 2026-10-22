using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Application.Tests;

public class ActiveCenterSessionTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Selecting_another_center_recalculates_permissions_without_a_new_login()
    {
        var harness = new SelectHarness(previousCenterId: CenterA);
        harness.Access
            .Setup(a => a.IsActivelyAssignedAsync("user-1", CenterB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.Tokens
            .Setup(t => t.GenerateTokensAsync("user-1", CenterB))
            .ReturnsAsync(new TokenGenerationResultDto
            {
                AccessToken = "new-access",
                RefreshToken = "new-refresh",
                Roles = new List<string> { "CenterBRole" },
                Permissions = new List<string> { "CenterB.Read" }
            });
        harness.Tokens.Setup(t => t.RevokeRefreshTokenAsync("old-refresh")).ReturnsAsync(true);

        var result = await harness.Service.SelectAsync(CenterB, "old-refresh");

        Assert.Equal(new[] { "CenterB.Read" }, result.Permissions);
        Assert.Equal(new[] { "CenterBRole" }, result.Roles);
        Assert.DoesNotContain("CenterA.Read", result.Permissions);
        harness.Tokens.Verify(t => t.GenerateTokensAsync("user-1", CenterA), Times.Never);
        harness.Tokens.Verify(t => t.RevokeRefreshTokenAsync("old-refresh"), Times.Once);

        var audit = Assert.Single(harness.Audits);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Equal("CenterChange", audit.EventType);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(CenterB, audit.CenterId);
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        Assert.Contains(CenterA.ToString(), audit.OldValues);
        Assert.Contains(CenterB.ToString(), audit.NewValues);
        Assert.NotEqual(default, audit.OccurredAt);
    }

    [Fact]
    public async Task Disabled_or_unassigned_center_is_rejected_and_audited()
    {
        var harness = new SelectHarness(previousCenterId: CenterA);
        harness.Access
            .Setup(a => a.IsActivelyAssignedAsync("user-1", CenterB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => harness.Service.SelectAsync(CenterB, "old-refresh"));

        harness.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        harness.Tokens.Verify(t => t.RevokeRefreshTokenAsync(It.IsAny<string>()), Times.Never);
        var audit = Assert.Single(harness.Audits);
        Assert.Equal("FAILURE", audit.Result);
        Assert.Equal("user-1", audit.ActorUserId);
        Assert.Contains(CenterA.ToString(), audit.OldValues);
        Assert.Contains(CenterB.ToString(), audit.NewValues);
        Assert.Equal(CenterA, audit.CenterId);
    }

    [Fact]
    public async Task Client_center_without_central_authority_is_rejected()
    {
        var harness = new SelectHarness(previousCenterId: null);
        harness.Access
            .Setup(a => a.LoadAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot());

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => harness.Service.SelectAsync(null));

        harness.Tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        Assert.Equal("FAILURE", Assert.Single(harness.Audits).Result);
    }

    [Fact]
    public async Task Operational_request_without_an_active_center_is_rejected()
    {
        var harness = new SelectHarness(previousCenterId: null, central: true);

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() => harness.Service.RequireOperationalCenterAsync());

        Assert.Equal("العملية المحلية تتطلب مركزاً نشطاً.", error.Message);
    }

    [Fact]
    public async Task Revoked_assignment_cannot_be_used_for_a_local_operation()
    {
        var harness = new SelectHarness(previousCenterId: CenterA);
        harness.Access
            .Setup(a => a.IsActivelyAssignedAsync("user-1", CenterA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => harness.Service.RequireOperationalCenterAsync());
    }

    [Fact]
    public void Revoked_center_claim_drops_center_permissions()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(CenterClaimTypes.Scope, CenterClaimTypes.Center),
            new Claim(CenterClaimTypes.CenterId, CenterA.ToString()),
            new Claim("permissions", "CenterA.Read"),
            new Claim(ClaimTypes.Role, "LocalDoctor")
        }, "Bearer");

        ActiveCenterSessionGate.RetainUnscopedContext(
            identity,
            permissions: new[] { "Central.Read" },
            roles: new[] { "CentralRole" });

        Assert.Null(identity.FindFirst(CenterClaimTypes.CenterId));
        Assert.Equal(CenterClaimTypes.Center, identity.FindFirst(CenterClaimTypes.Scope)!.Value);
        Assert.DoesNotContain(identity.FindAll("permissions"), c => c.Value == "CenterA.Read");
        Assert.Contains(identity.FindAll("permissions"), c => c.Value == "Central.Read");
        Assert.Null(ActiveCenterSessionGate.ResolveClaimedCenter(CenterA, activelyAssigned: false));
        Assert.Equal(CenterB, ActiveCenterSessionGate.ResolveClaimedCenter(CenterB, activelyAssigned: true));
    }

    [Fact]
    public void Permissions_follow_the_center_that_is_still_assigned()
    {
        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(new AccessSnapshot
        {
            Roles = new List<CenterPermissionEvaluator.RoleAssignment>
            {
                new("ra", "RoleA", CenterA, true, true),
                new("rb", "RoleB", CenterB, true, true)
            },
            Links = new List<CenterPermissionEvaluator.RolePermissionLink>
            {
                new("ra", "CenterA.Read", true, true),
                new("rb", "CenterB.Read", true, true)
            }
        });
        access.Setup(a => a.IsActivelyAssignedAsync("user-1", CenterA, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        access.Setup(a => a.IsActivelyAssignedAsync("user-1", CenterB, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var atRevokedCenter = PermissionsFor(access.Object, CenterA, central: false);
        var atAssignedCenter = PermissionsFor(access.Object, CenterB, central: false);

        Assert.Empty(atRevokedCenter);
        Assert.Equal(new[] { "CenterB.Read" }, atAssignedCenter);
    }

    [Fact]
    public async Task Refresh_drops_a_center_whose_assignment_was_revoked()
    {
        var center = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var sut = CreateRefreshService(center, activelyAssigned: false);

        var result = await sut.RefreshAccessTokenAsync(RefreshTokenProtector.GenerateRawToken(), center);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == CenterClaimTypes.CenterId);
        Assert.Equal(CenterClaimTypes.Central, jwt.Claims.Single(c => c.Type == CenterClaimTypes.Scope).Value);
    }

    [Fact]
    public async Task Refresh_keeps_a_center_that_is_still_assigned()
    {
        var sut = CreateRefreshService(CenterB, activelyAssigned: true);

        var result = await sut.RefreshAccessTokenAsync(RefreshTokenProtector.GenerateRawToken(), CenterB);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.Equal(CenterB.ToString(), jwt.Claims.Single(c => c.Type == CenterClaimTypes.CenterId).Value);
        Assert.Equal(CenterClaimTypes.Center, jwt.Claims.Single(c => c.Type == CenterClaimTypes.Scope).Value);
    }

    private static IReadOnlyList<string> PermissionsFor(IAccessContextRepository access, Guid centerId, bool central)
    {
        var current = new Mock<ICurrentCenterService>();
        current.SetupGet(c => c.CenterId).Returns(centerId);
        current.SetupGet(c => c.IsCentralContext).Returns(central);
        var service = new UserPermissionService(
            Mock.Of<IUserRepository>(),
            Mock.Of<IRoleRepository>(),
            access,
            current.Object);
        return service.GetPermissionNames("user-1");
    }

    private static TokenService CreateRefreshService(Guid centerId, bool activelyAssigned)
    {
        var userId = "user-1";
        var user = new User
        {
            Id = userId,
            UserName = "user1",
            Email = "u@test.com",
            IsActive = true,
            SecurityStamp = "stamp-1"
        };
        var refreshRepo = new Mock<IRefreshTokenRepository>();
        refreshRepo
            .Setup(r => r.TryConsumeValidByTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = "hash",
                SecurityStamp = user.SecurityStamp,
                ExpiryDate = DateTime.UtcNow.AddDays(1)
            });
        refreshRepo.Setup(r => r.Add(It.IsAny<UserRefreshToken>()));

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.FindByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        userRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        userRepo.Setup(r => r.GetUserFieldPermissionDetailsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string EntityName, string FieldName, FieldAccessType AccessType)>());

        var roleRepo = new Mock<IRoleRepository>();
        roleRepo.Setup(r => r.GetRoleFieldPermissionDetailsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string EntityName, string FieldName, FieldAccessType AccessType)>());

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new AccessSnapshot
        {
            Roles = new List<CenterPermissionEvaluator.RoleAssignment>
            {
                new("r1", "Local", centerId, true, true)
            },
            Links = new List<CenterPermissionEvaluator.RolePermissionLink>
            {
                new("r1", "Local.Read", true, true)
            }
        });
        access.Setup(a => a.IsActivelyAssignedAsync(userId, centerId, It.IsAny<CancellationToken>())).ReturnsAsync(activelyAssigned);

        var tx = new Mock<IDbContextTransaction>();
        tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tx.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new TokenService(
            userRepo.Object,
            roleRepo.Object,
            refreshRepo.Object,
            access.Object,
            uow.Object,
            MsOptions.Create(new JwtSettings
            {
                Key = new string('k', 64),
                Issuer = "test-issuer",
                Audience = "test-audience",
                ExpirationInMinutes = 30,
                RefreshTokenExpirationInDays = 7
            }));
    }

    private sealed class SelectHarness
    {
        public Mock<IAccessContextRepository> Access { get; }
        public Mock<ITokenService> Tokens { get; }
        public List<AuditEvent> Audits { get; } = new();
        public ActiveCenterService Service { get; }

        public SelectHarness(Guid? previousCenterId, bool central = false)
        {
            Access = new Mock<IAccessContextRepository>();
            Tokens = new Mock<ITokenService>();
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(c => c.UserId).Returns("user-1");
            currentUser.SetupGet(c => c.IPAddress).Returns("127.0.0.1");
            currentUser.SetupGet(c => c.UserAgent).Returns("test-agent");
            currentUser.SetupGet(c => c.CorrelationId).Returns(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

            var currentCenter = new Mock<ICurrentCenterService>();
            currentCenter.SetupGet(c => c.CenterId).Returns(previousCenterId);
            currentCenter.SetupGet(c => c.IsCentralContext).Returns(central || previousCenterId is null);

            var audit = new Mock<IAuditLogRepository>();
            audit.Setup(a => a.Add(It.IsAny<AuditEvent>())).Callback<AuditEvent>(Audits.Add);
            var unit = new Mock<IUnitOfWork>();
            unit.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Service = new ActiveCenterService(
                Access.Object,
                currentUser.Object,
                currentCenter.Object,
                Tokens.Object,
                audit.Object,
                unit.Object);
        }
    }
}
