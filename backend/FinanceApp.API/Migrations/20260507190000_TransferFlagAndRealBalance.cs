using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class TransferFlagAndRealBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RealBalance",
                table: "BankAccounts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BalanceUpdatedAt",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);

            // Flag Épargne (id 16) comme transfert interne
            migrationBuilder.Sql("UPDATE Categories SET IsTransfer = 1 WHERE Id = 16;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("IsTransfer", "Categories");
            migrationBuilder.DropColumn("RealBalance", "BankAccounts");
            migrationBuilder.DropColumn("BalanceUpdatedAt", "BankAccounts");
        }
    }
}
