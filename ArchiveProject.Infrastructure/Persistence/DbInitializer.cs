using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiveProject.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainDbContext>();
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

            async Task<Branch> EnsureBranchAsync(string code, string name, string? desc = null)
            {
                var b = await context.Branchs.FirstOrDefaultAsync(x => x.Code == code);
                if (b == null)
                {
                    b = new Branch
                    {
                        Code = code,
                        BranchName = name,
                        Description = desc ?? string.Empty,
                        CreatedAt = DateTime.Now
                    };
                    context.Branchs.Add(b);
                    await context.SaveChangesAsync();
                }
                return b;
            }

            async Task<Department> EnsureDeptAsync(long branchId, string code, string name, bool isActive = true)
            {
                var d = await context.Departments.FirstOrDefaultAsync(x => x.BranchId == branchId && x.Code == code);
                if (d == null)
                {
                    d = new Department
                    {
                        DepartmentName = name,
                        Description = string.Empty,
                        Code = code,
                        IsActive = isActive,
                        BranchId = branchId,
                        CreatedAt = DateTime.Now
                    };
                    context.Departments.Add(d);
                    await context.SaveChangesAsync();
                }
                return d;
            }

            async Task<User> EnsureUserAsync(
                string userName, string email, string password,
                string fullName,
                long? branchId = null, long? departmentId = null,
                IEnumerable<Role>? attachRoles = null)
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
                        FullName = fullName,
                        BranchId = branchId,
                        DepartmentId = departmentId
                    };
                    var res = await userManager.CreateAsync(u, password);
                    if (!res.Succeeded)
                        throw new Exception($"تعذر إنشاء المستخدم {userName}: " + string.Join(", ", res.Errors.Select(e => e.Description)));
                }
                else
                {
                    bool changed = false;
                    if (u.BranchId != branchId) { u.BranchId = branchId; changed = true; }
                    if (u.DepartmentId != departmentId) { u.DepartmentId = departmentId; changed = true; }
                    if (changed) await userManager.UpdateAsync(u);
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

            async Task<DocumentType> EnsureDocumentTypeAsync(string name, string allowedExtensionsCsv)
            {
                var dt = await context.DocumentTypes.FirstOrDefaultAsync(x => x.DocumentTypeName == name);
                if (dt == null)
                {
                    dt = new DocumentType
                    {
                        DocumentTypeName = name,
                        AllowedExtensions = allowedExtensionsCsv
                    };
                    context.DocumentTypes.Add(dt);
                    await context.SaveChangesAsync();
                }
                return dt;
            }

            async Task<ArchiveGroup> EnsureArchiveGroupAsync(long departmentId, string title, long archiveGroupNumber, string userId)
            {
                var g = await context.ArchiveGroups.FirstOrDefaultAsync(x =>
                    x.DepartmentId == departmentId &&
                    x.ArchiveGroupNumber == archiveGroupNumber);

                if (g == null)
                {
                    g = new ArchiveGroup
                    {
                        DepartmentId = departmentId,
                        UserId = userId,
                        Title = title,
                        ArchiveGroupNumber = archiveGroupNumber,
                        CreatedAt = DateTime.Now
                    };
                    context.ArchiveGroups.Add(g);
                    await context.SaveChangesAsync();
                }
                else
                {
                    bool changed = false;
                    if (g.Title != title) { g.Title = title; changed = true; }
                    if (g.UserId != userId) { g.UserId = userId; changed = true; }
                    if (changed) { context.ArchiveGroups.Update(g); await context.SaveChangesAsync(); }
                }
                return g;
            }

            async Task<Document> EnsureDocumentAsync(
                long departmentId,
                string userId,
                long? documentTypeId,
                long? archiveGroupId,
                long documentNumber,
                string title,
                string? description,
                string filePath,
                long? fileSize = null)
            {
                var doc = await context.Documents.FirstOrDefaultAsync(x =>
                    x.DepartmentId == departmentId &&
                    x.DocumentNumber == documentNumber);

                if (doc == null)
                {
                    doc = new Document
                    {
                        DepartmentId = departmentId,
                        UserId = userId,
                        DocumentTypeId = documentTypeId,
                        ArchiveGroupId = archiveGroupId,
                        DocumentNumber = documentNumber,
                        Title = title,
                        Description = description ?? string.Empty,
                        CreatedAt = DateTime.Now
                    };
                    context.Documents.Add(doc);
                    await context.SaveChangesAsync();
                }
                else
                {
                    bool changed = false;
                    if (doc.Title != title) { doc.Title = title; changed = true; }
                    if (doc.Description != (description ?? string.Empty)) { doc.Description = description ?? string.Empty; changed = true; }
                    if (doc.DocumentTypeId != documentTypeId) { doc.DocumentTypeId = documentTypeId; changed = true; }
                    if (doc.ArchiveGroupId != archiveGroupId) { doc.ArchiveGroupId = archiveGroupId; changed = true; }
                    if (changed) { context.Documents.Update(doc); await context.SaveChangesAsync(); }
                }
                return doc;
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

            // ============================
            // 2) الأدوار
            // ============================
            var roleSuperAdmin = await EnsureRoleAsync("SuperAdmin", "SUPERADMIN");
            var roleBranchManager = await EnsureRoleAsync("BranchManager", "BRANCHMANAGER");
            var roleDepartmentUser = await EnsureRoleAsync("DepartmentUser", "DEPARTMENTUSER");

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

                // علامة عامة (اختياري)
                ("SuperAdmin", "مدير النظام"),
            };

            // 3.2) صلاحيات عامة: تُنشأ فقط (لا تُمنح لأحد افتراضياً)
            var publicPermsOnly = new (string Name, string? Desc)[]
            {
                // ========== Branch CRUD ===================
                ("Branch.Read",   "قراءة بيانات الفروع"),
                ("Branch.Create", "إنشاء فروع جديدة"),
                ("Branch.Edit",   "تعديل بيانات الفروع"),
                ("Branch.Delete", "حذف الفروع"),

                // ========== Department CRUD ===============
                ("Department.Read",   "قراءة بيانات الأقسام"),
                ("Department.Create", "إنشاء أقسام جديدة"),
                ("Department.Edit",   "تعديل بيانات الأقسام"),
                ("Department.Delete", "حذف الأقسام"),

                // ========== ArchiveGroup CRUD =============
                ("ArchiveGroup.Read",   "قراءة المجموعات الأرشيفية"),
                ("ArchiveGroup.Create", "إنشاء مجموعات أرشيفية"),
                ("ArchiveGroup.Edit",   "تعديل المجموعات الأرشيفية"),
                ("ArchiveGroup.Delete", "حذف المجموعات الأرشيفية"),

                // ========== DocumentType CRUD =============
                ("DocumentType.Read",   "قراءة أنواع الوثائق"),
                ("DocumentType.Create", "إنشاء أنواع وثائق جديدة"),
                ("DocumentType.Edit",   "تعديل أنواع الوثائق"),
                ("DocumentType.Delete", "حذف أنواع الوثائق"),

                // ========== Document CRUD =================
                ("Document.Read",   "قراءة الوثائق"),
                ("Document.Create", "إنشاء وثائق جديدة"),
                ("Document.Edit",   "تعديل الوثائق"),
                ("Document.Delete", "حذف الوثائق"),

                // صلاحية عامة باسم BranchManager (اختياري استخدامها)
                ("BranchManager", "مدير الفرع"),
            };

            // أنشئ العامة فقط
            await EnsurePermissionsSetAsync(publicPermsOnly);

            // ============================
            // 4) الفروع والأقسام
            // ============================
            var br1 = await EnsureBranchAsync("BR-001", "الفرع الشمالي");
            var br2 = await EnsureBranchAsync("BR-002", "الفرع الجنوبي");

            var br1_d1 = await EnsureDeptAsync(br1.BranchId, "ACCT", "قسم المحاسبة");
            var br1_d2 = await EnsureDeptAsync(br1.BranchId, "HR", "قسم الموارد البشرية");
            var br2_d1 = await EnsureDeptAsync(br2.BranchId, "OPS", "قسم العمليات");
            var br2_d2 = await EnsureDeptAsync(br2.BranchId, "ARCH", "قسم الأرشيف");

            // ============================
            // 5) المستخدمون
            // ============================

            // SuperAdmin (بدون فرع/قسم)
            var superAdmin = await EnsureUserAsync(
                userName: "superAdmin",
                email: "superAdmin@gmail.com",
                password: "Admin@123",
                fullName: "Super Administrator",
                branchId: null,
                departmentId: null,
                attachRoles: new[] { roleSuperAdmin }
            );

            // امنح SuperAdmin الصلاحيات الأساسية فقط
            await EnsurePermissionsAndAssignToUserAsync(superAdmin.Id, permsForSuperAdmin);

            // مدراء الفروع (واحد لكل فرع)
            var br1_mgr = await EnsureUserAsync("br1.manager", "br1.manager@gmail.com", "manager@123", "مدير فرع 1",
                branchId: br1.BranchId, departmentId: null, attachRoles: new[] { roleBranchManager });

            var br2_mgr = await EnsureUserAsync("br2.manager", "br2.manager@gmail.com", "manager@123", "مدير فرع 2",
                branchId: br2.BranchId, departmentId: null, attachRoles: new[] { roleBranchManager });

            // موظفو الأقسام (واحد لكل قسم)
            var br1_d1_emp = await EnsureUserAsync("br1.acct.user", "br1.acct.user@gmail.com", "user@123", "موظف محاسبة - فرع 1",
                branchId: br1.BranchId, departmentId: br1_d1.DepartmentId, attachRoles: new[] { roleDepartmentUser });

            var br1_d2_emp = await EnsureUserAsync("br1.hr.user", "br1.hr.user@gmail.com", "user@123", "موظف موارد بشرية - فرع 1",
                branchId: br1.BranchId, departmentId: br1_d2.DepartmentId, attachRoles: new[] { roleDepartmentUser });

            var br2_d1_emp = await EnsureUserAsync("br2.ops.user", "br2.ops.user@gmail.com", "user@123", "موظف عمليات - فرع 2",
                branchId: br2.BranchId, departmentId: br2_d1.DepartmentId, attachRoles: new[] { roleDepartmentUser });

            var br2_d2_emp = await EnsureUserAsync("br2.arch.user", "br2.arch.user@gmail.com", "user@123", "موظف أرشيف - فرع 2",
                branchId: br2.BranchId, departmentId: br2_d2.DepartmentId, attachRoles: new[] { roleDepartmentUser });

            // ============================
            // 6) DocumentTypes
            // ============================
            var dt_invoice = await EnsureDocumentTypeAsync("فاتورة", ".pdf,.jpg,.png");
            var dt_contract = await EnsureDocumentTypeAsync("عقد", ".pdf,.docx");
            var dt_correspond = await EnsureDocumentTypeAsync("مراسلات", ".pdf,.docx,.msg");

            // ============================
            // 7) ArchiveGroups (واحد لكل قسم)
            // ============================
            var br1_acct_grp = await EnsureArchiveGroupAsync(br1_d1.DepartmentId, "أرشيف فواتير المحاسبة", 1, br1_d1_emp.Id);
            var br1_hr_grp = await EnsureArchiveGroupAsync(br1_d2.DepartmentId, "أرشيف عقود الموارد البشرية", 1, br1_d2_emp.Id);
            var br2_ops_grp = await EnsureArchiveGroupAsync(br2_d1.DepartmentId, "أرشيف مراسلات العمليات", 1, br2_d1_emp.Id);
            var br2_arch_grp = await EnsureArchiveGroupAsync(br2_d2.DepartmentId, "الأرشيف العام", 1, br2_d2_emp.Id);

            // ============================
            // 8) Documents (وثيقتان لكل مجموعة)
            // ============================
            // فرع 1 - محاسبة
            _ = await EnsureDocumentAsync(
                departmentId: br1_d1.DepartmentId,
                userId: br1_d1_emp.Id,
                documentTypeId: dt_invoice.DocumentTypeId,
                archiveGroupId: br1_acct_grp.ArchiveGroupId,
                documentNumber: 1001,
                title: "فاتورة-001",
                description: "فاتورة شراء معدات",
                filePath: "/storage/br1/acct/invoices/inv-001.pdf",
                fileSize: 204800);

            _ = await EnsureDocumentAsync(
                departmentId: br1_d1.DepartmentId,
                userId: br1_d1_emp.Id,
                documentTypeId: dt_invoice.DocumentTypeId,
                archiveGroupId: br1_acct_grp.ArchiveGroupId,
                documentNumber: 1002,
                title: "فاتورة-002",
                description: "فاتورة شراء قرطاسية",
                filePath: "/storage/br1/acct/invoices/inv-002.pdf",
                fileSize: 102400);

            // فرع 1 - HR
            _ = await EnsureDocumentAsync(
                departmentId: br1_d2.DepartmentId,
                userId: br1_d2_emp.Id,
                documentTypeId: dt_contract.DocumentTypeId,
                archiveGroupId: br1_hr_grp.ArchiveGroupId,
                documentNumber: 1001,
                title: "عقد-موظف-001",
                description: "عقد توظيف",
                filePath: "/storage/br1/hr/contracts/emp-001.pdf",
                fileSize: 307200);

            _ = await EnsureDocumentAsync(
                departmentId: br1_d2.DepartmentId,
                userId: br1_d2_emp.Id,
                documentTypeId: dt_contract.DocumentTypeId,
                archiveGroupId: br1_hr_grp.ArchiveGroupId,
                documentNumber: 1002,
                title: "عقد-موظف-002",
                description: "ملحق عقد",
                filePath: "/storage/br1/hr/contracts/emp-002.pdf",
                fileSize: 256000);

            // فرع 2 - OPS
            _ = await EnsureDocumentAsync(
                departmentId: br2_d1.DepartmentId,
                userId: br2_d1_emp.Id,
                documentTypeId: dt_correspond.DocumentTypeId,
                archiveGroupId: br2_ops_grp.ArchiveGroupId,
                documentNumber: 1001,
                title: "مراسلة-001",
                description: "مراسلة واردة",
                filePath: "/storage/br2/ops/correspondence/001.pdf",
                fileSize: 128000);

            _ = await EnsureDocumentAsync(
                departmentId: br2_d1.DepartmentId,
                userId: br2_d1_emp.Id,
                documentTypeId: dt_correspond.DocumentTypeId,
                archiveGroupId: br2_ops_grp.ArchiveGroupId,
                documentNumber: 1002,
                title: "مراسلة-002",
                description: "مراسلة صادرة",
                filePath: "/storage/br2/ops/correspondence/002.pdf",
                fileSize: 96000);

            // فرع 2 - ARCH (الأرشيف العام)
            _ = await EnsureDocumentAsync(
                departmentId: br2_d2.DepartmentId,
                userId: br2_d2_emp.Id,
                documentTypeId: dt_correspond.DocumentTypeId,
                archiveGroupId: br2_arch_grp.ArchiveGroupId,
                documentNumber: 1001,
                title: "تعميم-001",
                description: "تعميم داخلي",
                filePath: "/storage/br2/arch/general/circ-001.pdf",
                fileSize: 110000);

            _ = await EnsureDocumentAsync(
                departmentId: br2_d2.DepartmentId,
                userId: br2_d2_emp.Id,
                documentTypeId: dt_contract.DocumentTypeId,
                archiveGroupId: br2_arch_grp.ArchiveGroupId,
                documentNumber: 1002,
                title: "اتفاقية-001",
                description: "اتفاقية تعاون",
                filePath: "/storage/br2/arch/general/agreement-001.pdf",
                fileSize: 220000);

            // انتهى
        }
    }
}
