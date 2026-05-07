using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Color", "Icon", "IsDefault", "Name", "UserId" },
                values: new object[,]
                {
                    { 11, "#FF6384", "🍽️", true, "Restaurants", null },
                    { 12, "#9966FF", "📱", true, "Abonnements", null },
                    { 13, "#36A2EB", "🛡️", true, "Assurances", null },
                    { 14, "#FFCE56", "⚡", true, "Énergie", null },
                    { 15, "#4BC0C0", "👶", true, "Enfants", null },
                    { 16, "#4BC0C0", "📈", true, "Épargne", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValues: new object[] { 11, 12, 13, 14, 15, 16 });
        }
    }
}
