using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEcheanceReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StructuredCommunication",
                table: "Transactions",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoMatchRefusedAt",
                table: "Echeances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyIban",
                table: "Echeances",
                type: "TEXT",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MatchedAt",
                table: "Echeances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredCommunication",
                table: "Echeances",
                type: "TEXT",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StructuredCommunication",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AutoMatchRefusedAt",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "CounterpartyIban",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "MatchedAt",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "StructuredCommunication",
                table: "Echeances");
        }
    }
}
