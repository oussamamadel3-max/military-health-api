using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Entities.Archive;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Export;
using Domain.Entities.Medical;
using Domain.Entities.Patient;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly AuditInterceptor _auditInterceptor;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCenterService _currentCenter;
    private bool _inAuditSave;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserService currentUser,
        ICurrentCenterService currentCenter,
        AuditInterceptor auditInterceptor) : base(options)
    {
        _currentUser = currentUser;
        _currentCenter = currentCenter;
        _auditInterceptor = auditInterceptor;
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<UserPermission> UserPermissions { get; set; } = null!;
    public DbSet<UserRoleAssociation> UserRoleAssociations { get; set; } = null!;
    public DbSet<FieldPermission> FieldPermissions { get; set; } = null!;
    public DbSet<UserFieldPermission> UserFieldPermissions { get; set; } = null!;
    public DbSet<RoleFieldPermission> RoleFieldPermissions { get; set; } = null!;
    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; } = null!;
    public DbSet<UserCenter> UserCenters { get; set; } = null!;
    public DbSet<UserClinicAssignment> UserClinicAssignments { get; set; } = null!;
    public DbSet<PasswordPolicy> PasswordPolicies { get; set; } = null!;
    public DbSet<UserPasswordHistory> UserPasswordHistories { get; set; } = null!;
    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<UserContact> UserContacts { get; set; } = null!;

    public DbSet<MedicalCenter> MedicalCenters { get; set; } = null!;
    public DbSet<HistoricalCenterMigrationCompletion> HistoricalCenterMigrationCompletions { get; set; } = null!;
    public DbSet<RecruitmentCenter> RecruitmentCenters { get; set; } = null!;
    public DbSet<Clinic> Clinics { get; set; } = null!;
    public DbSet<CenterClinic> CenterClinics { get; set; } = null!;
    public DbSet<ResultStatus> ResultStatuses { get; set; } = null!;
    public DbSet<RequiredActionType> RequiredActionTypes { get; set; } = null!;
    public DbSet<BmiCategory> BmiCategories { get; set; } = null!;
    public DbSet<MaritalStatus> MaritalStatuses { get; set; } = null!;
    public DbSet<BloodType> BloodTypes { get; set; } = null!;
    public DbSet<ConsultationType> ConsultationTypes { get; set; } = null!;
    public DbSet<InvestigationType> InvestigationTypes { get; set; } = null!;
    public DbSet<ValidationRule> ValidationRules { get; set; } = null!;
    public DbSet<FileAsset> FileAssets { get; set; } = null!;

    public DbSet<Member> Members { get; set; } = null!;
    public DbSet<MemberCenterIdentity> MemberCenterIdentities { get; set; } = null!;
    public DbSet<MemberTransfer> MemberTransfers { get; set; } = null!;
    public DbSet<MemberGeneralNote> MemberGeneralNotes { get; set; } = null!;
    public DbSet<MemberImportBatch> MemberImportBatches { get; set; } = null!;

    public DbSet<MedicalExam> MedicalExams { get; set; } = null!;
    public DbSet<ClinicAssessment> ClinicAssessments { get; set; } = null!;
    public DbSet<EyeExamDetail> EyeExamDetails { get; set; } = null!;
    public DbSet<EyeRefractionDetail> EyeRefractionDetails { get; set; } = null!;
    public DbSet<InternalExamDetail> InternalExamDetails { get; set; } = null!;
    public DbSet<SurgeryExamDetail> SurgeryExamDetails { get; set; } = null!;
    public DbSet<OrthopedicExamDetail> OrthopedicExamDetails { get; set; } = null!;
    public DbSet<EntExamDetail> EntExamDetails { get; set; } = null!;
    public DbSet<DeferredAction> DeferredActions { get; set; } = null!;
    public DbSet<ClinicExamEditLock> ClinicExamEditLocks { get; set; } = null!;
    public DbSet<FinalDecision> FinalDecisions { get; set; } = null!;
    public DbSet<FinalDecisionHistory> FinalDecisionHistories { get; set; } = null!;
    public DbSet<SupervisorObjection> SupervisorObjections { get; set; } = null!;

    public DbSet<Consultation> Consultations { get; set; } = null!;
    public DbSet<Investigation> Investigations { get; set; } = null!;
    public DbSet<MedicalAttachment> MedicalAttachments { get; set; } = null!;

    public DbSet<RecruitmentExportBatch> RecruitmentExportBatches { get; set; } = null!;
    public DbSet<RecruitmentExportItem> RecruitmentExportItems { get; set; } = null!;

    public DbSet<MemberArchive> MemberArchives { get; set; } = null!;

    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<CrossCenterAccess> CrossCenterAccesses { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        AuditJsonbTextDbFunction.Register(modelBuilder);

        // MH-SEC-051: allow isolated SQLite integration hosts without changing PostgreSQL production mapping.
        if (string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            ApplySqliteIntegrationCompatibility(modelBuilder);
    }

    /// <summary>
    /// Softens PostgreSQL-only store types / identity annotations so auth HTTP integration tests can host on SQLite.
    /// </summary>
    private static void ApplySqliteIntegrationCompatibility(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType();
                if (columnType is not null
                    && (columnType.Equals("citext", StringComparison.OrdinalIgnoreCase)
                        || columnType.Equals("jsonb", StringComparison.OrdinalIgnoreCase)
                        || columnType.Equals("inet", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("TEXT");
                }

                if (property.FindAnnotation("Npgsql:ValueGenerationStrategy") is not null)
                {
                    property.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
                    if (property.ClrType == typeof(int) || property.ClrType == typeof(long))
                        property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
                }
            }
        }

        modelBuilder.Entity<AuditEvent>().Property(e => e.IPAddress).HasConversion(
            v => v == null ? null : v.ToString(),
            v => string.IsNullOrWhiteSpace(v) ? null : System.Net.IPAddress.Parse(v));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_auditInterceptor);
        base.OnConfiguring(optionsBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_inAuditSave)
            return await base.SaveChangesAsync(cancellationToken);

        if (Database.CurrentTransaction != null)
            return await SaveWithAuditAsync(cancellationToken);

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            var result = await SaveWithAuditAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    /// <summary>
    /// العملية وسجلات التدقيق في المعاملة نفسها. فشل التدقيق يلغي العملية.
    /// إذا كانت هناك معاملة خارجية، لا تُفتح معاملة متداخلة ولا يُنفَّذ Commit هنا.
    /// </summary>
    private async Task<int> SaveWithAuditAsync(CancellationToken cancellationToken)
    {
        var auditEvents = new List<AuditEvent>();
        var currentUserDto = new CurrentUserDto
        {
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            IPAddress = _currentUser.IPAddress
        };
        var centerId = _currentCenter.IsCentralContext ? null : _currentCenter.CenterId;

        var addedEntries = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditableEntity && e.State == EntityState.Added)
            .ToList();

        AuditLogger.LogChanges(ChangeTracker, auditEvents, currentUserDto, _currentUser, centerId, EntityState.Modified, EntityState.Deleted);

        _inAuditSave = true;
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);

            if (addedEntries.Count > 0)
                AuditLogger.LogEntriesList(addedEntries, auditEvents, currentUserDto, _currentUser, centerId, EntityState.Added);

            if (auditEvents.Count > 0)
            {
                AuditEvents.AddRange(auditEvents);
                await base.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            _inAuditSave = false;
        }
    }
}
