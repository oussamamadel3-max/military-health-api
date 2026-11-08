using System.Text.RegularExpressions;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Interfaces;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Application.Tests;

public class AuditLoggerTests
{
    [Fact]
    public async Task Audit_captures_guid_long_string_and_composite_keys()
    {
        await using var db = CreateProbe();
        var guidId = Guid.NewGuid();
        var compositeA = Guid.NewGuid();
        db.Add(new GuidProbe { Id = guidId, Name = "new", PasswordHash = "secret-hash" });
        db.Add(new LongProbe { Id = 42, Title = "long-key" });
        db.Add(new StringProbe { Code = "user-key", Label = "text" });
        db.Add(new CompositeProbe { PartA = compositeA, PartB = "B", Value = "compound" });
        await db.SaveChangesAsync();

        var events = new List<AuditEvent>();
        AuditLogger.LogEntriesList(db.ChangeTracker.Entries().ToList(), events, User(), Request(), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), EntityState.Added);

        Assert.Contains(events, e => e.EntityType == nameof(GuidProbe) && e.EntityId == guidId.ToString());
        Assert.Contains(events, e => e.EntityType == nameof(LongProbe) && e.EntityId == "42");
        Assert.Contains(events, e => e.EntityType == nameof(StringProbe) && e.EntityId == "user-key");
        Assert.Contains(events, e => e.EntityType == nameof(CompositeProbe) && e.EntityId == $"{compositeA}:B");
    }

    [Fact]
    public async Task Audit_stores_old_and_new_values_and_omits_secrets()
    {
        await using var db = CreateProbe();
        var row = new GuidProbe { Id = Guid.NewGuid(), Name = "before", PasswordHash = "secret-hash" };
        db.Add(row);
        await db.SaveChangesAsync();

        row.Name = "after";
        row.PasswordHash = "new-secret";

        var events = new List<AuditEvent>();
        var centerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        AuditLogger.LogChanges(db.ChangeTracker, events, User(), Request(), centerId, EntityState.Modified);

        var audit = Assert.Single(events);
        Assert.Equal("CENTER", audit.ContextScope);
        Assert.Equal(centerId, audit.CenterId);
        Assert.Equal("actor-1", audit.ActorUserId);
        Assert.Contains("before", audit.OldValues);
        Assert.Contains("after", audit.NewValues);
        Assert.DoesNotContain("secret-hash", audit.OldValues);
        Assert.DoesNotContain("new-secret", audit.NewValues);
        Assert.DoesNotContain("PasswordHash", audit.NewValues);
        Assert.Null(audit.TargetUserId);
    }

    [Fact]
    public void Audit_event_and_cross_center_access_are_not_self_audited()
    {
        var events = new List<AuditEvent>();
        using var db = CreateProbe();
        db.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ContextScope = "CENTRAL",
            EventType = "READ",
            Result = "SUCCESS",
            CorrelationId = Guid.NewGuid()
        });
        db.Add(new CrossCenterAccess
        {
            AccessId = Guid.NewGuid(),
            TargetCenterId = Guid.NewGuid(),
            AccessReason = "reason",
            DataType = "member"
        });

        AuditLogger.LogChanges(db.ChangeTracker, events, User(), Request(), null, EntityState.Added);
        Assert.Empty(events);
    }

    private static ProbeDb CreateProbe()
        => new(new DbContextOptionsBuilder<ProbeDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CurrentUserDto User() => new() { UserId = "actor-1", UserName = "Actor", IPAddress = "127.0.0.1" };

    private static ICurrentUserService Request()
    {
        var user = new TestCurrentUser();
        user.SetRequest("unit-test", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        return user;
    }
}

public class AuditTransactionTests
{
    [Fact]
    public async Task Failed_audit_rolls_back_the_business_change()
    {
        await using var connection = OpenSqlite();
        var user = new TestCurrentUser();
        var center = new TestCenter(null, central: true);
        var options = SqliteOptions(connection, interceptAuditFailure: true);
        await using (var db = new AppDbContext(options, user, center, new AuditInterceptor(user)))
        {
            await CreateSqliteSchemaAsync(db);
            db.Permissions.Add(new Permission { Id = Guid.NewGuid(), Name = "Probe.Read", IsActive = true });
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Equal("audit failed", error.Message);
        }

        await using var verify = new AppDbContext(SqliteOptions(connection, false), user, center, new AuditInterceptor(user));
        Assert.Empty(verify.Permissions);
        Assert.Empty(verify.AuditEvents);
    }

    [Fact]
    public async Task Saving_an_audit_event_does_not_create_another_audit_event()
    {
        await using var connection = OpenSqlite();
        var user = new TestCurrentUser();
        var center = new TestCenter(null, central: true);
        await using var db = new AppDbContext(SqliteOptions(connection, false), user, center, new AuditInterceptor(user));
        await CreateSqliteSchemaAsync(db);

        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ContextScope = "CENTRAL",
            EventType = "READ",
            Result = "SUCCESS",
            CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.AuditEvents.CountAsync());
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
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(statement, ex);
            }
        }
    }

    private static SqliteConnection OpenSqlite()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.CreateFunction("btrim", (string? value) => value?.Trim() ?? string.Empty);
        return connection;
    }

    private static DbContextOptions<AppDbContext> SqliteOptions(SqliteConnection connection, bool interceptAuditFailure)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
        if (interceptAuditFailure)
            builder.AddInterceptors(new FailWhenAuditEventIsSaved());
        return builder.Options;
    }

    private sealed class FailWhenAuditEventIsSaved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("audit failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public class EfModelTests
{
    [Fact]
    public void Model_contains_the_official_tables_and_no_legacy_org_entities()
    {
        var user = new TestCurrentUser();
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=mhs_model_check;Username=unused;Password=unused")
                .ConfigureWarnings(w => w.Throw(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
                .Options,
            user,
            new TestCenter(null, true),
            new AuditInterceptor(user));

        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var (clr, schema, table) in OfficialTables)
        {
            var entity = model.GetEntityTypes().Single(e => e.ClrType.Name == clr);
            Assert.Equal(schema, entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
        }

        Assert.Equal(OfficialTables.Length, model.GetEntityTypes().Count(e =>
            OfficialTables.Any(t => t.Clr == e.ClrType.Name)));
        Assert.Null(model.FindEntityType("Domain.Entities.Core.OrgUnit"));
        Assert.Null(model.FindEntityType("Domain.Entities.Core.OrgUnitType"));
        Assert.Null(model.FindEntityType("Domain.Entities.Core.UserOrgScope"));

        var userCenter = model.GetEntityTypes().Single(e => e.ClrType.Name == nameof(UserCenter));
        Assert.Equal(2, userCenter.FindPrimaryKey()!.Properties.Count);
        Assert.False(userCenter.FindProperty(nameof(UserCenter.AssignedByUserId))!.IsNullable);

        var profile = model.GetEntityTypes().Single(e => e.ClrType.Name == nameof(UserProfile));
        Assert.Contains(profile.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == nameof(UserProfile.EmployeeNumber)));
        Assert.Contains(profile.GetCheckConstraints(), c => c.Name == "ckUserProfile1");
        Assert.True(profile.FindProperty(nameof(UserProfile.ProfileFileAssetId))!.IsNullable);

        var contact = model.GetEntityTypes().Single(e => e.ClrType.Name == nameof(UserContact));
        Assert.Contains(contact.GetIndexes(), i => i.IsUnique && i.Properties.Count == 3);
        Assert.Contains(contact.GetCheckConstraints(), c => c.Name == "ckUserContact1");

        var file = model.GetEntityTypes().Single(e => e.ClrType.Name == nameof(Domain.Entities.Core.FileAsset));
        Assert.True(file.FindProperty(nameof(Domain.Entities.Core.FileAsset.CenterId))!.IsNullable);
        Assert.False(file.FindProperty(nameof(Domain.Entities.Core.FileAsset.UploadedByUserId))!.IsNullable);

        var attachment = model.GetEntityTypes().Single(e => e.ClrType.Name == "MedicalAttachment");
        Assert.Contains(attachment.GetCheckConstraints(), c => c.Name == "ckMedicalAttachment1");
        Assert.Contains(attachment.GetForeignKeys(), fk => fk.Properties.Count == 3);
        Assert.NotEmpty(file.GetDeclaredQueryFilters());
        Assert.NotEmpty(attachment.GetDeclaredQueryFilters());
        var fileFk = attachment.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Domain.Entities.Core.FileAsset));
        Assert.True(fileFk.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, fileFk.DeleteBehavior);

        var roleAssignment = model.GetEntityTypes().Single(e => e.ClrType == typeof(UserRoleAssociation));
        Assert.All(
            roleAssignment.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(User) || fk.PrincipalEntityType.ClrType == typeof(Role)),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    private static readonly (string Clr, string Schema, string Table)[] OfficialTables =
    {
        ("MedicalCenter", "core", "medicalCenter"),
        ("RecruitmentCenter", "core", "recruitmentCenter"),
        ("Clinic", "core", "clinic"),
        ("CenterClinic", "core", "centerClinic"),
        ("ResultStatus", "core", "resultStatus"),
        ("RequiredActionType", "core", "requiredActionType"),
        ("BmiCategory", "core", "bmiCategory"),
        ("MaritalStatus", "core", "maritalStatus"),
        ("BloodType", "core", "bloodType"),
        ("ConsultationType", "core", "consultationType"),
        ("InvestigationType", "core", "investigationType"),
        ("ValidationRule", "core", "validationRule"),
        ("FileAsset", "core", "fileAsset"),
        ("User", "iam", "Users"),
        ("UserProfile", "iam", "userProfile"),
        ("UserContact", "iam", "userContact"),
        ("PasswordPolicy", "iam", "passwordPolicy"),
        ("UserPasswordHistory", "iam", "userPasswordHistory"),
        ("UserRefreshToken", "iam", "refreshToken"),
        ("Role", "iam", "Roles"),
        ("Permission", "iam", "Permissions"),
        ("RolePermission", "iam", "RolePermissions"),
        ("UserCenter", "iam", "userCenter"),
        ("UserRoleAssociation", "iam", "UserRoleAssociations"),
        ("UserPermission", "iam", "UserPermissions"),
        ("UserClinicAssignment", "iam", "userClinicAssignment"),
        ("Member", "patient", "member"),
        ("MemberCenterIdentity", "patient", "memberCenterIdentity"),
        ("MemberTransfer", "patient", "memberTransfer"),
        ("MemberGeneralNote", "patient", "memberGeneralNote"),
        ("MedicalExam", "clinical", "medicalExam"),
        ("ClinicAssessment", "clinical", "clinicAssessment"),
        ("EyeExamDetail", "clinical", "eyeExamDetail"),
        ("EyeRefractionDetail", "clinical", "eyeRefractionDetail"),
        ("InternalExamDetail", "clinical", "internalExamDetail"),
        ("SurgeryExamDetail", "clinical", "surgeryExamDetail"),
        ("OrthopedicExamDetail", "clinical", "orthopedicExamDetail"),
        ("EntExamDetail", "clinical", "entExamDetail"),
        ("DeferredAction", "clinical", "deferredAction"),
        ("FinalDecision", "clinical", "finalDecision"),
        ("FinalDecisionHistory", "clinical", "finalDecisionHistory"),
        ("SupervisorObjection", "clinical", "supervisorObjection"),
        ("Consultation", "medical", "consultation"),
        ("Investigation", "medical", "investigation"),
        ("MedicalAttachment", "medical", "medicalAttachment"),
        ("RecruitmentExportBatch", "export", "recruitmentExportBatch"),
        ("RecruitmentExportItem", "export", "recruitmentExportItem"),
        ("MemberArchive", "archive", "memberArchive"),
        ("AuditEvent", "audit", "event"),
        ("CrossCenterAccess", "audit", "crossCenterAccess")
    };
}

internal sealed class GuidProbe : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

internal sealed class LongProbe : IAuditableEntity
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
}

internal sealed class StringProbe : IAuditableEntity
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
}

internal sealed class CompositeProbe : IAuditableEntity
{
    public Guid PartA { get; set; }
    public string PartB { get; set; } = "";
    public string Value { get; set; } = "";
}

internal sealed class ProbeDb : DbContext
{
    public ProbeDb(DbContextOptions<ProbeDb> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuidProbe>().HasKey(x => x.Id);
        modelBuilder.Entity<LongProbe>().HasKey(x => x.Id);
        modelBuilder.Entity<StringProbe>().HasKey(x => x.Code);
        modelBuilder.Entity<CompositeProbe>().HasKey(x => new { x.PartA, x.PartB });
        modelBuilder.Ignore<User>();
        modelBuilder.Entity<AuditEvent>().HasKey(x => x.EventId);
        modelBuilder.Entity<CrossCenterAccess>().HasKey(x => x.AccessId);
        modelBuilder.Entity<CrossCenterAccess>().Ignore(x => x.AuditEvent);
        modelBuilder.Entity<CrossCenterAccess>().Ignore(x => x.TargetCenter);
        modelBuilder.Entity<AuditEvent>().Ignore(x => x.ActorUser);
        modelBuilder.Entity<AuditEvent>().Ignore(x => x.Center);
        // TargetUserId يُعامل كعمود عادي في ProbeDb دون علاقة FK إلى User.
        modelBuilder.Entity<AuditEvent>().Property(x => x.TargetUserId);
    }
}

internal sealed class TestCurrentUser : ICurrentUserService
{
    public string UserId { get; private set; } = "actor-1";
    public string UserName { get; private set; } = "Actor";
    public string IPAddress { get; private set; } = "127.0.0.1";
    public string? UserAgent { get; private set; } = "unit-test";
    public Guid CorrelationId { get; private set; } = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public void SetUser(string userId, string userName, string ipAddress)
    {
        UserId = userId;
        UserName = userName;
        IPAddress = ipAddress;
    }
    public void SetRequest(string? userAgent, Guid correlationId)
    {
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }
}

internal sealed class TestCenter : ICurrentCenterService
{
    public TestCenter(Guid? centerId, bool central)
    {
        CenterId = centerId;
        IsCentralContext = central;
    }

    public Guid? CenterId { get; }
    public bool IsCentralContext { get; }
    public string? UserId => "actor-1";
}
