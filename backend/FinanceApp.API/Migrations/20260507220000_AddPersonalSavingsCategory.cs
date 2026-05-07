using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalSavingsCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Catégorie distincte de "Épargne" (id 16, IsTransfer=true) : ici c'est une vraie dépense côté
            // dashboard Commun — l'argent quitte le commun pour aller sur le perso d'un membre du couple.
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Color", "Icon", "IsDefault", "IsTransfer", "Name", "UserId" },
                values: new object[] { 22, "#34C759", "🐷", true, false, "Épargne perso", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "Categories", keyColumn: "Id", keyValue: 22);
        }
    }
}
