using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// الصورة الشخصية تُعامل كمرفق عادي — إزالة IsProfilePhoto والتمييز الخاص.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261005140000_RemoveAttachmentIsProfilePhoto")]
    public class RemoveAttachmentIsProfilePhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Attachments_OwnerType_OwnerId_IsProfilePhoto";
                """);

            migrationBuilder.DropColumn(
                name: "IsProfilePhoto",
                table: "Attachments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProfilePhoto",
                table: "Attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Attachments_OwnerType_OwnerId_IsProfilePhoto"
                    ON "Attachments" ("OwnerType", "OwnerId", "IsProfilePhoto")
                    WHERE "IsProfilePhoto" = true;
                """);
        }
    }
}
