using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberImportBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memberImportBatch",
                schema: "patient",
                columns: table => new
                {
                    importBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    createdByUserId = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    contentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    validCount = table.Column<int>(type: "integer", nullable: false),
                    duplicateCount = table.Column<int>(type: "integer", nullable: false),
                    invalidCount = table.Column<int>(type: "integer", nullable: false),
                    validRowsJson = table.Column<string>(type: "text", nullable: false),
                    errorsJson = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberImportBatch", x => x.importBatchId);
                    table.CheckConstraint("ckMemberImportBatchDuplicateCount", "\"duplicateCount\" >= 0");
                    table.CheckConstraint("ckMemberImportBatchInvalidCount", "\"invalidCount\" >= 0");
                    table.CheckConstraint("ckMemberImportBatchStatus", "\"status\" IN ('Previewed','Committed','Cancelled')");
                    table.CheckConstraint("ckMemberImportBatchValidCount", "\"validCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_memberImportBatch_Users_createdByUserId",
                        column: x => x.createdByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberImportBatch_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memberImportBatch_centerId_createdAt",
                schema: "patient",
                table: "memberImportBatch",
                columns: new[] { "centerId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_memberImportBatch_centerId_status",
                schema: "patient",
                table: "memberImportBatch",
                columns: new[] { "centerId", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_memberImportBatch_createdByUserId",
                schema: "patient",
                table: "memberImportBatch",
                column: "createdByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memberImportBatch",
                schema: "patient");
        }
    }
}
