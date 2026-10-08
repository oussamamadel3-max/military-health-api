using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
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

            async Task<Role> EnsureRoleAsync(string name, string normalized)
            {
                var role = await roleManager.Roles.FirstOrDefaultAsync(r => r.Name == name);
                if (role == null)
                {
                    role = new Role
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        NormalizedName = normalized,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };
                    var res = await roleManager.CreateAsync(role);
                    if (!res.Succeeded)
                        throw new Exception($"تعذر إنشاء الدور {name}: " + string.Join(", ", res.Errors.Select(e => e.Description)));
                }
                else if (!role.IsActive)
                {
                    role.IsActive = true;
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
                        var exists = await context.UserRoleAssociations
                            .AnyAsync(ura => ura.UserId == u.Id && ura.RoleId == r.Id);
                        if (!exists)
                        {
                            context.UserRoleAssociations.Add(new UserRoleAssociation
                            {
                                Id = Guid.NewGuid(),
                                UserId = u.Id,
                                RoleId = r.Id
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
                return perm;
            }

            /// <summary>
            /// يعيد تسمية صلاحية قديمة إلى الاسم الجديد مع الحفاظ على إسنادات الأدوار/المستخدمين.
            /// إن وُجد الاسمان معاً تُنقل الإسنادات ثم تُحذف القديمة.
            /// </summary>
            async Task MigratePermissionNameAsync(string oldName, string newName, string? newDesc)
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
                var existingNewRoleIds = await context.RolePermissions
                    .Where(rp => rp.PermissionId == newPerm.Id)
                    .Select(rp => rp.RoleId)
                    .ToListAsync();
                var existingNewRoleSet = existingNewRoleIds.ToHashSet();

                foreach (var link in oldRoleLinks)
                {
                    if (existingNewRoleSet.Contains(link.RoleId))
                        context.RolePermissions.Remove(link);
                    else
                        link.PermissionId = newPerm.Id;
                }

                var oldUserLinks = await context.UserPermissions
                    .Where(up => up.PermissionId == oldPerm.Id)
                    .ToListAsync();
                var existingNewUserIds = await context.UserPermissions
                    .Where(up => up.PermissionId == newPerm.Id)
                    .Select(up => up.UserId)
                    .ToListAsync();
                var existingNewUserSet = existingNewUserIds.ToHashSet();

                foreach (var link in oldUserLinks)
                {
                    if (existingNewUserSet.Contains(link.UserId))
                        context.UserPermissions.Remove(link);
                    else
                        link.PermissionId = newPerm.Id;
                }

                context.Permissions.Remove(oldPerm);
                if (!string.IsNullOrWhiteSpace(newDesc) && string.IsNullOrWhiteSpace(newPerm.Description))
                    newPerm.Description = newDesc;

                await context.SaveChangesAsync();
            }

            async Task EnsurePermissionsAndAssignToUserAsync(string userId, IEnumerable<(string Name, string? Desc)> list)
            {
                foreach (var (name, desc) in list)
                {
                    var perm = await EnsurePermissionAsync(name, desc);

                    var has = await context.UserPermissions
                        .AnyAsync(up => up.UserId == userId && up.PermissionId == perm.Id);

                    if (!has)
                    {
                        context.UserPermissions.Add(new UserPermission
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            PermissionId = perm.Id
                        });
                    }
                }
                await context.SaveChangesAsync();
            }


            async Task EnsureOrgUnitTypesAsync()
            {
                var types = new (string Code, string NameAr, short Order)[]
                {
                    ("HQ", "المقر الرئيسي", 1),
                    ("DIRECTORATE", "مديرية", 2),
                    ("DEPARTMENT", "إدارة", 3),
                    ("BRANCH", "فرع", 4),
                    ("UNIT", "وحدة", 5)
                };
                foreach (var (code, name, order) in types)
                {
                    if (!await context.OrgUnitTypes.AnyAsync(x => x.Code == code))
                        context.OrgUnitTypes.Add(new OrgUnitType
                        {
                            OrgUnitTypeId = Guid.NewGuid(), Code = code, NameAr = name,
                            DisplayOrder = order, IsActive = true
                        });
                }
                await context.SaveChangesAsync();
            }

            async Task CleanupObsoletePermissionsAsync()
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
                        (p.Name != null && p.Name.StartsWith("ReportManagement.")))
                    .ToListAsync();

                if (obsolete.Count == 0) return;

                var obsoleteIds = obsolete.Select(p => p.Id).ToList();

                var userPerms = await context.UserPermissions
                    .Where(up => up.PermissionId.HasValue && obsoleteIds.Contains(up.PermissionId.Value))
                    .ToListAsync();
                if (userPerms.Count > 0)
                    context.UserPermissions.RemoveRange(userPerms);

                var rolePerms = await context.RolePermissions
                    .Where(rp => obsoleteIds.Contains(rp.PermissionId))
                    .ToListAsync();
                if (rolePerms.Count > 0)
                    context.RolePermissions.RemoveRange(rolePerms);

                var orgScopes = await context.UserOrgScopes
                    .Where(s => obsoleteIds.Contains(s.PermissionId))
                    .ToListAsync();
                context.UserOrgScopes.RemoveRange(orgScopes);

                context.Permissions.RemoveRange(obsolete);
                await context.SaveChangesAsync();
            }

            async Task EnsureAllPermissionsAssignedToRoleAsync(string roleId, IEnumerable<(string Name, string? Desc)> catalog)
            {
                foreach (var (name, desc) in catalog)
                    await EnsurePermissionAsync(name, desc);

                var allPerms = await context.Permissions.AsNoTracking().ToListAsync();
                var existingRolePermIds = await context.RolePermissions
                    .Where(rp => rp.RoleId == roleId)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync();
                var existingSet = existingRolePermIds.ToHashSet();

                foreach (var perm in allPerms)
                {
                    if (existingSet.Contains(perm.Id)) continue;
                    context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = perm.Id
                    });
                }
                await context.SaveChangesAsync();
            }


            // ============================
            // 2) الأدوار
            // ============================
            var roleSuperAdmin = await EnsureRoleAsync("SuperAdmin", "SUPERADMIN");

            // ============================
            // 3) الصلاحيات (كتالوج النظام — تُسند لدور SuperAdmin فقط عبر Seed)
            // ============================
            var systemPermissions = new (string Name, string? Desc)[]
            {
                ("UserManagement.Read", "قراءة بيانات المستخدمين"),
                ("UserManagement.Create", "إنشاء مستخدمين جدد"),
                ("UserManagement.Edit", "تعديل بيانات المستخدمين"),
                ("UserManagement.Delete", "حذف المستخدمين"),
                ("UserManagement.ChangeStatus", "تغيير حالة المستخدمين"),
                ("UserManagement.AssignRoles", "إسناد أدوار للمستخدمين"),
                ("UserManagement.AssignPermissions", "إسناد صلاحيات مباشرة للمستخدمين"),
                ("UserManagement.ResetPassword", "إعادة تعيين كلمة مرور مستخدم"),
                ("UserManagement.RemoveFieldPermissions", "إزالة صلاحيات الحقول عن مستخدم"),

                ("RoleManagement.Read", "قراءة الأدوار"),
                ("RoleManagement.Create", "إنشاء أدوار"),
                ("RoleManagement.Edit", "تعديل بيانات الأدوار"),
                ("RoleManagement.Delete", "حذف الأدوار"),
                ("RoleManagement.ChangeStatus", "تغيير حالة الأدوار"),
                ("RoleManagement.AssignPermissions", "إسناد صلاحيات للأدوار"),

                ("PermissionManagement.Read", "قراءة تعريفات الصلاحيات"),
                ("PermissionManagement.ChangeStatus", "تغيير حالة الصلاحيات"),

                ("FieldPermissionManagement.Read", "قراءة صلاحيات الحقول"),
                ("FieldPermissionManagement.Create", "إنشاء صلاحيات حقول"),
                ("FieldPermissionManagement.Edit", "تعديل صلاحيات الحقول"),
                ("FieldPermissionManagement.Delete", "حذف صلاحيات الحقول"),
                ("FieldPermissionManagement.ChangeAccessType", "تغيير نوع وصول صلاحية الحقل"),

                ("AuditLog.Read", "قراءة سجلات التدقيق"),
                ("AuditLog.Export", "تصدير تقرير السجل"),

                ("Dashboard.Read", "قراءة لوحة التحكم"),
                ("Dashboard.Export", "تصدير تقرير لوحة التحكم"),
                ("OrgStructure.Read", "قراءة الهيكل التنظيمي"),
                ("OrgStructure.Create", "إنشاء وحدات تنظيمية"),
                ("OrgStructure.Edit", "تعديل الوحدات التنظيمية"),
                ("OrgStructure.Delete", "حذف أو أرشفة الوحدات التنظيمية"),
                ("UserOrgScope.Read", "قراءة نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Create", "إنشاء نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Edit", "تعديل نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Delete", "حذف نطاقات المستخدمين التنظيمية")
            };

            await EnsureOrgUnitTypesAsync();

            // ============================
            // 5) المستخدمون
            // ============================

            // SuperAdmin (بدون فرع/قسم)
            var superAdmin = await EnsureUserAsync(
                userName: "superAdmin",
                email: "superAdmin@gmail.com",
                password: "cZf0bYa4HjMmqewUk2BM7#@3",
                fullName: "Super Administrator",
                attachRoles: new[] { roleSuperAdmin }
            );

            // أنشئ الصلاحيات وأسند كل صلاحيات النظام لدور SuperAdmin (إضافة الناقص فقط)
            await MigratePermissionNameAsync("ReportsAuditLogs.Export", "AuditLog.Export", "تصدير تقرير السجل");
            await MigratePermissionNameAsync("ReportsDashboard.Export", "Dashboard.Export", "تصدير تقرير لوحة التحكم");
            await EnsureAllPermissionsAssignedToRoleAsync(roleSuperAdmin.Id, systemPermissions);

            // مزامنة UserPermissions للمستخدم seed مع الكتالوج (إضافة الناقص فقط؛ بلا إعادة ضبط كلمة المرور)
            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, systemPermissions);

            // تنظيف صلاحيات قديمة بعد إضافة البدائل
            await CleanupObsoletePermissionsAsync();

            // بعد التنظيف أعد ضمان إسناد كل المتبقي لدور SuperAdmin
            await EnsureAllPermissionsAssignedToRoleAsync(roleSuperAdmin.Id, systemPermissions);
        }
    }
}
