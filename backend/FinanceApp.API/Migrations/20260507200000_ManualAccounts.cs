using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class ManualAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "BankAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "BankAccounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialBalance",
                table: "BankAccounts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialBalanceDate",
                table: "BankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceBankAccountId",
                table: "BankAccounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncrementCategoryId",
                table: "BankAccounts",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("IsManual", "BankAccounts");
            migrationBuilder.DropColumn("UserId", "BankAccounts");
            migrationBuilder.DropColumn("InitialBalance", "BankAccounts");
            migrationBuilder.DropColumn("InitialBalanceDate", "BankAccounts");
            migrationBuilder.DropColumn("SourceBankAccountId", "BankAccounts");
            migrationBuilder.DropColumn("IncrementCategoryId", "BankAccounts");
        }
    }
}
