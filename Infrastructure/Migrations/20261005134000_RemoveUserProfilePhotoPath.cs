using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// مسار الصورة الشخصية لم يعد على User — يُدار عبر جدول Attachments.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261005134000_RemoveUserProfilePhotoPath")]
    public class RemoveUserProfilePhotoPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePhotoPath",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilePhotoPath",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
