using Application.Common.Permissions;
using Application.Common.Security;
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

            // 1) تأكد من وجود القاعدة ومحدّثة
            await context.Database.MigrateAsync();

            // ============================
            // Helpers
            // ============================

            async Task<Role> EnsureRoleAsync(string name, string normalized, string? descriptionAr = null)
            {
                var role = await roleManager.Roles.FirstOrDefaultAsync(r => r.Name == name);
                if (role == null)
                {
                    role = new Role
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        NormalizedName = normalized,
                        Description = descriptionAr,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };
                    var res = await roleManager.CreateAsync(role);
                    if (!res.Succeeded)
                        throw new Exception($"تعذر إنشاء الدور {name}: " + string.Join(", ", res.Errors.Select(e => e.Description)));
                }
                else
                {
                    var dirty = false;
                    if (!role.IsActive)
                    {
                        role.IsActive = true;
                        dirty = true;
                    }
                    if (!string.IsNullOrWhiteSpace(descriptionAr) &&
                        !string.Equals(role.Description, descriptionAr, StringComparison.Ordinal))
                    {
                        role.Description = descriptionAr;
                        dirty = true;
                    }
                    if (dirty)
                        await roleManager.UpdateAsync(role);
                }
                return role;
            }

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

                        var revokedRole = existingRoles.FirstOrDefault();
                        if (revokedRole != null)
                            IamRetention.Reactivate(revokedRole, u.Id, DateTimeOffset.UtcNow);
                        else
                        {
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
                    }
                    await context.SaveChangesAsync();
                }

                return u;
            }


            // --- صلاحيات: أدوات مساعدة ---
            async Task<Permission> EnsurePermissionAsync(string name, string? desc)
            {
                var perm = await context.Permissions.FirstOrDefaultAsync(x => x.Name == name);
                if (perm == null)
                {
                    perm = new Permission
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        Description = desc,
                        IsActive = true
                    };
                    context.Permissions.Add(perm);
                    await context.SaveChangesAsync();
                }
                else if (!perm.IsActive)
                {
                    perm.IsActive = true;
                    await context.SaveChangesAsync();
                }
                return perm;
            }

            /// <summary>
            /// يعيد تسمية صلاحية قديمة إلى الاسم الجديد مع الحفاظ على إسنادات الأدوار/المستخدمين.
            /// إن وُجد الاسمان معاً تُنقل الإسنادات النشطة أو يُلغى المكرر، ثم تُعطّل القديمة.
            /// </summary>
            async Task MigratePermissionNameAsync(string oldName, string newName, string? newDesc, string actorId)
            {
                var oldPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Name == oldName);
                if (oldPerm == null) return;

                var newPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Name == newName);
                if (newPerm == null)
                {
                    oldPerm.Name = newName;
                    if (!string.IsNullOrWhiteSpace(newDesc))
                        oldPerm.Description = newDesc;
                    await context.SaveChangesAsync();
                    return;
                }

                var oldRoleLinks = await context.RolePermissions
                    .Where(rp => rp.PermissionId == oldPerm.Id)
                    .ToListAsync();
                var newRoleLinks = await context.RolePermissions
                    .Where(rp => rp.PermissionId == newPerm.Id)
                    .ToListAsync();
                foreach (var link in oldRoleLinks.Where(link => link.IsActive))
                {
                    var counterpart = newRoleLinks.FirstOrDefault(row => row.RoleId == link.RoleId);
                    if (counterpart == null)
                        link.PermissionId = newPerm.Id;
                    else if (!counterpart.IsActive)
                    {
                        IamRetention.Reactivate(counterpart, actorId, DateTimeOffset.UtcNow);
                        IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                    }
                    else
                        IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                }

                var oldUserLinks = await context.UserPermissions
                    .Where(up => up.PermissionId == oldPerm.Id)
                    .ToListAsync();
                var newUserLinks = await context.UserPermissions
                    .Where(up => up.PermissionId == newPerm.Id)
                    .ToListAsync();
                foreach (var link in oldUserLinks.Where(link => link.IsActive))
                {
                    var counterpart = newUserLinks.FirstOrDefault(row => row.UserId == link.UserId && row.CenterId == link.CenterId);
                    if (counterpart == null)
                        link.PermissionId = newPerm.Id;
                    else if (!counterpart.IsActive)
                    {
                        IamRetention.Reactivate(counterpart, actorId, DateTimeOffset.UtcNow);
                        IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                    }
                    else
                        IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
                }

                IamRetention.DeactivatePermission(oldPerm);
                if (!string.IsNullOrWhiteSpace(newDesc) && string.IsNullOrWhiteSpace(newPerm.Description))
                    newPerm.Description = newDesc;

                await context.SaveChangesAsync();
            }

            async Task EnsurePermissionsAndAssignToUserAsync(string userId, IEnumerable<(string Name, string? Desc)> list)
            {
                foreach (var (name, desc) in list)
                {
                    var perm = await EnsurePermissionAsync(name, desc);

                    if (!perm.IsActive)
                        continue;

                    var existingGrants = await context.UserPermissions
                        .Where(up => up.UserId == userId && up.PermissionId == perm.Id && up.CenterId == null)
                        .OrderByDescending(up => up.IsActive)
                        .ThenByDescending(up => up.RevokedAt)
                        .ToListAsync();
                    if (existingGrants.Any(up => up.IsActive))
                        continue;

                    var revokedGrant = existingGrants.FirstOrDefault();
                    if (revokedGrant != null)
                        IamRetention.Reactivate(revokedGrant, userId, DateTimeOffset.UtcNow);
                    else
                    {
                        context.UserPermissions.Add(new UserPermission
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            PermissionId = perm.Id,
                            GrantedByUserId = userId,
                            GrantedAt = DateTimeOffset.UtcNow,
                            IsActive = true
                        });
                    }
                }
                await context.SaveChangesAsync();
            }


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
                var names = permissionNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (names.Count == 0) return;

                var perms = await context.Permissions
                    .Where(p => p.Name != null && names.Contains(p.Name))
                    .ToListAsync();
                var existingRolePerms = await context.RolePermissions
                    .Where(rp => rp.RoleId == roleId)
                    .ToListAsync();

                foreach (var perm in perms)
                {
                    if (!perm.IsActive)
                        continue;

                    var matches = existingRolePerms.Where(rp => rp.PermissionId == perm.Id).ToList();
                    if (matches.Any(rp => rp.IsActive))
                        continue;

                    var revoked = matches.OrderByDescending(rp => rp.RevokedAt).FirstOrDefault();
                    if (revoked != null)
                        IamRetention.Reactivate(revoked, grantedByUserId, DateTimeOffset.UtcNow);
                    else
                    {
                        context.RolePermissions.Add(new RolePermission
                        {
                            RoleId = roleId,
                            PermissionId = perm.Id,
                            GrantedByUserId = grantedByUserId,
                            GrantedAt = DateTimeOffset.UtcNow,
                            IsActive = true
                        });
                    }
                }
                await context.SaveChangesAsync();
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
                    roleDef.DescriptionAr);
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

            // صلاحيات القالب تبقى على دور SuperAdmin وعلى إسناده المباشر الحالي دون توسيعها إلى صلاحيات المجال
            await EnsurePermissionsAssignedToRoleAsync(
                roleSuperAdmin.Id,
                superAdmin.Id,
                templateBootstrapPermissions.Select(p => p.Name));

            // روابط Role–Permission من مصفوفة SRS §7.5 فقط (إضافة/إعادة تفعيل الناقص؛ بلا حذف روابط قائمة)
            var grantsByRole = MilitaryHealthSeedCatalog.GrantsByRoleName();
            foreach (var (roleName, permissionNames) in grantsByRole)
            {
                if (!seededRoles.TryGetValue(roleName, out var role))
                    throw new InvalidOperationException($"دور المصفوفة غير موجود في كتالوج الأدوار: {roleName}");

                await EnsurePermissionsAssignedToRoleAsync(role.Id, superAdmin.Id, permissionNames);
            }

            // مزامنة UserPermissions للمستخدم bootstrap مع كتالوج القالب فقط
            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, templateBootstrapPermissions);

            await CleanupObsoletePermissionsAsync(superAdmin.Id);

            await EnsurePermissionsAssignedToRoleAsync(
                roleSuperAdmin.Id,
                superAdmin.Id,
                templateBootstrapPermissions.Select(p => p.Name));
            if (grantsByRole.TryGetValue("SuperAdmin", out var superAdminMatrix))
                await EnsurePermissionsAssignedToRoleAsync(roleSuperAdmin.Id, superAdmin.Id, superAdminMatrix);
        }
    }
}
