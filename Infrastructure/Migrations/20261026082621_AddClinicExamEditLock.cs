using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicExamEditLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clinicExamEditLock",
                schema: "clinical",
                columns: table => new
                {
                    lockId = table.Column<Guid>(type: "uuid", nullable: false),
                    memberId = table.Column<Guid>(type: "uuid", nullable: false),
                    centerId = table.Column<Guid>(type: "uuid", nullable: false),
                    clinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    lockedByUserId = table.Column<string>(type: "text", nullable: false),
                    lockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinicExamEditLock", x => x.lockId);
                    table.CheckConstraint("ckClinicExamEditLock1", "\"expiresAt\" > \"lockedAt\"");
                    table.ForeignKey(
                        name: "FK_clinicExamEditLock_Users_lockedByUserId",
                        column: x => x.lockedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicExamEditLock_clinic_clinicId",
                        column: x => x.clinicId,
                        principalSchema: "core",
                        principalTable: "clinic",
                        principalColumn: "clinicId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicExamEditLock_medicalCenter_centerId",
                        column: x => x.centerId,
                        principalSchema: "core",
                        principalTable: "medicalCenter",
                        principalColumn: "centerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinicExamEditLock_member_memberId",
                        column: x => x.memberId,
                        principalSchema: "patient",
                        principalTable: "member",
                        principalColumn: "memberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinicExamEditLock_centerId_clinicId_expiresAt",
                schema: "clinical",
                table: "clinicExamEditLock",
                columns: new[] { "centerId", "clinicId", "expiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_clinicExamEditLock_clinicId",
                schema: "clinical",
                table: "clinicExamEditLock",
                column: "clinicId");

            migrationBuilder.CreateIndex(
                name: "IX_clinicExamEditLock_lockedByUserId",
                schema: "clinical",
                table: "clinicExamEditLock",
                column: "lockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_clinicExamEditLock_memberId_clinicId",
                schema: "clinical",
                table: "clinicExamEditLock",
                columns: new[] { "memberId", "clinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinicExamEditLock",
                schema: "clinical");
        }
    }
}
