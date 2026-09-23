using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMailIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UploadedByUserId",
                table: "Documents",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "MailMessageId",
                table: "Documents",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Documents",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.CreateTable(
                name: "MailSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastDepositAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DepositedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailSources_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailSources_DashboardId_Address",
                table: "MailSources",
                columns: new[] { "DashboardId", "Address" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailSources");

            migrationBuilder.DropColumn(
                name: "MailMessageId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Documents");

            migrationBuilder.AlterColumn<int>(
                name: "UploadedByUserId",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
