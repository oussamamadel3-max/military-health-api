using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

public class RoleAssignmentPathTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Password = "Abcdef1!";

    [Fact]
    public async Task Create_binds_operational_role_to_active_center_without_direct_permissions()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        var roleId = await SeedRoleAsync(db, "ClinicDoctor");
        await SeedRolePermissionAsync(db, roleId, "Clinical.ExamWrite");

        var harness = CreateUserService(db, central: false);
        var created = await harness.Service.Create(NewUser("clinic.user", "ClinicDoctor"));

        var link = await db.UserRoleAssociations.SingleAsync(x => x.UserId == "created-1");
        Assert.Equal(CenterA, created.centerId);
        Assert.Equal(CenterA, link.CenterId);
        Assert.True(await db.UserCenters.AnyAsync(x => x.UserId == "created-1" && x.CenterId == CenterA && x.IsActive));
        Assert.Equal(0, await db.UserPermissions.CountAsync(x => x.UserId == "created-1"));
        Assert.Equal("EMP-clinic.user", created.employeeNumber);
        Assert.Equal("موظف", created.rankOrTitle);

        var profile = await db.UserProfiles.SingleAsync(p => p.UserId == "created-1");
        Assert.Equal("EMP-clinic.user", profile.EmployeeNumber);
        Assert.Equal("موظف", profile.RankOrTitle);
        Assert.Equal("actor-1", profile.UpdatedByUserId);

        var listed = await harness.Service.GetUserAsync("created-1");
        Assert.Single(listed.centers);
        Assert.Equal(CenterA, listed.centers[0].CenterId);
        Assert.Null(listed.lastLoginAt);

        var access = new AccessContextRepository(db);
        var snapshot = await access.LoadAsync("created-1");
        var inA = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, CenterA);
        var inB = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, CenterB);
        Assert.Contains("Clinical.ExamWrite", inA);
        Assert.DoesNotContain("Clinical.ExamWrite", inB);
    }

    [Fact]
    public async Task Create_rejects_duplicate_employee_number()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "existing", "existing");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        await SeedRoleAsync(db, "ClinicDoctor");
        db.UserProfiles.Add(new UserProfile
        {
            UserId = "existing",
            EmployeeNumber = "EMP-dup",
            RankOrTitle = "نقيب",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var harness = CreateUserService(db, central: false);
        var dto = NewUser("new.user", "ClinicDoctor");
        dto.employeeNumber = "EMP-dup";

        var error = await Assert.ThrowsAsync<InvalidUserDataException>(() => harness.Service.Create(dto));
        Assert.Contains("الرقم الوظيفي", error.Message);
        Assert.Equal(0, await db.Users.CountAsync(u => u.UserName == "new.user"));
    }

    [Fact]
    public async Task Create_inactive_user_defers_center_membership_without_active_link()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        await SeedRoleAsync(db, "ReceptionClerk");

        var harness = CreateUserService(db, central: false);
        var dto = NewUser("inactive.clerk", "ReceptionClerk");
        dto.isActive = false;

        var created = await harness.Service.Create(dto);

        Assert.False(created.isActive);
        Assert.Equal(CenterA, created.centerId);
        var user = await db.Users.SingleAsync(u => u.Id == "created-1");
        Assert.False(user.IsActive);

        var membership = await db.UserCenters.SingleAsync(uc => uc.UserId == "created-1" && uc.CenterId == CenterA);
        Assert.False(membership.IsActive);
        Assert.Equal(IamRetention.AccountCreatedInactive, membership.RevokeReason);
        Assert.NotNull(membership.RevokedAt);
        Assert.Equal("actor-1", membership.RevokedByUserId);
        Assert.False(await db.UserCenters.AnyAsync(uc =>
            uc.UserId == "created-1" && uc.IsActive));
    }

    [Fact]
    public async Task Activate_inactive_created_user_reactivates_deferred_center_membership()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        await SeedRoleAsync(db, "ReceptionClerk");

        var harness = CreateUserService(db, central: false);
        var dto = NewUser("pending.clerk", "ReceptionClerk");
        dto.isActive = false;
        await harness.Service.Create(dto);

        harness.UserManager
            .Setup(m => m.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var status = await harness.Service.UpdateUserStatusAsync(
            "created-1",
            new UpdateUserStatusRequest { isActive = true });

        Assert.True(status.isActive);
        var user = await db.Users.SingleAsync(u => u.Id == "created-1");
        Assert.True(user.IsActive);

        var membership = await db.UserCenters.SingleAsync(uc => uc.UserId == "created-1" && uc.CenterId == CenterA);
        Assert.True(membership.IsActive);
        Assert.Null(membership.RevokedAt);
        Assert.Null(membership.RevokedByUserId);
        Assert.True(string.IsNullOrEmpty(membership.RevokeReason));
    }

    [Fact]
    public async Task Get_and_update_return_and_persist_job_fields()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        db.UserProfiles.Add(new UserProfile
        {
            UserId = "target-1",
            EmployeeNumber = "EMP-old",
            RankOrTitle = "ممرض",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var harness = CreateUserService(db, central: false);
        var before = await harness.Service.GetUserAsync("target-1");
        Assert.Equal("EMP-old", before.employeeNumber);
        Assert.Equal("ممرض", before.rankOrTitle);

        var updated = await harness.Service.UpdateUserAsync("target-1", new UpdateUserPatchRequest
        {
            fullName = "ممرض محدث",
            username = "nurse",
            email = "nurse@example.com",
            employeeNumber = "EMP-new",
            rankOrTitle = "طبيب عظمية"
        });

        Assert.Equal("EMP-new", updated.employeeNumber);
        Assert.Equal("طبيب عظمية", updated.rankOrTitle);
        var profile = await db.UserProfiles.SingleAsync(p => p.UserId == "target-1");
        Assert.Equal("EMP-new", profile.EmployeeNumber);
        Assert.Equal("طبيب عظمية", profile.RankOrTitle);
    }

    [Fact]
    public async Task Create_rejects_unlisted_role_before_inserting_the_user()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedRoleAsync(db, "CenterOfficer");

        var harness = CreateUserService(db, central: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.Create(NewUser("officer.user", "CenterOfficer")));

        harness.UserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        Assert.False(await db.Users.AnyAsync(u => u.UserName == "officer.user"));
    }

    [Fact]
    public async Task Create_rejects_inactive_center_and_center_outside_active_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedCenterAsync(db, CenterB, false);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedRoleAsync(db, "ReceptionClerk");
        var harness = CreateUserService(db, central: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.Create(NewUser("other.user", "ReceptionClerk", CenterB)));

        var active = await db.MedicalCenters.SingleAsync(c => c.CenterId == CenterA);
        active.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InactiveMedicalCenterException>(() =>
            harness.Service.Create(NewUser("inactive.user", "ReceptionClerk")));
    }

    [Fact]
    public async Task Create_rolls_back_when_saving_the_assignment_fails()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        await SeedRoleAsync(db, "ArchiveClerk");

        var uow = new RollbackTrackingUnitOfWork(db) { FailOnSave = true };
        var harness = CreateUserService(db, central: false, uow);
        harness.UserManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync((User user, string _) =>
            {
                if (!uow.Began)
                    throw new InvalidOperationException("أُنشئ المستخدم خارج المعاملة.");
                user.Id = "created-1";
                user.SecurityStamp = "stamp";
                db.Users.Add(user);
                return IdentityResult.Success;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.Create(NewUser("archive.user", "ArchiveClerk")));

        Assert.True(uow.Began);
        Assert.True(uow.RolledBack);
        Assert.False(uow.Committed);
    }

    [Fact]
    public async Task Toggle_patch_and_update_with_roles_share_the_active_center_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedCenterAsync(db, CenterB, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        var doctor = await SeedRoleAsync(db, "ClinicDoctor");
        var clerk = await SeedRoleAsync(db, "ReceptionClerk");
        var supervisor = await SeedRoleAsync(db, "Supervisor");
        await SeedRoleLinkAsync(db, "target-1", "Supervisor", CenterB);
        await SeedRolePermissionAsync(db, doctor, "Clinical.ExamWrite");

        var harness = CreateUserService(db, central: false);

        await harness.Service.ToggleRolesForUserAsync(new AssignRolesDto
        {
            UserId = "target-1",
            RoleIds = [doctor]
        });

        await harness.Service.UpdateUserAsync("target-1", new UpdateUserPatchRequest
        {
            fullName = "مستخدم",
            username = "nurse",
            email = "nurse@example.com",
            employeeNumber = "EMP-nurse",
            rankOrTitle = "موظف استقبال",
            roles = [new RoleLiteDto(clerk, "ReceptionClerk")]
        });

        await harness.Service.UpdateUserWithRolesAsync("target-1", new UpdateUserWithRolesDto
        {
            UserName = "nurse",
            Email = "nurse@example.com",
            RoleIds = [supervisor],
            CenterId = CenterA
        });

        var links = await db.UserRoleAssociations.Where(x => x.UserId == "target-1" && x.IsActive).ToListAsync();
        var nurse = await db.Users.SingleAsync(u => u.Id == "target-1");
        Assert.NotNull(nurse.UpdatedAt);
        Assert.Contains(links, x => x.RoleId == supervisor && x.CenterId == CenterA);
        Assert.Contains(links, x => x.RoleId == supervisor && x.CenterId == CenterB);
        Assert.DoesNotContain(links, x => x.CenterId == null);
        Assert.Equal(0, await db.UserPermissions.CountAsync(x => x.UserId == "target-1"));
    }

    [Fact]
    public async Task Local_paths_reject_central_and_unlisted_roles_and_users_outside_scope()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedCenterAsync(db, CenterB, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserAsync(db, "outsider", "outsider");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedUserCenterAsync(db, "outsider", CenterB);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        var superAdmin = await SeedRoleAsync(db, "SuperAdmin");
        var officer = await SeedRoleAsync(db, "CenterOfficer");
        await SeedRoleAsync(db, "ReceptionClerk");

        var harness = CreateUserService(db, central: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.ToggleRolesForUserAsync(new AssignRolesDto
            {
                UserId = "target-1",
                RoleIds = [superAdmin]
            }));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.UpdateUserAsync("target-1", new UpdateUserPatchRequest
            {
                username = "nurse",
                email = "nurse@example.com",
                employeeNumber = "EMP-nurse",
                rankOrTitle = "موظف",
                roles = [new RoleLiteDto(officer, "CenterOfficer")]
            }));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            harness.Service.UpdateUserWithRolesAsync("outsider", new UpdateUserWithRolesDto
            {
                UserName = "outsider",
                Email = "outsider@example.com",
                RoleIds = ["ReceptionClerk"]
            }));

        Assert.Equal(0, await db.UserRoleAssociations.CountAsync(x => x.UserId == "target-1" || x.UserId == "outsider"));
    }

    [Fact]
    public async Task Direct_permission_grant_still_blocks_permissions_the_actor_does_not_hold()
    {
        var target = new User { Id = "target-1", UserName = "nurse", IsActive = true, SecurityStamp = "stamp" };
        var permissionId = Guid.NewGuid();
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.SharesActiveCenterAsync("actor-1", target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        users.Setup(r => r.GetDirectPermissionIdsAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        users.Setup(r => r.GetActiveDirectPermissionIdsAsync(target.Id, CenterA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetActiveIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { permissionId });
        permissions.Setup(r => r.GetActiveByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission>
            {
                new() { Id = permissionId, Name = "Clinical.ExamWrite", IsActive = true }
            });

        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync("actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                Roles = { new CenterPermissionEvaluator.RoleAssignment("r1", "CenterManager", CenterA, true, true) }
            });
        access.Setup(a => a.LoadAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot());
        access.Setup(a => a.IsActivelyAssignedAsync(target.Id, CenterA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(target.Id)).ReturnsAsync(target);
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new[] { SystemPermissions.UserManagement.AssignPermissions });
        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "manager", "127.0.0.1");

        var service = new UserService(
            manager.Object, null!, users.Object, Mock.Of<IRoleRepository>(), permissions.Object, null!,
            Mock.Of<IRefreshTokenRepository>(), access.Object, Mock.Of<IUnitOfWork>(), gate.Object, null!,
            actor, Mock.Of<ITextInputSanitizer>(), Mock.Of<IMapper>(), Mock.Of<IMedicalCenterRepository>(),
            new TestCenter(CenterA, false), Mock.Of<IAuditLogRepository>());

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = target.Id,
                PermissionId = [permissionId]
            }));

        users.Verify(r => r.AddUserPermissions(It.IsAny<IEnumerable<UserPermission>>()), Times.Never);
    }

    [Fact]
    public async Task Direct_permission_toggle_is_limited_to_the_requested_center()
    {
        await using var db = CreateDb();
        await SeedCenterAsync(db, CenterA, true);
        await SeedCenterAsync(db, CenterB, true);
        await SeedUserAsync(db, "actor-1", "manager");
        await SeedUserAsync(db, "target-1", "nurse");
        await SeedUserCenterAsync(db, "actor-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterA);
        await SeedUserCenterAsync(db, "target-1", CenterB);
        await SeedRoleLinkAsync(db, "actor-1", "CenterManager", CenterA);
        await SeedIamKeeperAsync(db);
        var permissionId = await SeedPermissionAsync(db, "Members.View");

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(new[] { "Members.View", SystemPermissions.UserManagement.AssignPermissions });
        var local = CreateUserService(db, central: false, gate: gate.Object);

        await local.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [permissionId]
        });

        var granted = await db.UserPermissions.SingleAsync(p => p.UserId == "target-1" && p.IsActive);
        Assert.Equal(permissionId, granted.PermissionId);
        Assert.Equal(CenterA, granted.CenterId);
        Assert.Null(granted.RevokeReason);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            local.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
            {
                UserId = "target-1",
                PermissionId = [permissionId],
                CenterId = CenterB
            }));

        await local.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [permissionId],
            CenterId = CenterA
        });

        var revoked = await db.UserPermissions.SingleAsync(p => p.UserId == "target-1" && p.PermissionId == permissionId);
        Assert.False(revoked.IsActive);
        Assert.Equal(IamRetention.DirectPermissionWithdrawn, revoked.RevokeReason);
        Assert.Equal(CenterA, revoked.CenterId);

        await SeedRoleLinkAsync(db, "actor-1", "SuperAdmin", null);
        var centralGate = new Mock<IPermissionGate>();
        centralGate.Setup(g => g.GetPermissions()).Returns(new[] { "Members.View" });
        var central = CreateUserService(db, central: true, gate: centralGate.Object);
        await central.Service.TogglePermissionsForUserAsync(new AssignPermissionToUserDto
        {
            UserId = "target-1",
            PermissionId = [permissionId]
        });

        var centralGrant = await db.UserPermissions.SingleAsync(p =>
            p.UserId == "target-1" && p.IsActive && p.CenterId == null);
        Assert.Equal(permissionId, centralGrant.PermissionId);
        Assert.Equal(CenterA, revoked.CenterId);
    }

    private static CreateUserDto NewUser(string username, string role, Guid? centerId = null) => new()
    {
        fullName = "مستخدم جديد",
        username = username,
        email = $"{username}@example.com",
        employeeNumber = $"EMP-{username}",
        rankOrTitle = "موظف",
        password = Password,
        confirmPassword = Password,
        roles = [role],
        centerId = centerId
    };

    private static UserHarness CreateUserService(
        AppDbContext db,
        bool central,
        IUnitOfWork? uow = null,
        IPermissionGate? gate = null)
    {
        var manager = CreateUserManager();
        manager.Setup(m => m.FindByNameAsync(It.IsAny<string>()))
            .Returns((string name) => db.Users.FirstOrDefaultAsync(u => u.UserName == name));
        manager.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .Returns((string email) => db.Users.FirstOrDefaultAsync(u => u.Email == email));
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));
        manager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync((User user, string _) =>
            {
                user.Id = "created-1";
                user.SecurityStamp = "stamp";
                db.Users.Add(user);
                return IdentityResult.Success;
            });
        manager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        manager.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<User>(It.IsAny<CreateUserDto>()))
            .Returns((CreateUserDto dto) => new User
            {
                UserName = dto.username,
                Email = dto.email,
                FullName = dto.fullName
            });
        mapper.Setup(m => m.Map(It.IsAny<UpdateUserPatchRequest>(), It.IsAny<User>()));
        mapper.Setup(m => m.Map<UserListItemDto>(It.IsAny<User>()))
            .Returns((User user) => new UserListItemDto
            {
                userId = user.Id,
                username = user.UserName ?? string.Empty,
                email = user.Email ?? string.Empty
            });
        mapper.Setup(m => m.Map<List<RoleLiteDto>>(It.IsAny<object>())).Returns(new List<RoleLiteDto>());
        mapper.Setup(m => m.Map<List<PermissionLiteDto>>(It.IsAny<object>())).Returns(new List<PermissionLiteDto>());

        var sanitizer = new Mock<ITextInputSanitizer>();
        sanitizer.Setup(s => s.SanitizePlainText(It.IsAny<string?>())).Returns((string? value) => value);

        var actor = new TestCurrentUser();
        actor.SetUser("actor-1", "manager", "127.0.0.1");
        var service = new UserService(
            manager.Object,
            null!,
            new UserRepository(db),
            new RoleRepository(db),
            new PermissionRepository(db),
            null!,
            new RefreshTokenRepository(db),
            new AccessContextRepository(db),
            uow ?? new SavingUnitOfWork(db),
            gate ?? Mock.Of<IPermissionGate>(),
            null!,
            actor,
            sanitizer.Object,
            mapper.Object,
            new MedicalCenterRepository(db),
            new TestCenter(central ? null : CenterA, central),
            Mock.Of<IAuditLogRepository>());

        return new UserHarness(service, manager);
    }

    private static async Task SeedIamKeeperAsync(AppDbContext db)
    {
        await SeedUserAsync(db, "iam-keeper", "keeper");
        var roleId = await SeedRoleAsync(db, "SuperAdmin");
        if (!await db.UserRoleAssociations.AnyAsync(x => x.UserId == "iam-keeper" && x.RoleId == roleId))
        {
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = "iam-keeper",
                RoleId = roleId,
                CenterId = null,
                IsActive = true,
                AssignedByUserId = "iam-keeper",
                AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        foreach (var permissionName in SystemPermissions.UserManagementWrite
                     .Concat(SystemPermissions.RoleManagementWrite)
                     .Concat(SystemPermissions.PermissionManagementWrite)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            await SeedRolePermissionAsync(db, roleId, permissionName);
    }

    private static async Task SeedRoleLinkAsync(AppDbContext db, string userId, string roleName, Guid? centerId)
    {
        var roleId = await SeedRoleAsync(db, roleName);
        if (await db.UserRoleAssociations.AnyAsync(x => x.UserId == userId && x.RoleId == roleId && x.CenterId == centerId && x.IsActive))
            return;

        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(AppDbContext db, string id, string userName)
    {
        if (await db.Users.AnyAsync(u => u.Id == id))
            return;

        db.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterAsync(AppDbContext db, Guid id, bool isActive)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == id))
            return;

        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = id.ToString()[..2],
            NameAr = "مركز",
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserCenterAsync(AppDbContext db, string userId, Guid centerId)
    {
        if (await db.UserCenters.AnyAsync(x => x.UserId == userId && x.CenterId == centerId))
            return;

        db.UserCenters.Add(new UserCenter
        {
            UserId = userId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> SeedRoleAsync(AppDbContext db, string name)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (existing is not null)
            return existing.Id;

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Scope = AssignableRolePolicy.CatalogScopeForName(name),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<Guid> SeedPermissionAsync(AppDbContext db, string permissionName)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
        if (permission is not null)
            return permission.Id;

        permission = new Permission { Id = Guid.NewGuid(), Name = permissionName, IsActive = true };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();
        return permission.Id;
    }

    private static async Task SeedRolePermissionAsync(AppDbContext db, string roleId, string permissionName)
    {
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
        if (permission is null)
        {
            permission = new Permission { Id = Guid.NewGuid(), Name = permissionName, IsActive = true };
            db.Permissions.Add(permission);
            await db.SaveChangesAsync();
        }

        if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id && rp.IsActive))
            return;

        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = permission.Id,
            IsActive = true,
            GrantedByUserId = "actor-1",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
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

    private static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        user.SetUser("actor-1", "admin", "127.0.0.1");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private sealed record UserHarness(UserService Service, Mock<UserManager<User>> UserManager);

    private sealed class SavingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SavingUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTransaction());
    }

    private sealed class RollbackTrackingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public RollbackTrackingUnitOfWork(AppDbContext db) => _db = db;
        public bool Began { get; private set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public bool FailOnSave { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailOnSave)
                throw new InvalidOperationException("assignment-failed");
            return _db.SaveChangesAsync(cancellationToken);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            Began = true;
            return Task.FromResult<IDbContextTransaction>(new TrackingTransaction(this));
        }

        private sealed class TrackingTransaction : IDbContextTransaction
        {
            private readonly RollbackTrackingUnitOfWork _owner;
            public TrackingTransaction(RollbackTrackingUnitOfWork owner) => _owner = owner;
            public Guid TransactionId { get; } = Guid.NewGuid();
            public void Commit() => _owner.Committed = true;
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                _owner.Committed = true;
                return Task.CompletedTask;
            }
            public void Rollback() => _owner.RolledBack = true;
            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                _owner.RolledBack = true;
                return Task.CompletedTask;
            }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
