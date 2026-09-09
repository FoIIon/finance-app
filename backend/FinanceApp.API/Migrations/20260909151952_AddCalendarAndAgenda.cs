using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarAndAgenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalendarOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OccurrenceStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LocalStart = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    LocalEnd = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    LocalEndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsAllDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Recurrence = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsException = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarOccurrences_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CalendarSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DashboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    EncryptedUrl = table.Column<string>(type: "TEXT", nullable: false),
                    CalendarName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarSources_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOccurrences_DashboardId_LocalDate",
                table: "CalendarOccurrences",
                columns: new[] { "DashboardId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarOccurrences_DashboardId_Uid_OccurrenceStart",
                table: "CalendarOccurrences",
                columns: new[] { "DashboardId", "Uid", "OccurrenceStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSources_DashboardId",
                table: "CalendarSources",
                column: "DashboardId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarOccurrences");

            migrationBuilder.DropTable(
                name: "CalendarSources");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Users");
        }
    }
}
