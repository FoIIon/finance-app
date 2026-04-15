using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeRepublicFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "BankConnections",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "GoCardless");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedRefreshToken",
                table: "BankConnections",
                type: "nvarchar(max)",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "BankConnections");

            migrationBuilder.DropColumn(
                name: "EncryptedRefreshToken",
                table: "BankConnections");
        }
    }
}
