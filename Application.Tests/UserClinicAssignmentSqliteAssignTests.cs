using System.Text.RegularExpressions;
using Application.Dtos.AuthDtos;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Enums;
using Application.Common.Permissions;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace Application.Tests;

/// <summary>تشخيص AssignAsync على SQLite بنفس مسار الحفظ والتدقيق في الإنتاج.</summary>
public class UserClinicAssignmentSqliteAssignTests
{
    private static readonly Guid CenterB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicEyes = Guid.Parse("f3333333-3333-3333-3333-333333333333");

    private readonly ITestOutputHelper _output;

    public UserClinicAssignmentSqliteAssignTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sqlite_assign_persists_new_clinic_assignment_with_audit()
    {
        await using var connection = OpenSqlite();
        var actor = new TestCurrentUser();
        actor.SetUser("actor-super", "it.clinic.super", "127.0.0.1");
        var center = new TestCenter(CenterB, central: false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        await using var db = new AppDbContext(options, actor, center, new AuditInterceptor(actor));
        await CreateSqliteSchemaAsync(db);
        var (doctor2Id, _) = await SeedMinimalAsync(db, "actor-super");

        var service = CreateService(db, actor.UserId!);
        Exception? caught = null;
        try
        {
            await service.AssignAsync(new AssignUserClinicDto
            {
                UserId = doctor2Id,
                CenterId = CenterB,
                ClinicId = ClinicEyes
            });
        }
        catch (Exception ex)
        {
            caught = ex;
            _output.WriteLine(FormatException(ex));
        }

        var count = await db.UserClinicAssignments.CountAsync(a =>
            a.UserId == doctor2Id && a.CenterId == CenterB && a.IsActive);
        _output.WriteLine($"activeAssignments={count}");

        Assert.Null(caught);
        Assert.Equal(1, count);
    }

    private static string FormatException(Exception ex)
    {
        var lines = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            lines.Add($"{cur.GetType().Name}: {cur.Message}");
        return string.Join(" <= ", lines);
    }

    private static async Task CreateSqliteSchemaAsync(AppDbContext db)
    {
        var script = Regex.Replace(db.Database.GenerateCreateScript(), "::[A-Za-z_][A-Za-z0-9_]*", "");
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(statement))
                continue;
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(string Doctor2Id, string ActorId)> SeedMinimalAsync(AppDbContext db, string actorId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = actorId,
            UserName = "it.clinic.super",
            NormalizedUserName = "IT.CLINIC.SUPER",
            Email = "super@test",
            NormalizedEmail = "SUPER@TEST",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true
        });
        var doctor2Id = "doctor2-" + Guid.NewGuid().ToString("N")[..8];
        db.Users.Add(new User
        {
            Id = doctor2Id,
            UserName = "it.clinic.doctor2",
            NormalizedUserName = "IT.CLINIC.DOCTOR2",
            Email = "d2@test",
            NormalizedEmail = "D2@TEST",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true
        });
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = CenterB,
            Code = "CLB",
            NameAr = "مركز ب",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Clinics.Add(new Clinic
        {
            ClinicId = ClinicEyes,
            Code = "EYE",
            NameAr = "العيون",
            SortOrder = 1,
            CountsForFinal = true,
            IsActive = true
        });
        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = CenterB,
            ClinicId = ClinicEyes,
            IsActive = true,
            ActivatedAt = now
        });
        db.UserCenters.Add(new UserCenter
        {
            UserId = doctor2Id,
            CenterId = CenterB,
            IsActive = true,
            AssignedAt = now,
            AssignedByUserId = actorId
        });
        var roleId = Guid.NewGuid().ToString();
        db.Roles.Add(new Role
        {
            Id = roleId,
            Name = UserClinicAssignmentService.ClinicDoctorRoleName,
            NormalizedName = UserClinicAssignmentService.ClinicDoctorRoleName.ToUpperInvariant(),
            Scope = RoleScope.Center,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = doctor2Id,
            RoleId = roleId,
            CenterId = CenterB,
            IsActive = true,
            AssignedByUserId = actorId,
            AssignedAt = now
        });
        var superRoleId = Guid.NewGuid().ToString();
        db.Roles.Add(new Role
        {
            Id = superRoleId,
            Name = "SuperAdmin",
            NormalizedName = "SUPERADMIN",
            Scope = RoleScope.Central,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        var manageId = Guid.NewGuid();
        db.Permissions.Add(new Permission
        {
            Id = manageId,
            Name = MilitaryHealthPermissions.UserClinics.Manage,
            Description = "manage",
            IsActive = true
        });
        db.RolePermissions.Add(new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = superRoleId,
            PermissionId = manageId,
            IsActive = true,
            GrantedByUserId = actorId,
            GrantedAt = now
        });
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            RoleId = superRoleId,
            CenterId = null,
            IsActive = true,
            AssignedByUserId = actorId,
            AssignedAt = now
        });
        await db.SaveChangesAsync();
        return (doctor2Id, actorId);
    }

    private static UserClinicAssignmentService CreateService(AppDbContext db, string actorId)
    {
        var users = new UserRepository(db);
        var centers = new MedicalCenterRepository(db);
        var centerClinics = new CenterClinicRepository(db);
        var access = new AccessContextRepository(db);
        var uow = new UnitOfWork(db);

        var manager = new Mock<UserManager<User>>(
            Mock.Of<IUserStore<User>>(),
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Mock.Of<ILookupNormalizer>(),
            Mock.Of<IdentityErrorDescriber>(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()));
        gate.Setup(g => g.EnsureLocalAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterB);

        var current = new TestCurrentUser();
        current.SetUser(actorId, "it.clinic.super", "127.0.0.1");

        return new UserClinicAssignmentService(
            manager.Object,
            users,
            centers,
            centerClinics,
            access,
            uow,
            gate.Object,
            current);
    }

    private static SqliteConnection OpenSqlite()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.CreateFunction("btrim", (string? value) => value?.Trim() ?? string.Empty);
        return connection;
    }
}
