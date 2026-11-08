using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalCenterMigrationCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historicalCenterMigrationCompletion",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    targetCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    verifiedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    verifiedByUserId = table.Column<string>(type: "text", nullable: false),
                    summaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    activeCenterCountAtVerification = table.Column<int>(type: "integer", nullable: false),
                    preExistingMultiCenterDetected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historicalCenterMigrationCompletion", x => x.id);
                    table.ForeignKey(
                        name: "FK_historicalCenterMigrationCompletion_medicalCenter_targetCen~",
                        column: x => x.targetCenterId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_historicalCenterMigrationCompletion_targetCenterId",
                schema: "core",
                table: "historicalCenterMigrationCompletion",
                column: "targetCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_historicalCenterMigrationCompletion_verifiedAt",
                schema: "core",
                table: "historicalCenterMigrationCompletion",
                column: "verifiedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historicalCenterMigrationCompletion",
                schema: "core");
        }
    }
}
