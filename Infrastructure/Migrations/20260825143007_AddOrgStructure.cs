using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "OrgUnitType",
                columns: table => new
                {
                    OrgUnitTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnitType", x => x.OrgUnitTypeId);
                });

            migrationBuilder.CreateTable(
                name: "OrgUnit",
                columns: table => new
                {
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", nullable: true),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    OrgUnitTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnit", x => x.OrgUnitId);
                    table.ForeignKey(
                        name: "FK_OrgUnit_OrgUnitType_OrgUnitTypeId",
                        column: x => x.OrgUnitTypeId,
                        principalTable: "OrgUnitType",
                        principalColumn: "OrgUnitTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrgUnit_OrgUnit_ParentOrgUnitId",
                        column: x => x.ParentOrgUnitId,
                        principalTable: "OrgUnit",
                        principalColumn: "OrgUnitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserOrgScope",
                columns: table => new
                {
                    UserOrgScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeMode = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOrgScope", x => x.UserOrgScopeId);
                    table.ForeignKey(
                        name: "FK_UserOrgScope_OrgUnit_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnit",
                        principalColumn: "OrgUnitId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserOrgScope_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserOrgScope_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserOrgScope_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnit_Code",
                table: "OrgUnit",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnit_OrgUnitTypeId_Status",
                table: "OrgUnit",
                columns: new[] { "OrgUnitTypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnit_ParentOrgUnitId",
                table: "OrgUnit",
                column: "ParentOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnit_ParentOrgUnitId_NameAr",
                table: "OrgUnit",
                columns: new[] { "ParentOrgUnitId", "NameAr" },
                unique: true,
                filter: "\"Status\" <> 'ARCHIVED'");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnitType_Code",
                table: "OrgUnitType",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOrgScope_AssignedByUserId",
                table: "UserOrgScope",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrgScope_OrgUnitId_Status",
                table: "UserOrgScope",
                columns: new[] { "OrgUnitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserOrgScope_PermissionId",
                table: "UserOrgScope",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrgScope_UserId_PermissionId_OrgUnitId_ScopeMode",
                table: "UserOrgScope",
                columns: new[] { "UserId", "PermissionId", "OrgUnitId", "ScopeMode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOrgScope_UserId_PermissionId_Status",
                table: "UserOrgScope",
                columns: new[] { "UserId", "PermissionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserOrgScope");

            migrationBuilder.DropTable(
                name: "OrgUnit");

            migrationBuilder.DropTable(
                name: "OrgUnitType");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
