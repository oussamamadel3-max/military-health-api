using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// يحذف دوري DepartmentUser وBranchManager مرة واحدة. لا يُعاد التنفيذ عند كل تشغيل.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260928094000_RemoveDepartmentUserRole")]
    public class RemoveDepartmentUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "UserRoleAssociations"
                WHERE "RoleId" IN (
                    SELECT "Id" FROM "Roles"
                    WHERE "Name" IN ('DepartmentUser', 'BranchManager')
                       OR "NormalizedName" IN ('DEPARTMENTUSER', 'BRANCHMANAGER')
                );

                DELETE FROM "RolePermissions"
                WHERE "RoleId" IN (
                    SELECT "Id" FROM "Roles"
                    WHERE "Name" IN ('DepartmentUser', 'BranchManager')
                       OR "NormalizedName" IN ('DEPARTMENTUSER', 'BRANCHMANAGER')
                );

                DELETE FROM "RoleFieldPermissions"
                WHERE "RoleId" IN (
                    SELECT "Id" FROM "Roles"
                    WHERE "Name" IN ('DepartmentUser', 'BranchManager')
                       OR "NormalizedName" IN ('DEPARTMENTUSER', 'BRANCHMANAGER')
                );

                DELETE FROM "Roles"
                WHERE "Name" IN ('DepartmentUser', 'BranchManager')
                   OR "NormalizedName" IN ('DEPARTMENTUSER', 'BRANCHMANAGER');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
