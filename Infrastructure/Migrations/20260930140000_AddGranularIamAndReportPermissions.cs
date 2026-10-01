using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// ترحيل بيانات: إدراج صلاحيات IAM/التدقيق/التقارير الجديدة بشكل idempotent.
    /// يُحضَّر للمراجعة؛ لا يُفترض تطبيقه على الإنتاج من هذا المستودع مباشرة.
    /// </summary>
    public partial class AddGranularIamAndReportPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            void Upsert(string name, string description)
            {
                migrationBuilder.Sql($"""
INSERT INTO "Permissions" ("Id", "Name", "Description", "IsActive")
SELECT gen_random_uuid(), '{name}', '{description}', TRUE
WHERE NOT EXISTS (SELECT 1 FROM "Permissions" WHERE "Name" = '{name}');
""");
            }

            Upsert("UserManagement.ChangeStatus", "تغيير حالة المستخدمين");
            Upsert("UserManagement.AssignRoles", "إسناد أدوار للمستخدمين");
            Upsert("UserManagement.AssignPermissions", "إسناد صلاحيات مباشرة للمستخدمين");
            Upsert("UserManagement.ResetPassword", "إعادة تعيين كلمة مرور مستخدم");
            Upsert("UserManagement.RemoveFieldPermissions", "إزالة صلاحيات الحقول عن مستخدم");

            Upsert("RoleManagement.Read", "قراءة الأدوار");
            Upsert("RoleManagement.Create", "إنشاء أدوار");
            Upsert("RoleManagement.Edit", "تعديل بيانات الأدوار");
            Upsert("RoleManagement.Delete", "حذف الأدوار");
            Upsert("RoleManagement.ChangeStatus", "تغيير حالة الأدوار");
            Upsert("RoleManagement.AssignPermissions", "إسناد صلاحيات للأدوار");

            Upsert("PermissionManagement.Read", "قراءة تعريفات الصلاحيات");
            Upsert("PermissionManagement.ChangeStatus", "تغيير حالة الصلاحيات");

            Upsert("FieldPermissionManagement.Read", "قراءة صلاحيات الحقول");
            Upsert("FieldPermissionManagement.Create", "إنشاء صلاحيات حقول");
            Upsert("FieldPermissionManagement.Edit", "تعديل صلاحيات الحقول");
            Upsert("FieldPermissionManagement.Delete", "حذف صلاحيات الحقول");
            Upsert("FieldPermissionManagement.ChangeAccessType", "تغيير نوع وصول صلاحية الحقل");

            Upsert("AuditLog.Export", "تصدير تقرير السجل");
            Upsert("Dashboard.Export", "تصدير تقرير لوحة التحكم");

            migrationBuilder.Sql("""
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM "Permissions" WHERE "Name" = 'ReportsAuditLogs.Export')
     AND NOT EXISTS (SELECT 1 FROM "Permissions" WHERE "Name" = 'AuditLog.Export') THEN
    UPDATE "Permissions" SET "Name" = 'AuditLog.Export', "Description" = 'تصدير تقرير السجل'
    WHERE "Name" = 'ReportsAuditLogs.Export';
  END IF;

  IF EXISTS (SELECT 1 FROM "Permissions" WHERE "Name" = 'ReportsDashboard.Export')
     AND NOT EXISTS (SELECT 1 FROM "Permissions" WHERE "Name" = 'Dashboard.Export') THEN
    UPDATE "Permissions" SET "Name" = 'Dashboard.Export', "Description" = 'تصدير تقرير لوحة التحكم'
    WHERE "Name" = 'ReportsDashboard.Export';
  END IF;
END $$;
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لا نحذف الصلاحيات في Down لتجنب كسر التخصيصات اليدوية.
        }
    }
}
