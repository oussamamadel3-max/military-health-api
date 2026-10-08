using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveArchiveGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_ArchiveGroups_ArchiveGroupId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ArchiveGroupId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ArchiveGroupId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "ArchiveGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ArchiveGroupId",
                table: "Documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchiveGroups",
                columns: table => new
                {
                    ArchiveGroupId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArchiveGroupNumber = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveGroups", x => x.ArchiveGroupId);
                    table.ForeignKey(
                        name: "FK_ArchiveGroups_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArchiveGroups_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ArchiveGroupId",
                table: "Documents",
                column: "ArchiveGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveGroups_DepartmentId",
                table: "ArchiveGroups",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveGroups_DepartmentId_ArchiveGroupNumber",
                table: "ArchiveGroups",
                columns: new[] { "DepartmentId", "ArchiveGroupNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveGroups_UserId",
                table: "ArchiveGroups",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_ArchiveGroups_ArchiveGroupId",
                table: "Documents",
                column: "ArchiveGroupId",
                principalTable: "ArchiveGroups",
                principalColumn: "ArchiveGroupId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
