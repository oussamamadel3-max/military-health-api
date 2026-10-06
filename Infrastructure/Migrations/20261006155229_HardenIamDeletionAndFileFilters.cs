using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenIamDeletionAndFileFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Permissions_PermissionId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Roles_RoleId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissions_Permissions_PermissionId",
                schema: "iam",
                table: "UserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissions_Users_UserId",
                schema: "iam",
                table: "UserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoleAssociations_Roles_RoleId",
                schema: "iam",
                table: "UserRoleAssociations");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoleAssociations_Users_UserId",
                schema: "iam",
                table: "UserRoleAssociations");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                schema: "iam",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Permissions_PermissionId",
                schema: "iam",
                table: "RolePermissions",
                column: "PermissionId",
                principalSchema: "iam",
                principalTable: "Permissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Roles_RoleId",
                schema: "iam",
                table: "RolePermissions",
                column: "RoleId",
                principalSchema: "iam",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissions_Permissions_PermissionId",
                schema: "iam",
                table: "UserPermissions",
                column: "PermissionId",
                principalSchema: "iam",
                principalTable: "Permissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissions_Users_UserId",
                schema: "iam",
                table: "UserPermissions",
                column: "UserId",
                principalSchema: "iam",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoleAssociations_Roles_RoleId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "RoleId",
                principalSchema: "iam",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoleAssociations_Users_UserId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "UserId",
                principalSchema: "iam",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE TRIGGER "trgUsersDeleteGuard"
                BEFORE DELETE ON iam."Users"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trgRolesDeleteGuard"
                BEFORE DELETE ON iam."Roles"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trgPermissionsDeleteGuard"
                BEFORE DELETE ON iam."Permissions"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trgRolePermissionsDeleteGuard"
                BEFORE DELETE ON iam."RolePermissions"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trgUserRoleAssociationsDeleteGuard"
                BEFORE DELETE ON iam."UserRoleAssociations"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trgUserPermissionsDeleteGuard"
                BEFORE DELETE ON iam."UserPermissions"
                FOR EACH ROW EXECUTE FUNCTION audit."preventHardDelete"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgUsersDeleteGuard" ON iam."Users";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgRolesDeleteGuard" ON iam."Roles";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgPermissionsDeleteGuard" ON iam."Permissions";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgRolePermissionsDeleteGuard" ON iam."RolePermissions";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgUserRoleAssociationsDeleteGuard" ON iam."UserRoleAssociations";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trgUserPermissionsDeleteGuard" ON iam."UserPermissions";""");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Permissions_PermissionId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Roles_RoleId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissions_Permissions_PermissionId",
                schema: "iam",
                table: "UserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissions_Users_UserId",
                schema: "iam",
                table: "UserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoleAssociations_Roles_RoleId",
                schema: "iam",
                table: "UserRoleAssociations");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoleAssociations_Users_UserId",
                schema: "iam",
                table: "UserRoleAssociations");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId",
                schema: "iam",
                table: "RolePermissions",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Permissions_PermissionId",
                schema: "iam",
                table: "RolePermissions",
                column: "PermissionId",
                principalSchema: "iam",
                principalTable: "Permissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Roles_RoleId",
                schema: "iam",
                table: "RolePermissions",
                column: "RoleId",
                principalSchema: "iam",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissions_Permissions_PermissionId",
                schema: "iam",
                table: "UserPermissions",
                column: "PermissionId",
                principalSchema: "iam",
                principalTable: "Permissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissions_Users_UserId",
                schema: "iam",
                table: "UserPermissions",
                column: "UserId",
                principalSchema: "iam",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoleAssociations_Roles_RoleId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "RoleId",
                principalSchema: "iam",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoleAssociations_Users_UserId",
                schema: "iam",
                table: "UserRoleAssociations",
                column: "UserId",
                principalSchema: "iam",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
