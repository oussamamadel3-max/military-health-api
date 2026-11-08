using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Moq;
using static Application.Common.Security.CenterPermissionEvaluator;

namespace Application.Tests;

public class UserAdminOperationsServiceTests
{

    [Fact]
    public void Titles_map_create_activate_password_and_skip_login()
    {
        var created = Map(new AuditEvent
        {
            EventType = "Added",
            EntityType = nameof(User),
            EntityId = "u1",
            TargetUserId = "u1",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal("إنشاء المستخدم", created.Title);

        var activated = Map(new AuditEvent
        {
            EventType = "Modified",
            EntityType = nameof(User),
            EntityId = "u1",
            TargetUserId = "u1",
            ReasonText = "الخصائص التي جرى تعديل عليها: IsActive",
            NewValues = """{"IsActive":true}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal("تفعيل الحساب", activated.Title);

        var reset = Map(new AuditEvent
        {
            EventType = UserAccountAdminEventTypes.PasswordReset,
            EntityType = nameof(User),
            EntityId = "u1",
            TargetUserId = "u1",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal("إعادة تعيين كلمة المرور", reset.Title);

        var login = UserAdminOperationTitles.TryMap(new AuditEvent
        {
            EventType = "Login",
            EntityType = nameof(User),
            EntityId = "u1",
            TargetUserId = "u1",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        });
        Assert.Null(login.Title);
    }

    [Fact]
    public void QueryForTargetUser_keeps_target_linked_events_only()
    {
        var targetId = "target-1";
        var events = new List<AuditEvent>
        {
            new()
            {
                EventId = 1,
                EventType = "Added",
                EntityType = nameof(User),
                EntityId = targetId,
                TargetUserId = targetId,
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 2,
                EventType = UserAccountAdminEventTypes.PasswordReset,
                EntityType = nameof(User),
                EntityId = targetId,
                TargetUserId = targetId,
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 3,
                EventType = "Added",
                EntityType = nameof(User),
                EntityId = "other",
                TargetUserId = "other",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        var filtered = UserAdminOperationsService.QueryForTargetUser(events.AsQueryable(), targetId).ToList();
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, e => Assert.Equal(targetId, e.TargetUserId));
    }

    [Fact]
    public void QueryForTargetUser_matches_legacy_json_payload_without_target_user_id()
    {
        var targetId = "legacy-user";
        var events = new List<AuditEvent>
        {
            new()
            {
                EventId = 10,
                EventType = "Added",
                EntityType = nameof(UserRoleAssociation),
                EntityId = Guid.NewGuid().ToString(),
                NewValues = "{\"UserId\":\"" + targetId + "\",\"RoleId\":\"r1\"}",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 11,
                EventType = "Added",
                EntityType = nameof(UserRoleAssociation),
                EntityId = Guid.NewGuid().ToString(),
                NewValues = "{\"UserId\":\"other\",\"RoleId\":\"r1\"}",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        var filtered = UserAdminOperationsService.QueryForTargetUser(events.AsQueryable(), targetId).ToList();
        Assert.Equal(10, Assert.Single(filtered).EventId);
    }

    [Fact]
    public async Task GetForUser_rejects_without_manage_scope()
    {
        var audit = new Mock<IAuditLogRepository>();
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.FindByIdAsync("target-1"))
            .ReturnsAsync(new User { Id = "target-1", UserName = "t" });
        users.Setup(u => u.SharesActiveCenterAsync("actor-1", "target-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles = new List<RoleAssignment>
                {
                    new("r1", "CenterManager", Guid.NewGuid(), true, true)
                }
            });

        var gate = new Mock<IPermissionGate>();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns("actor-1");

        var service = new UserAdminOperationsService(
            audit.Object, users.Object, access.Object, gate.Object, current.Object);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetForUserAsync("target-1"));
    }

    private static UserAdminOperationDto Map(AuditEvent e)
    {
        var (title, detail) = UserAdminOperationTitles.TryMap(e);
        return new UserAdminOperationDto
        {
            Title = title ?? string.Empty,
            Detail = detail,
            EventType = e.EventType,
            OccurredAt = e.OccurredAt
        };
    }
}
