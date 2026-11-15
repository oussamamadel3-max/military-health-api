using Application.Common.Permissions;
using Domain.Entities.Auth;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic IAM fixtures for MH-SEC-051. Does not use production DbInitializer.
/// </summary>
public static class AuthTestSeed
{
    public const string ActiveUserName = "it.auth.active";
    public const string ActivePassword = "AuthT3st!Pass1";
    public const string InactiveUserName = "it.auth.inactive";
    public const string InactivePassword = "AuthT3st!Pass1";
    public const string AdminUserName = "it.auth.admin";
    public const string AdminPassword = "AuthT3st!Adm1n";
    public const string SecondUserName = "it.auth.second";
    public const string SecondPassword = "AuthT3st!Pass2";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<User>>();
        var roles = services.GetRequiredService<RoleManager<Role>>();

        var resetPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0001"),
            Name = SystemPermissions.UserManagement.ResetPassword,
            Description = "IT reset password",
            IsActive = true
        };
        var viewPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0002"),
            Name = SystemPermissions.UserManagement.ViewEffectiveAccess,
            Description = "IT view effective access",
            IsActive = true
        };
        var changeStatusPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0003"),
            Name = SystemPermissions.UserManagement.ChangeStatus,
            Description = "IT change user status",
            IsActive = true
        };
        var assignRolesPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0004"),
            Name = SystemPermissions.UserManagement.AssignRoles,
            Description = "IT assign roles",
            IsActive = true
        };
        var editUserPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0005"),
            Name = SystemPermissions.UserManagement.Edit,
            Description = "IT edit users",
            IsActive = true
        };
        var createUserPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0007"),
            Name = SystemPermissions.UserManagement.Create,
            Description = "IT create users",
            IsActive = true
        };
        var roleReadPermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee000d"),
            Name = SystemPermissions.RoleManagement.Read,
            Description = "IT read roles",
            IsActive = true
        };
        var roleCreatePermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0006"),
            Name = SystemPermissions.RoleManagement.Create,
            Description = "IT create roles",
            IsActive = true
        };
        var userCentersManagePermission = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0008"),
            Name = MilitaryHealthPermissions.UserCenters.Manage,
            Description = "IT manage user centers",
            IsActive = true
        };
        var fieldPermissionRead = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0009"),
            Name = SystemPermissions.FieldPermissionManagement.Read,
            Description = "IT field permission read",
            IsActive = true
        };
        var fieldPermissionCreate = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee000a"),
            Name = SystemPermissions.FieldPermissionManagement.Create,
            Description = "IT field permission create",
            IsActive = true
        };
        var fieldPermissionEdit = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee000b"),
            Name = SystemPermissions.FieldPermissionManagement.Edit,
            Description = "IT field permission edit",
            IsActive = true
        };
        var fieldPermissionDelete = new Permission
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee000c"),
            Name = SystemPermissions.FieldPermissionManagement.Delete,
            Description = "IT field permission delete",
            IsActive = true
        };
        db.Permissions.AddRange(
            resetPermission,
            viewPermission,
            changeStatusPermission,
            assignRolesPermission,
            editUserPermission,
            roleReadPermission,
            roleCreatePermission,
            createUserPermission,
            userCentersManagePermission,
            fieldPermissionRead,
            fieldPermissionCreate,
            fieldPermissionEdit,
            fieldPermissionDelete);

        await db.SaveChangesAsync();

        var adminRole = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = "IT-Admin",
            NormalizedName = "IT-ADMIN",
            IsActive = true,
            Scope = RoleScope.Central,
            CreatedAt = DateTime.UtcNow,
            Description = "Integration test admin"
        };
        var createRole = await roles.CreateAsync(adminRole);
        if (!createRole.Succeeded)
            throw new InvalidOperationException(string.Join("; ", createRole.Errors.Select(e => e.Description)));

        var active = await CreateUserAsync(users, ActiveUserName, "active@it.local", ActivePassword, "Active User", isActive: true);
        var inactive = await CreateUserAsync(users, InactiveUserName, "inactive@it.local", InactivePassword, "Inactive User", isActive: false);
        var admin = await CreateUserAsync(users, AdminUserName, "admin@it.local", AdminPassword, "Admin User", isActive: true);
        var second = await CreateUserAsync(users, SecondUserName, "second@it.local", SecondPassword, "Second User", isActive: true);

        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            UserId = admin.Id,
            RoleId = adminRole.Id,
            CenterId = null,
            IsActive = true,
            AssignedByUserId = admin.Id,
            AssignedAt = DateTimeOffset.UtcNow
        });

        foreach (var permissionId in new[]
                 {
                     resetPermission.Id,
                     changeStatusPermission.Id,
                     assignRolesPermission.Id,
                     editUserPermission.Id,
                     roleReadPermission.Id,
                     roleCreatePermission.Id,
                     createUserPermission.Id,
                     userCentersManagePermission.Id,
                     fieldPermissionRead.Id,
                     fieldPermissionCreate.Id,
                     fieldPermissionEdit.Id,
                     fieldPermissionDelete.Id
                 })
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = adminRole.Id,
                PermissionId = permissionId,
                IsActive = true,
                GrantedByUserId = admin.Id,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        var memberRole = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = "IT-Member",
            NormalizedName = "IT-MEMBER",
            IsActive = true,
            Scope = RoleScope.Central,
            CreatedAt = DateTime.UtcNow,
            Description = "Integration test member role"
        };
        var memberRoleCreate = await roles.CreateAsync(memberRole);
        if (!memberRoleCreate.Succeeded)
            throw new InvalidOperationException(string.Join("; ", memberRoleCreate.Errors.Select(e => e.Description)));

        foreach (var subject in new[] { active, second })
        {
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                UserId = subject.Id,
                RoleId = memberRole.Id,
                CenterId = null,
                IsActive = true,
                AssignedByUserId = admin.Id,
                AssignedAt = DateTimeOffset.UtcNow
            });
        }

        foreach (var permissionId in new[] { viewPermission.Id, createUserPermission.Id })
        {
            db.UserPermissions.Add(new UserPermission
            {
                UserId = admin.Id,
                PermissionId = permissionId,
                CenterId = null,
                IsActive = true,
                GrantedByUserId = admin.Id,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();

        // Touch ids so compilers keep locals used (inactive/second needed for lookup by name later).
        _ = inactive.Id;
        _ = second.Id;
        _ = active.Id;
    }

    private static async Task<User> CreateUserAsync(
        UserManager<User> users,
        string userName,
        string email,
        string password,
        string fullName,
        bool isActive)
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = isActive,
            FullName = fullName,
            CreationDate = DateTime.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seed user '{userName}' failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        return user;
    }
}
