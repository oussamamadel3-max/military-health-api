using Application.Common.Localization;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Moq;

namespace Application.Tests;

public class RoleAdminOperationsServiceTests
{
    [Fact]
    public void Titles_map_role_lifecycle_and_permissions()
    {
        Assert.Equal("إنشاء الدور", Map(new AuditEvent
        {
            EventType = "Added",
            EntityType = nameof(Role),
            EntityId = "r1",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);

        Assert.Equal("تفعيل الدور", Map(new AuditEvent
        {
            EventType = "Modified",
            EntityType = nameof(Role),
            EntityId = "r1",
            ReasonText = "الخصائص التي جرى تعديل عليها: IsActive",
            NewValues = """{"IsActive":true}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);

        Assert.Equal("تعطيل الدور", Map(new AuditEvent
        {
            EventType = "Modified",
            EntityType = nameof(Role),
            EntityId = "r1",
            ReasonText = "الخصائص التي جرى تعديل عليها: IsActive",
            NewValues = """{"IsActive":false}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);

        Assert.Equal("إضافة صلاحية", Map(new AuditEvent
        {
            EventType = "Added",
            EntityType = nameof(RolePermission),
            NewValues = """{"RoleId":"r1","PermissionId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);

        Assert.Equal("إزالة صلاحية", Map(new AuditEvent
        {
            EventType = "Deleted",
            EntityType = nameof(RolePermission),
            OldValues = """{"RoleId":"r1","PermissionId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);

        Assert.Equal("تعديل صلاحيات الحقول", Map(new AuditEvent
        {
            EventType = "Added",
            EntityType = nameof(RoleFieldPermission),
            NewValues = """{"RoleId":"r1"}""",
            Result = "SUCCESS",
            OccurredAt = DateTimeOffset.UtcNow
        }).Title);
    }

    [Fact]
    public void QueryForTargetRole_isolates_role_and_includes_permission_links()
    {
        var roleId = "role-1";
        var events = new List<AuditEvent>
        {
            new()
            {
                EventId = 1,
                EventType = "Added",
                EntityType = nameof(Role),
                EntityId = roleId,
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 2,
                EventType = "Added",
                EntityType = nameof(RolePermission),
                EntityId = Guid.NewGuid().ToString(),
                NewValues = "{\"RoleId\":\"" + roleId + "\",\"PermissionId\":\"" + Guid.NewGuid() + "\"}",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 3,
                EventType = "Added",
                EntityType = nameof(Role),
                EntityId = "role-other",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            },
            new()
            {
                EventId = 4,
                EventType = "Deleted",
                EntityType = nameof(RolePermission),
                EntityId = Guid.NewGuid().ToString(),
                OldValues = "{\"RoleId\":\"" + roleId + "\"}",
                Result = "SUCCESS",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        var filtered = RoleAdminOperationsService.QueryForTargetRole(events.AsQueryable(), roleId).ToList();
        Assert.Equal(3, filtered.Count);
        Assert.DoesNotContain(filtered, e => e.EntityId == "role-other");
    }

    [Fact]
    public async Task GetForRole_requires_existing_role()
    {
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetByIdAsync("missing", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var service = new RoleAdminOperationsService(
            Mock.Of<IAuditLogRepository>(),
            roles.Object,
            Mock.Of<IUserRepository>(),
            Mock.Of<IPermissionGate>());

        await Assert.ThrowsAsync<RoleNotFoundException>(() => service.GetForRoleAsync("missing"));
    }

    private static Application.Dtos.AuthDtos.UserAdminOperationDto Map(AuditEvent e)
    {
        var (title, detail) = RoleAdminOperationTitles.TryMap(e);
        return new Application.Dtos.AuthDtos.UserAdminOperationDto
        {
            Title = title ?? string.Empty,
            Detail = detail,
            EventType = e.EventType,
            OccurredAt = e.OccurredAt
        };
    }
}
