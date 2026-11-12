using Application.Common.Permissions;
using Application.Common.Security;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

            // تمييز أحداث التدقيق أثناء البذرة عن طلبات HTTP (بدون عنوان عميل مضلل).
            IamSeedCatalogOps.ApplySystemSeedAuditContext(currentUser);

            // 1) تأكد من وجود القاعدة ومحدّثة
            await context.Database.MigrateAsync();

            // ============================
            // Helpers
            // ============================

            // إنشاء الدور الناقص فقط؛ لا تُعاد تفعيل أدوار عطّلها المسؤول (FR-016 / BR-047).
            Task<Role> EnsureRoleAsync(
                string name,
                string normalized,
                string? descriptionAr = null,
                Domain.Enums.RoleScope scope = Domain.Enums.RoleScope.Center)
                => IamSeedCatalogOps.EnsureRoleAsync(context, name, normalized, descriptionAr, scope);

            async Task<User> EnsureUserAsync(string userName, string email, string password,string fullName,IEnumerable<Role>? attachRoles = null)
            {
                var u = await userManager.Users.FirstOrDefaultAsync(x => x.UserName == userName);
                if (u == null)
                {
                    u = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserName = userName,
                        NormalizedUserName = userName.ToUpperInvariant(),
                        Email = email,
                        NormalizedEmail = email.ToUpperInvariant(),
                        EmailConfirmed = true,
                        IsActive = true,
                        CreationDate = DateTime.Now,
                        SecurityStamp = Guid.NewGuid().ToString(),
                        FullName = fullName
                    };
                    var res = await userManager.CreateAsync(u, password);
                    if (!res.Succeeded)
                        throw new Exception($"تعذر إنشاء المستخدم {userName}: " + string.Join(", ", res.Errors.Select(e => e.Description)));
                }

                if (attachRoles != null)
                {
                    foreach (var r in attachRoles)
                    {
                        var existingRoles = await context.UserRoleAssociations
                            .Where(ura => ura.UserId == u.Id && ura.RoleId == r.Id && ura.CenterId == null)
                            .OrderByDescending(ura => ura.IsActive)
                            .ThenByDescending(ura => ura.RevokedAt)
                            .ToListAsync();
                        if (existingRoles.Any(ura => ura.IsActive))
                            continue;

                        // لا تُعاد تفعيل إسناد دور سُحب إدارياً أثناء البذرة.
                        if (existingRoles.Count > 0)
                            continue;

                        context.UserRoleAssociations.Add(new UserRoleAssociation
                        {
                            Id = Guid.NewGuid(),
                            UserId = u.Id,
                            RoleId = r.Id,
                            AssignedByUserId = u.Id,
                            AssignedAt = DateTimeOffset.UtcNow,
                            IsActive = true
                        });
                    }
                    await context.SaveChangesAsync();
                }

                return u;
            }


            // --- صلاحيات: أدوات مساعدة (إنشاء الناقص فقط؛ لا تُلغى قرارات التعطيل/السحب الإدارية) ---
            Task<Permission> EnsurePermissionAsync(string name, string? desc)
                => IamSeedCatalogOps.EnsurePermissionAsync(context, name, desc);

            /// <summary>
            /// يعيد تسمية صلاحية قديمة إلى الاسم الجديد مع الحفاظ على إسنادات الأدوار/المستخدمين.
            /// إن وُجد الاسمان معاً تُنقل الإسنادات النشطة أو يُلغى المكرر، ثم تُعطّل القديمة.
            /// </summary>
            Task MigratePermissionNameAsync(string oldName, string newName, string? newDesc, string actorId)
                => IamSeedCatalogOps.MigratePermissionNameAsync(context, oldName, newName, newDesc, actorId);

            /// <summary>
            /// يفكك Members.CreateUpdate إلى إنشاء وتعديل. المشرف يحتفظ بالتعديل فقط؛ بقية الأدوار التي كانت تملك المفتاح الموحّد تحتفظ بالإنشاء أيضاً. الصلاحية المباشرة القديمة تُنسخ إلى المفتاحين.
            /// </summary>
            async Task SplitMembersCreateUpdateAsync(string actorId)
            {
                var oldPerm = await context.Permissions
                    .FirstOrDefaultAsync(p => p.Name == MilitaryHealthPermissions.Members.CreateUpdate);
                if (oldPerm == null)
                    return;

                var createName = MilitaryHealthPermissions.Members.Create;
                var updateName = MilitaryHealthPermissions.Members.Update;
                var roleLinks = await context.RolePermissions
                    .Where(rp => rp.PermissionId == oldPerm.Id && rp.IsActive)
                    .ToListAsync();
                var affectedRoleIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var link in roleLinks)
                {
                    var roleName = await roleManager.Roles
                        .Where(r => r.Id == link.RoleId)
                        .Select(r => r.Name)
                        .FirstOrDefaultAsync();
                    await EnsurePermissionsAssignedToRoleAsync(link.RoleId, actorId, new[] { updateName });
                    if (!string.Equals(roleName, "Supervisor", StringComparison.Ordinal))
                        await EnsurePermissionsAssignedToRoleAsync(link.RoleId, actorId, new[] { createName });
                    IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                    affectedRoleIds.Add(link.RoleId);
                }

                var userLinks = await context.UserPermissions
                    .Where(up => up.PermissionId == oldPerm.Id && up.IsActive)
                    .ToListAsync();
                var affectedUserIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var link in userLinks)
                {
                    await EnsureDirectPermissionCopyAsync(link, createName, actorId);
                    await EnsureDirectPermissionCopyAsync(link, updateName, actorId);
                    IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                    affectedUserIds.Add(link.UserId);
                }

                if (oldPerm.IsActive)
                    IamRetention.DeactivatePermission(oldPerm);
                await context.SaveChangesAsync();

                foreach (var roleId in affectedRoleIds)
                {
                    await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                        userRepository, refreshTokens, unitOfWork, userManager, roleId);
                }

                foreach (var userId in affectedUserIds)
                {
                    await SecurityStampHelper.InvalidateUserTokensAsync(
                        userRepository, refreshTokens, unitOfWork, userManager, userId);
                }
            }

            async Task EnsureDirectPermissionCopyAsync(UserPermission source, string permissionName, string actorId)
            {
                var permission = await context.Permissions.FirstAsync(p => p.Name == permissionName && p.IsActive);
                var existing = await context.UserPermissions
                    .Where(up => up.UserId == source.UserId
                        && up.PermissionId == permission.Id
                        && up.CenterId == source.CenterId)
                    .ToListAsync();
                if (existing.Any(up => up.IsActive))
                    return;

                var revoked = existing.OrderByDescending(up => up.RevokedAt).FirstOrDefault();
                if (revoked != null)
                    IamRetention.Reactivate(revoked, actorId, DateTimeOffset.UtcNow);
                else
                {
                    context.UserPermissions.Add(new UserPermission
                    {
                        UserId = source.UserId,
                        PermissionId = permission.Id,
                        CenterId = source.CenterId,
                        GrantedByUserId = actorId,
                        GrantedAt = DateTimeOffset.UtcNow,
                        IsActive = true
                    });
                }
            }

            /// <summary>
            /// يسحب مرة واحدة المنح الافتراضية التي أصبحت «لا» في SRS §7.5، ثم يكتب علامة حتى لا يُلغى منح لاحق من مدير النظام.
            /// </summary>
            async Task WithdrawRemovedMatrixDefaultsOnceAsync(
                string actorId,
                IReadOnlyDictionary<string, Role> roles)
            {
                var markerName = MilitaryHealthSeedCatalog.MatrixAlignmentMarker;
                if (await context.Permissions.AnyAsync(p => p.Name == markerName))
                    return;

                var affectedRoleIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (roleName, permissionName) in MilitaryHealthSeedCatalog.RemovedDefaultRoleGrants)
                {
                    if (!roles.TryGetValue(roleName, out var role))
                        continue;

                    var permissionId = await context.Permissions
                        .Where(p => p.Name == permissionName)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefaultAsync();
                    if (permissionId == null)
                        continue;

                    var links = await context.RolePermissions
                        .Where(rp => rp.RoleId == role.Id && rp.PermissionId == permissionId && rp.IsActive)
                        .ToListAsync();
                    foreach (var link in links)
                    {
                        IamRetention.Revoke(link, actorId, IamRetention.MatrixDefaultWithdrawn);
                        affectedRoleIds.Add(role.Id);
                    }
                }

                context.Permissions.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Name = markerName,
                    Description = "علامة داخلية: تمت مواءمة المنح الافتراضية مع SRS 7.5. ليست صلاحية تشغيلية.",
                    IsActive = false
                });
                await context.SaveChangesAsync();

                foreach (var roleId in affectedRoleIds)
                {
                    await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                        userRepository, refreshTokens, unitOfWork, userManager, roleId);
                }
            }

            Task EnsurePermissionsAndAssignToUserAsync(string userId, IEnumerable<(string Name, string? Desc)> list)
                => IamSeedCatalogOps.EnsureDirectUserPermissionsAsync(context, userId, list);


            async Task CleanupObsoletePermissionsAsync(string actorId)
            {
                var obsolete = await context.Permissions
                    .Where(p =>
                        p.Name == "SuperAdmin" ||
                        p.Name == "BranchManager" ||
                        p.Name == "Document" ||
                        p.Name == "ReportsDocuments" ||
                        p.Name == "ReportsAuditLogs.Export" ||
                        p.Name == "ReportsDashboard.Export" ||
                        p.Name == "PermissionManagement.Create" ||
                        p.Name == "PermissionManagement.Edit" ||
                        p.Name == "PermissionManagement.Delete" ||
                        p.Name == "ReportManagement.Read" ||
                        p.Name == "ReportManagement.Export" ||
                        (p.Name != null && p.Name.StartsWith("Document.")) ||
                        (p.Name != null && p.Name.StartsWith("ReportsDocuments.")) ||
                        (p.Name != null && p.Name.StartsWith("ReportsAuditLogs.")) ||
                        (p.Name != null && p.Name.StartsWith("ReportsDashboard.")) ||
                        (p.Name != null && p.Name.StartsWith("ReportManagement.")) ||
                        (p.Name != null && p.Name.StartsWith("OrgStructure.")) ||
                        (p.Name != null && p.Name.StartsWith("UserOrgScope.")))
                    .ToListAsync();

                if (obsolete.Count == 0) return;

                var obsoleteIds = obsolete.Select(p => p.Id).ToList();

                var userPerms = await context.UserPermissions
                    .Where(up => obsoleteIds.Contains(up.PermissionId) && up.IsActive)
                    .ToListAsync();
                foreach (var link in userPerms)
                    IamRetention.Revoke(link, actorId, IamRetention.ObsoletePermission);

                var rolePerms = await context.RolePermissions
                    .Where(rp => obsoleteIds.Contains(rp.PermissionId) && rp.IsActive)
                    .ToListAsync();
                foreach (var link in rolePerms)
                    IamRetention.Revoke(link, actorId, IamRetention.ObsoletePermission);

                foreach (var permission in obsolete)
                    IamRetention.DeactivatePermission(permission);
                await context.SaveChangesAsync();
            }

            async Task EnsurePermissionsAssignedToRoleAsync(
                string roleId,
                string grantedByUserId,
                IEnumerable<string> permissionNames)
            {
                var changed = await IamSeedCatalogOps.EnsurePermissionsAssignedToRoleAsync(
                    context, roleId, grantedByUserId, permissionNames);
                if (changed)
                {
                    await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                        userRepository, refreshTokens, unitOfWork, userManager, roleId);
                }
            }


            // ============================
            // 2) أدوار SRS §6.2 (مدير النظام المركزي = SuperAdmin في القالب)
            // ============================
            var seededRoles = new Dictionary<string, Role>(StringComparer.Ordinal);
            foreach (var roleDef in MilitaryHealthSeedCatalog.Roles)
            {
                seededRoles[roleDef.Name] = await EnsureRoleAsync(
                    roleDef.Name,
                    roleDef.NormalizedName,
                    roleDef.DescriptionAr,
                    roleDef.IsCentralCapable
                        ? Domain.Enums.RoleScope.Central
                        : Domain.Enums.RoleScope.Center);
            }

            var roleSuperAdmin = seededRoles["SuperAdmin"];
            var templateBootstrapPermissions = MilitaryHealthSeedCatalog.TemplateBootstrapPermissions;

            // ============================
            // 3) كتالوج الصلاحيات: قالب IAM + عمليات SRS §7.5
            // ============================
            foreach (var perm in MilitaryHealthSeedCatalog.AllCatalogPermissions)
                await EnsurePermissionAsync(perm.Name, perm.DescriptionAr);

            // ============================
            // 5) المستخدم bootstrap فقط (بدون مستخدمين تشغيليين إضافيين)
            // ============================
            var superAdmin = await EnsureUserAsync(
                userName: "superAdmin",
                email: "superAdmin@gmail.com",
                password: "cZf0bYa4HjMmqewUk2BM7#@3",
                fullName: "Super Administrator",
                attachRoles: new[] { roleSuperAdmin }
            );

            await MigratePermissionNameAsync("ReportsAuditLogs.Export", "AuditLog.Export", "تصدير تقرير السجل", superAdmin.Id);
            await MigratePermissionNameAsync("ReportsDashboard.Export", "Dashboard.Export", "تصدير تقرير لوحة التحكم", superAdmin.Id);
            await SplitMembersCreateUpdateAsync(superAdmin.Id);

            // صلاحيات القالب تبقى على دور SuperAdmin وعلى إسناده المباشر الحالي دون توسيعها إلى صلاحيات المجال
            await EnsurePermissionsAssignedToRoleAsync(
                roleSuperAdmin.Id,
                superAdmin.Id,
                templateBootstrapPermissions.Select(p => p.Name));

            // روابط Role–Permission من مصفوفة SRS §7.5 (إضافة الناقص فقط دون إحياء سحب إداري). سحب المنح الملغاة يتم مرة واحدة أدناه.
            var grantsByRole = MilitaryHealthSeedCatalog.GrantsByRoleName();
            foreach (var (roleName, permissionNames) in grantsByRole)
            {
                if (!seededRoles.TryGetValue(roleName, out var role))
                    throw new InvalidOperationException($"دور المصفوفة غير موجود في كتالوج الأدوار: {roleName}");

                await EnsurePermissionsAssignedToRoleAsync(role.Id, superAdmin.Id, permissionNames);
            }

            await WithdrawRemovedMatrixDefaultsOnceAsync(superAdmin.Id, seededRoles);

            // مزامنة UserPermissions للمستخدم bootstrap مع كتالوج القالب فقط
            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, templateBootstrapPermissions);

            await CleanupObsoletePermissionsAsync(superAdmin.Id);

            await EnsurePermissionsAssignedToRoleAsync(
                roleSuperAdmin.Id,
                superAdmin.Id,
                templateBootstrapPermissions.Select(p => p.Name));
            if (grantsByRole.TryGetValue("SuperAdmin", out var superAdminMatrix))
                await EnsurePermissionsAssignedToRoleAsync(roleSuperAdmin.Id, superAdmin.Id, superAdminMatrix);

            await ReceptionReferenceSeed.ApplyAsync(context, superAdmin.Id);
        }
    }
}
