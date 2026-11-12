using System.Net;
using System.Text.Json;
using API.Validation;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Application.Tests;

/// <summary>
/// MH-IAM-SEC-04 — تعريب Validation للدخول وتدقيق رفض IAM الحساس.
/// </summary>
public class IamSec04ValidationAndAccessDeniedAuditTests
{
    [Theory]
    [InlineData("userName", "The UserName field is required.", "الحقل اسم المستخدم مطلوب.")]
    [InlineData("passWord", "The PassWord field is required.", "الحقل كلمة المرور مطلوب.")]
    [InlineData("UserName", "The UserName field is required.", "الحقل اسم المستخدم مطلوب.")]
    public void Login_validation_messages_use_arabic_field_labels(string key, string raw, string expected)
    {
        var localized = ValidationErrorText.Localize(key, raw);
        Assert.Equal(expected, localized);
        Assert.DoesNotContain("UserName", localized, StringComparison.Ordinal);
        Assert.DoesNotContain("PassWord", localized, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestToken_data_annotations_are_arabic_without_changing_property_names()
    {
        var userName = typeof(Application.Dtos.AuthDtos.RequestToken).GetProperty("UserName")!;
        var passWord = typeof(Application.Dtos.AuthDtos.RequestToken).GetProperty("PassWord")!;
        Assert.Equal("UserName", userName.Name);
        Assert.Equal("PassWord", passWord.Name);

        var requiredUser = userName.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.RequiredAttribute>()
            .Single();
        Assert.Equal("اسم المستخدم مطلوب.", requiredUser.ErrorMessage);

        var requiredPass = passWord.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.RequiredAttribute>()
            .Single();
        Assert.Equal("كلمة المرور مطلوبة.", requiredPass.ErrorMessage);
    }

    [Fact]
    public void RequestToken_string_length_bounds_unchanged_from_pre_sec04_contract()
    {
        var userName = typeof(Application.Dtos.AuthDtos.RequestToken).GetProperty("UserName")!;
        var passWord = typeof(Application.Dtos.AuthDtos.RequestToken).GetProperty("PassWord")!;

        var userLen = userName.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.StringLengthAttribute>()
            .Single();
        Assert.Equal(3, userLen.MinimumLength);
        Assert.Equal(256, userLen.MaximumLength);

        var passLen = passWord.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.StringLengthAttribute>()
            .Single();
        Assert.Equal(5, passLen.MinimumLength);
        Assert.Equal(128, passLen.MaximumLength);

        Assert.NotNull(userName.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false).Single());
        Assert.NotNull(passWord.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false).Single());
    }

    [Theory]
    [InlineData(SystemPermissions.UserManagement.AssignPermissions, true)]
    [InlineData(SystemPermissions.RoleManagement.AssignPermissions, true)]
    [InlineData(SystemPermissions.AuditLog.Read, true)]
    [InlineData(SystemPermissions.Dashboard.Read, false)]
    [InlineData("Members.View", false)]
    public void Sensitive_access_denial_classifier_scopes_iam_only(string permission, bool expected)
    {
        Assert.Equal(expected, IamSensitiveAccessDenial.RequiresAuditForPermission(permission));
    }

    [Fact]
    public void PermissionGate_iam_denial_marks_exception_for_access_denied_audit()
    {
        var gate = CreateGateWithNoPermissions();

        var ex = Assert.Throws<ForbiddenPermissionException>(() =>
            gate.EnsureAll(SystemPermissions.UserManagement.AssignPermissions));

        Assert.True(ex.AuditAccessDenial);
        Assert.Equal("permission-missing", ex.DenialReasonCode);
        Assert.Equal("PermissionGate.EnsureAll", ex.DenialOperation);
        Assert.Contains(SystemPermissions.UserManagement.AssignPermissions, ex.DeniedPermission, StringComparison.Ordinal);
        Assert.Equal(403, ex.Status);
        Assert.Equal("FORBIDDEN_ACCESS", ex.ErrorCode);
    }

    [Fact]
    public void PermissionGate_non_iam_denial_keeps_403_without_access_denied_audit_flag()
    {
        var gate = CreateGateWithNoPermissions();

        var ex = Assert.Throws<ForbiddenPermissionException>(() =>
            gate.EnsureAll(SystemPermissions.Dashboard.Read));

        Assert.False(ex.AuditAccessDenial);
        Assert.Null(ex.DenialOperation);
        Assert.Equal(403, ex.Status);
        Assert.Equal("FORBIDDEN_ACCESS", ex.ErrorCode);
        Assert.Equal("ليس لديك الصلاحية المطلوبة.", ex.Message);
    }

    [Fact]
    public async Task Access_denied_auditor_failure_is_swallowed_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // لا AppDbContext → SaveChanges يفشل داخل TryRecordAsync ويجب ألا يُرمى للخارج.
        await using var provider = services.BuildServiceProvider();
        var auditor = new IamAccessDeniedAuditor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<IamAccessDeniedAuditor>>());

        var denial = ForbiddenPermissionException.AccessDenied(
            "رفض",
            "PermissionGate.EnsureAll",
            "permission-missing",
            SystemPermissions.UserManagement.AssignRoles);

        await auditor.TryRecordAsync(
            new TestCurrentUser(),
            new TestCenter(null, true),
            denial,
            Guid.NewGuid(),
            new Dictionary<object, object?>());
    }

    private static PermissionGate CreateGateWithNoPermissions()
    {
        var http = new DefaultHttpContext();
        http.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "actor-1") },
                "test"));

        var accessor = new HttpContextAccessor { HttpContext = http };
        var permissions = new Mock<IUserPermissionService>();
        permissions.Setup(p => p.GetPermissionNames("actor-1")).Returns(new List<string>());
        return new PermissionGate(
            accessor,
            permissions.Object,
            Mock.Of<IAccessContextRepository>(),
            Mock.Of<ICurrentCenterService>(),
            Mock.Of<ILogger<PermissionGate>>());
    }

    [Fact]
    public void Access_denied_audit_event_is_denied_without_secrets()
    {
        var current = new TestCurrentUser();
        current.SetUser("actor-1", "admin", "203.0.113.10");
        current.SetRequest("unit-test", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var denial = ForbiddenPermissionException.AccessDenied(
            "لا يمكنك إدارة مستخدم خارج نطاق مراكزك.",
            "UserManagement.ManageTarget",
            "center-scope-mismatch",
            SystemPermissions.UserManagement.AssignPermissions,
            targetUserId: "target-9");

        var evt = IamAccessDeniedAudit.Create(current, denial, current.CorrelationId);

        Assert.Equal(IamAccessDeniedAudit.EventType, evt.EventType);
        Assert.Equal("DENIED", evt.Result);
        Assert.Equal("actor-1", evt.ActorUserId);
        Assert.Equal("target-9", evt.TargetUserId);
        Assert.Equal("CENTRAL", evt.ContextScope);
        Assert.Contains("center-scope-mismatch", evt.ReasonText, StringComparison.Ordinal);
        Assert.DoesNotContain("password", evt.NewValues ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", evt.NewValues ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), evt.IPAddress);
    }

    [Fact]
    public async Task Access_denied_auditor_persists_once_per_request_and_survives_independent_scope()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUserService>(_ =>
        {
            var user = new TestCurrentUser();
            user.SetUser("actor-1", "admin", "127.0.0.1");
            return user;
        });
        services.AddScoped<ICurrentCenterService>(_ => new TestCenter(null, true));
        services.AddScoped(sp =>
        {
            var user = sp.GetRequiredService<ICurrentUserService>();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return new AppDbContext(options, user, sp.GetRequiredService<ICurrentCenterService>(), new AuditInterceptor(user));
        });

        await using var provider = services.BuildServiceProvider();
        var auditor = new IamAccessDeniedAuditor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<IamAccessDeniedAuditor>>());

        var current = new TestCurrentUser();
        current.SetUser("actor-1", "admin", "127.0.0.1");
        var items = new Dictionary<object, object?>();
        var denial = ForbiddenPermissionException.AccessDenied(
            "رفض",
            "PermissionGate.EnsureAll",
            "permission-missing",
            SystemPermissions.RoleManagement.AssignPermissions);

        await auditor.TryRecordAsync(current, new TestCenter(null, true), denial, Guid.NewGuid(), items);
        await auditor.TryRecordAsync(current, new TestCenter(null, true), denial, Guid.NewGuid(), items);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await db.AuditEvents.ToListAsync();
        Assert.Single(events);
        Assert.Equal("DENIED", events[0].Result);
        Assert.Equal(IamAccessDeniedAudit.EventType, events[0].EventType);
    }

    [Fact]
    public void Non_sensitive_forbidden_exception_is_not_audited_by_factory_flag()
    {
        var plain = new ForbiddenPermissionException("رفض عام");
        Assert.False(plain.AuditAccessDenial);
    }
}
