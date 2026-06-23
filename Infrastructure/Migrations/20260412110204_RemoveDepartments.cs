using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Departments_DepartmentId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId_DocumentNumber",
                table: "Documents");

            // إعادة ترقيم عالمي قبل فرض تفرد DocumentNumber
            migrationBuilder.Sql(@"
UPDATE ""Documents"" AS d
SET ""DocumentNumber"" = sub.rn
FROM (
  SELECT ""DocumentId"", ROW_NUMBER() OVER (ORDER BY ""DocumentId"") AS rn
  FROM ""Documents""
) AS sub
WHERE d.""DocumentId"" = sub.""DocumentId"";
");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentNumber",
                table: "Documents",
                column: "DocumentNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Rollback of RemoveDepartments is not supported.");
        }
    }
}
