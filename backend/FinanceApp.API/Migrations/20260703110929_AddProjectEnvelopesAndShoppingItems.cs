using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectEnvelopesAndShoppingItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectEnvelopeId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectEnvelopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", nullable: false),
                    TargetBudget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    FundingNote = table.Column<string>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEnvelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEnvelopes_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingItems_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProjectEnvelopeId",
                table: "Transactions",
                column: "ProjectEnvelopeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEnvelopes_DashboardId",
                table: "ProjectEnvelopes",
                column: "DashboardId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingItems_DashboardId",
                table: "ShoppingItems",
                column: "DashboardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ProjectEnvelopes_ProjectEnvelopeId",
                table: "Transactions",
                column: "ProjectEnvelopeId",
                principalTable: "ProjectEnvelopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ProjectEnvelopes_ProjectEnvelopeId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "ProjectEnvelopes");

            migrationBuilder.DropTable(
                name: "ShoppingItems");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ProjectEnvelopeId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ProjectEnvelopeId",
                table: "Transactions");
        }
    }
}
