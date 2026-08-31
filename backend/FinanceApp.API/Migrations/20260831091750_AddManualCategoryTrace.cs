using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddManualCategoryTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryBeforeManualId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CategorySetManuallyAt",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryBeforeManualId",
                table: "Transactions",
                column: "CategoryBeforeManualId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryBeforeManualId",
                table: "Transactions",
                column: "CategoryBeforeManualId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryBeforeManualId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CategoryBeforeManualId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategoryBeforeManualId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorySetManuallyAt",
                table: "Transactions");
        }
    }
}
