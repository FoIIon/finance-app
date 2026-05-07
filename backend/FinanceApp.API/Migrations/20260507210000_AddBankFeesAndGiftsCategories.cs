using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBankFeesAndGiftsCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Color", "Icon", "IsDefault", "Name", "UserId" },
                values: new object[,]
                {
                    { 20, "#8E8E93", "🏦", true, "Frais bancaires", null },
                    { 21, "#FF6B9D", "🎁", true, "Cadeaux", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "Categories", keyColumn: "Id", keyValue: 20);
            migrationBuilder.DeleteData(table: "Categories", keyColumn: "Id", keyValue: 21);
        }
    }
}
