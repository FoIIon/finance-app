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

            migrationBuilder.AddColumn<int>(
                name: "RejectedTransactionId",
                table: "Echeances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructuredCommunication",
                table: "Echeances",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_StructuredCommunication",
                table: "Transactions",
                column: "StructuredCommunication");

            migrationBuilder.CreateIndex(
                name: "IX_Echeances_RejectedTransactionId",
                table: "Echeances",
                column: "RejectedTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Echeances_Transactions_RejectedTransactionId",
                table: "Echeances",
                column: "RejectedTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Echeances_Transactions_RejectedTransactionId",
                table: "Echeances");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_StructuredCommunication",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Echeances_RejectedTransactionId",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "StructuredCommunication",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CounterpartyIban",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "MatchedAt",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "RejectedTransactionId",
                table: "Echeances");

            migrationBuilder.DropColumn(
                name: "StructuredCommunication",
                table: "Echeances");
        }
    }
}
