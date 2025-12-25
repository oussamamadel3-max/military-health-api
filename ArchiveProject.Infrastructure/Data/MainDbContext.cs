using FinanceProject.Infrastructure.Persistence;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiveProject.Infrastructure.Data
{
    public class MainDbContext : DbContext
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly AuditInterceptor _auditInterceptor;
        private readonly ICurrentUserService _currentUser;

        public MainDbContext(DbContextOptions<MainDbContext> options, IServiceScopeFactory scopeFactory, ICurrentUserService currentUser, AuditInterceptor auditInterceptor) : base(options)
        {
            _scopeFactory = scopeFactory;
            _currentUser = currentUser;
            _auditInterceptor = auditInterceptor;
        }


        #region   === DbSets Authorization ===
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;
        public DbSet<UserPermission> UserPermissions { get; set; } = null!;
        public DbSet<UserRoleAssociation> UserRoleAssociations { get; set; } = null!;
        public DbSet<FieldPermission> FieldPermissions { get; set; } = null!;
        public DbSet<UserFieldPermission> UserFieldPermissions { get; set; } = null!;
        public DbSet<RoleFieldPermission> RoleFieldPermissions { get; set; } = null!;
        public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        #endregion


        // For Testing Purposes
        public DbSet<Branch> Branchs { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<DocumentType> DocumentTypes { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<ArchiveGroup> ArchiveGroups { get; set; }
        public DbSet<Attachment> Attachments { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            // 💡 الحل: إجبار Npgsql على استخدام UTC لجميع التواريخ
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            // 💡 الحل: فرض نوع عمود "Timestamp" في كيان AuditLog ليتوافق مع PostgreSQL
            modelBuilder.Entity<AuditLog>()
                .Property(a => a.Timestamp)
                .HasColumnType("timestamp with time zone");

            base.OnModelCreating(modelBuilder);

            #region === Mapping Authorization ===

            // ===== UserPermission =====
            modelBuilder.Entity<UserPermission>(entity =>
            {
                entity.HasKey(up => up.Id);

                entity.HasOne(up => up.User)
                      .WithMany(u => u.UserPermissions)
                      .HasForeignKey(up => up.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(up => up.Permission)
                      .WithMany(p => p.UserPermissions)
                      .HasForeignKey(up => up.PermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== RolePermission =====
            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(rp => rp.Id);

                entity.HasOne(rp => rp.Role)
                      .WithMany(r => r.RolePermissions)
                      .HasForeignKey(rp => rp.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(rp => rp.Permission)
                      .WithMany(p => p.RolePermissions)
                      .HasForeignKey(rp => rp.PermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== Permission =====
            modelBuilder.Entity<Permission>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
                entity.HasIndex(p => p.Name).IsUnique(); // اسم الصلاحية فريد
            });

            // ===== Role =====
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasMany(r => r.RolePermissions)
                      .WithOne(rp => rp.Role)
                      .HasForeignKey(rp => rp.RoleId);
            });

            // ===== User =====
            modelBuilder.Entity<User>(entity =>
            {
                // IdentityUser.Id هو string
                entity.HasMany(u => u.UserPermissions)
                      .WithOne(up => up.User)
                      .HasForeignKey(up => up.UserId);

                entity.Property(u => u.FullName).HasMaxLength(250);

                // 🔵 Department (اختياري) — موظف قسم
                entity.HasOne(u => u.Department)
                      .WithMany(d => d.Users)
                      .HasForeignKey(u => u.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(u => u.DepartmentId);

                // 🔵 Branch (اختياري) — مستخدم على مستوى الفرع (مثل مدير الفرع)
                entity.HasOne(u => u.Branch)
                      .WithMany(b => b.Users)            
                      .HasForeignKey(u => u.BranchId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(u => u.BranchId);
            });

            // ===== UserRoleAssociation =====
            modelBuilder.Entity<UserRoleAssociation>(entity =>
            {
                entity.HasKey(uur => uur.Id);

                entity.HasOne(uur => uur.User)
                      .WithMany(u => u.UserRoleAssociations)
                      .HasForeignKey(uur => uur.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(uur => uur.Role)
                      .WithMany(r => r.UserRoleAssociations)
                      .HasForeignKey(uur => uur.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== UserFieldPermission =====
            modelBuilder.Entity<UserFieldPermission>(entity =>
            {
                entity.HasKey(ufp => ufp.Id);

                entity.HasOne(ufp => ufp.User)
                      .WithMany(u => u.UserFieldPermissions)
                      .HasForeignKey(ufp => ufp.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ufp => ufp.FieldPermission)
                      .WithMany(fp => fp.UserFieldPermissions)
                      .HasForeignKey(ufp => ufp.FieldPermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== RoleFieldPermission =====
            modelBuilder.Entity<RoleFieldPermission>(entity =>
            {
                entity.HasKey(rfp => rfp.Id);

                entity.HasOne(rfp => rfp.Role)
                      .WithMany(r => r.RoleFieldPermissions)
                      .HasForeignKey(rfp => rfp.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(rfp => rfp.FieldPermission)
                      .WithMany(fp => fp.RoleFieldPermissions)
                      .HasForeignKey(rfp => rfp.FieldPermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== UserRefreshToken =====
            modelBuilder.Entity<UserRefreshToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Token).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ExpiryDate).IsRequired();
                entity.Property(e => e.IsRevoked).HasDefaultValue(false);

                entity.HasOne<User>() // علاقة اختيارية حسب تصميمك
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
            #endregion



            #region === Core Domain Entities ===  (ArchiveProject)

            // ===== Branch =====
            modelBuilder.Entity<Branch>(entity =>
            {
                entity.HasKey(b => b.BranchId);
                entity.Property(b => b.Code).IsRequired().HasMaxLength(50);
                entity.Property(b => b.BranchName).IsRequired().HasMaxLength(250);

                // ✅ مفقود عندك
                entity.Property(b => b.Description).HasMaxLength(1000);

                entity.HasIndex(b => b.Code).IsUnique();

                entity.HasMany(b => b.Users)
                      .WithOne(u => u.Branch)
                      .HasForeignKey(u => u.BranchId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== Department =====
            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasKey(d => d.DepartmentId);
                entity.Property(d => d.DepartmentName).IsRequired().HasMaxLength(250);
                entity.Property(d => d.Code).HasMaxLength(50);

                // ✅ مفقود عندك
                entity.Property(d => d.Description).HasMaxLength(1000);

                entity.HasIndex(d => d.BranchId);

                entity.HasOne(d => d.Branch)
                      .WithMany(b => b.Departments)
                      .HasForeignKey(d => d.BranchId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(d => d.Users)
                      .WithOne(u => u.Department)
                      .HasForeignKey(u => u.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(d => new { d.BranchId, d.Code })
                      .IsUnique();
            });

            // ===== DocumentType =====
            modelBuilder.Entity<DocumentType>(entity =>
            {
                entity.HasKey(t => t.DocumentTypeId);

                // ✅ خليها نفس DTO (أنا بنصح تبقى 250 متل عندك)
                entity.Property(t => t.DocumentTypeName).IsRequired().HasMaxLength(150);

                // ✅ خلي DTO ما يتجاوز 512
                entity.Property(t => t.AllowedExtensions).HasMaxLength(512);

                entity.HasIndex(t => t.DocumentTypeName);
            });

            // ===== ArchiveGroup =====
            modelBuilder.Entity<ArchiveGroup>(entity =>
            {
                entity.HasKey(g => g.ArchiveGroupId);
                entity.Property(g => g.Title).IsRequired().HasMaxLength(250);

                entity.HasIndex(d => new { d.DepartmentId, d.ArchiveGroupNumber }).IsUnique();

                entity.HasIndex(g => g.DepartmentId);
                entity.HasIndex(g => g.UserId);

                entity.HasOne(g => g.Department)
                      .WithMany(d => d.ArchiveGroups)
                      .HasForeignKey(g => g.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(g => g.User)
                      .WithMany(u => u.ArchiveGroups)
                      .HasForeignKey(g => g.UserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== Document =====
            modelBuilder.Entity<Document>(entity =>
            {
                entity.HasKey(d => d.DocumentId);

                entity.Property(d => d.Title).IsRequired().HasMaxLength(250);

                // ✅ مفقود عندك (DTO عندك Description)
                entity.Property(d => d.Description).HasMaxLength(2000);

                // ✅ (اختياري بس مفيد) أعمدة التدقيق إذا موجودة بالـ Entity
                // entity.Property(d => d.CreatedBy).HasMaxLength(100);
                // entity.Property(d => d.UpdatedBy).HasMaxLength(100);

                entity.HasIndex(d => new { d.DepartmentId, d.DocumentNumber }).IsUnique();

                entity.HasIndex(d => d.DepartmentId);
                entity.HasIndex(d => d.UserId);
                entity.HasIndex(d => d.DocumentTypeId);
                entity.HasIndex(d => d.ArchiveGroupId);

                entity.HasOne(d => d.Department)
                      .WithMany()
                      .HasForeignKey(d => d.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.User)
                      .WithMany(u => u.Documents)
                      .HasForeignKey(d => d.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.DocumentType)
                      .WithMany(t => t.Documents)
                      .HasForeignKey(d => d.DocumentTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.ArchiveGroup)
                      .WithMany(g => g.Documents)
                      .HasForeignKey(d => d.ArchiveGroupId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ===== Attachment =====
            modelBuilder.Entity<Attachment>(entity =>
            {
                entity.HasKey(a => a.AttachmentId);

                entity.Property(a => a.OwnerType).IsRequired().HasMaxLength(100);
                entity.Property(a => a.OwnerId).IsRequired();

                entity.Property(a => a.FileName).IsRequired().HasMaxLength(260);
                entity.Property(a => a.FileExtension).HasMaxLength(20);

                entity.Property(a => a.FilePath).IsRequired().HasMaxLength(1024);
                entity.Property(a => a.ContentType).HasMaxLength(150);
                entity.Property(a => a.Description).HasMaxLength(1000);

                entity.Property(a => a.FileSize).IsRequired();
                entity.Property(a => a.IsProfilePhoto).HasDefaultValue(false);
                entity.Property(a => a.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(a => new { a.OwnerType, a.OwnerId });

                // postgresql 
                entity.HasIndex(a => new { a.OwnerType, a.OwnerId, a.IsProfilePhoto })
                      .HasFilter("\"IsProfilePhoto\" = true")
                      .IsUnique();

                //// SqlServer
                //entity.HasIndex(a => new { a.OwnerType, a.OwnerId, a.IsProfilePhoto });

            });

            #endregion



            // === (اختياري) استدعاء Seed
            // SeedData.Seed(modelBuilder);
        }


        #region === Configuring AuditInterceptor And AuditLogging ===

        // تكوين DbContext لاستخدام AuditInterceptor
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(_auditInterceptor);
            base.OnConfiguring(optionsBuilder);
        }

        public override int SaveChanges()
        {
            // لو عندك Audit متزامن، فعّله هنا (اختياري)
            // WriteAuditLogsSync();
            return base.SaveChanges();
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

            // 1) التقاط المُضافين قبل الحفظ لاستخراج الـID بعد الحفظ
            var entriesForPostSaveAudit = ChangeTracker.Entries()
                .Where(e => e.Entity is IAuditableEntity && e.State == EntityState.Added)
                .ToList();

            // 2) المرحلة الأولى: تعديل/حذف (المفاتيح معروفة قبل الحفظ)
            AuditLogger.LogChanges(ChangeTracker, auditLogsToSave, currentUserDto, EntityState.Modified, EntityState.Deleted);

            // 3) الحفظ الأولي لتوليد المفاتيح
            var result = await base.SaveChangesAsync(cancellationToken);

            // 4) المرحلة الثانية: تسجيل المُضافين الآن بعد توفّر المفاتيح
            if (entriesForPostSaveAudit.Any())
            {
                AuditLogger.LogEntriesList(entriesForPostSaveAudit, auditLogsToSave, currentUserDto, EntityState.Added);
            }

            // 5) حفظ سجلات التدقيق (Fire-and-Forget)
            if (auditLogsToSave.Any())
            {
                _ = Task.Run(() => SaveAuditLogsInBackground(auditLogsToSave));
            }

            return result;
        }

        // دالة مساعدة لحفظ سجلات التدقيق في الخلفية.
        // ملاحظة: غيّر MainDbContext إلى اسم DbContext الحقيقي لديك إن لزم.
        private async Task SaveAuditLogsInBackground(List<AuditLog> auditLogs)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var auditContext = scope.ServiceProvider.GetRequiredService<MainDbContext>();
                auditContext.AuditLogs.AddRange(auditLogs);
                await auditContext.SaveChangesAsync();
            }
        }

        #endregion


    }
}

