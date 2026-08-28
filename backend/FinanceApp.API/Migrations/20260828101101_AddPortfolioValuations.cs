using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioValuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioValuations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    AsOf = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MarketValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioValuations_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioValuations_DashboardId_AsOf",
                table: "PortfolioValuations",
                columns: new[] { "DashboardId", "AsOf" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioValuations");
        }
    }
}
