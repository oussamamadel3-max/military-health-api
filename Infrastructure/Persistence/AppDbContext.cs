using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
using Domain.Entities;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditInterceptor _auditInterceptor;
    private readonly ICurrentUserService _currentUser;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUser,
        AuditInterceptor auditInterceptor) : base(options)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _auditInterceptor = auditInterceptor;
    }

    #region DbSets Authorization
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
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    #endregion

    public DbSet<OrgUnitType> OrgUnitTypes { get; set; } = null!;
    public DbSet<OrgUnit> OrgUnits { get; set; } = null!;
    public DbSet<UserOrgScope> UserOrgScopes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_auditInterceptor);
        base.OnConfiguring(optionsBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditLogsToSave = new List<AuditLog>();
        var currentUserDto = new CurrentUserDto
        {
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            IPAddress = _currentUser.IPAddress
        };

        var entriesForPostSaveAudit = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditableEntity && e.State == EntityState.Added)
            .ToList();

        AuditLogger.LogChanges(ChangeTracker, auditLogsToSave, currentUserDto, EntityState.Modified, EntityState.Deleted);

        var result = await base.SaveChangesAsync(cancellationToken);

        if (entriesForPostSaveAudit.Any())
        {
            AuditLogger.LogEntriesList(entriesForPostSaveAudit, auditLogsToSave, currentUserDto, EntityState.Added);
        }

        if (auditLogsToSave.Any())
        {
            _ = Task.Run(() => SaveAuditLogsInBackground(auditLogsToSave));
        }

        return result;
    }

    private async Task SaveAuditLogsInBackground(List<AuditLog> auditLogs)
    {
        using var scope = _scopeFactory.CreateScope();
        var auditContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        auditContext.AuditLogs.AddRange(auditLogs);
        await auditContext.SaveChangesAsync();
    }
}
