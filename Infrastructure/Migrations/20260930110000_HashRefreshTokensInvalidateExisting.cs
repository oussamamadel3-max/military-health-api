using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokensInvalidateExisting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // القيم القديمة مخزّنة كنص واضح ولن تطابق SHA-256؛ إبطال الكل لإجبار إعادة تسجيل الدخول.
            migrationBuilder.Sql("""UPDATE "UserRefreshTokens" SET "IsRevoked" = TRUE WHERE "IsRevoked" = FALSE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لا يمكن استعادة التوكنات الملغاة.
        }
    }
}
