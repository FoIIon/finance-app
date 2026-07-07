using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class MoveIsFixedToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFixed",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarkAsFixed",
                table: "CategoryRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Backfill avant de dropper Categories.IsFixed : les dépenses des catégories
            // marquées fixes le restent, et les règles pointant vers ces catégories
            // continueront à marquer les prochaines transactions (Type = 1 → Expense).
            migrationBuilder.Sql(
                "UPDATE Transactions SET IsFixed = 1 WHERE Type = 1 AND CategoryId IN (SELECT Id FROM Categories WHERE IsFixed = 1);");
            migrationBuilder.Sql(
                "UPDATE CategoryRules SET MarkAsFixed = 1 WHERE CategoryId IN (SELECT Id FROM Categories WHERE IsFixed = 1);");

            migrationBuilder.DropColumn(
                name: "IsFixed",
                table: "Categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFixed",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "MarkAsFixed",
                table: "CategoryRules");

            migrationBuilder.AddColumn<bool>(
                name: "IsFixed",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 5,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 6,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 8,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 9,
                column: "IsFixed",
                value: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 10,
                column: "IsFixed",
                value: false);
        }
    }
}
