using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleOperationalScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Scope",
                schema: "iam",
                table: "Roles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Scope",
                schema: "iam",
                table: "Roles",
                column: "Scope");

            // تثبيت نطاق أدوار الكتالوج المركزية (RoleScope.Central = 1)
            migrationBuilder.Sql(
                """
                UPDATE iam."Roles"
                SET "Scope" = 1
                WHERE "Name" IN ('SuperAdmin', 'Auditor');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_Scope",
                schema: "iam",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Scope",
                schema: "iam",
                table: "Roles");
        }
    }
}
