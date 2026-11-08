using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventTargetUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "targetUserId",
                schema: "audit",
                table: "event",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_targetUserId_occurredAt",
                schema: "audit",
                table: "event",
                columns: new[] { "targetUserId", "occurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_event_Users_targetUserId",
                schema: "audit",
                table: "event",
                column: "targetUserId",
                principalSchema: "iam",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_Users_targetUserId",
                schema: "audit",
                table: "event");

            migrationBuilder.DropIndex(
                name: "IX_event_targetUserId_occurredAt",
                schema: "audit",
                table: "event");

            migrationBuilder.DropColumn(
                name: "targetUserId",
                schema: "audit",
                table: "event");
        }
    }
}
