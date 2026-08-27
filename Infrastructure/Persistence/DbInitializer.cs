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

            async Task EnsurePermissionsSetAsync(IEnumerable<(string Name, string? Desc)> list)
            {
                foreach (var (name, desc) in list)
                    await EnsurePermissionAsync(name, desc);
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


            // ============================
            // 2) الأدوار
            // ============================
            var roleSuperAdmin = await EnsureRoleAsync("SuperAdmin", "SUPERADMIN");
            var roleBranchManager = await EnsureRoleAsync("BranchManager", "BRANCHMANAGER");
            _ = await EnsureRoleAsync("DepartmentUser", "DEPARTMENTUSER");

            // ============================
            // 3) الصلاحيات
            // ============================

            // 3.1) صلاحيات “تُنشأ وتُمنح” للـ SuperAdmin فقط
            var permsForSuperAdmin = new (string Name, string? Desc)[]
            {
                // User Management
                ("UserManagement.Read",   "قراءة بيانات المستخدمين"),
                ("UserManagement.Create", "إنشاء مستخدمين جدد"),
                ("UserManagement.Edit",   "تعديل بيانات المستخدمين"),
                ("UserManagement.Delete", "حذف المستخدمين"),

                // Audit
                ("AuditLog.Read", "قراءة سجلات التدقيق"),

                // عامة
                ("SuperAdmin", "مدير النظام"),
                ("Dashboard.Read", "قراءة لوحة التحكم"),
                ("ReportsDocuments.Export", "تصدير تقرير الوثائق"),
                ("ReportsAuditLogs.Export", "تصدير تقرير السجل"),
                ("ReportsDashboard.Export", "تصدير تقرير لوحة التحكم"),
                ("OrgStructure.Read", "قراءة الهيكل التنظيمي"),
                ("OrgStructure.Create", "إنشاء وحدات تنظيمية"),
                ("OrgStructure.Edit", "تعديل الوحدات التنظيمية"),
                ("OrgStructure.Delete", "حذف أو أرشفة الوحدات التنظيمية"),
                ("UserOrgScope.Read", "قراءة نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Create", "إنشاء نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Edit", "تعديل نطاقات المستخدمين التنظيمية"),
                ("UserOrgScope.Delete", "حذف نطاقات المستخدمين التنظيمية")
            };

            // 3.2) صلاحيات عامة: تُنشأ فقط
            var publicPermsOnly = new (string Name, string? Desc)[]
            {
                // ========== Document CRUD =================
                ("Document.Read",   "قراءة الوثائق"),
                ("Document.Create", "إنشاء وثائق جديدة"),
                ("Document.Edit",   "تعديل الوثائق"),
                ("Document.Delete", "حذف الوثائق"),

                // (موجودة ولكن لن تُمنح للسوبر)
                ("BranchManager", "مدير الفرع"),
            };

            // إنشاء جميع الصلاحيات العامة
            await EnsurePermissionsSetAsync(publicPermsOnly);
            await EnsureOrgUnitTypesAsync();

            // ============================
            // 5) المستخدمون
            // ============================

            // SuperAdmin (بدون فرع/قسم)
            var superAdmin = await EnsureUserAsync(
                userName: "superAdmin",
                email: "superAdmin@gmail.com",
                password: "Admin@123",
                fullName: "Super Administrator",
                attachRoles: new[] { roleSuperAdmin }
            );

            // امنح SuperAdmin الصلاحيات الأساسية
            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, permsForSuperAdmin);

            // امنحه جميع صلاحيات CRUD العامة بدون BranchManager
            var publicCrudPermsForSuperAdmin = publicPermsOnly
                .Where(p => !string.Equals(p.Name, "BranchManager", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, publicCrudPermsForSuperAdmin);


        }
    }
}
