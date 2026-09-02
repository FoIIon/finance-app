using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonal",
                table: "Dashboards",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Remplissage : la convention implicite devient une colonne. Le dashboard personnel est le
            // plus ancien de chaque créateur, le compte principal le plus ancien compte non perso de
            // chaque utilisateur. Exactement ce que le code déduisait jusqu'ici, figé une fois pour toutes.
            migrationBuilder.Sql(@"
                UPDATE Dashboards SET IsPersonal = 1
                WHERE Id IN (
                    SELECT d.Id FROM Dashboards d
                    WHERE NOT EXISTS (
                        SELECT 1 FROM Dashboards d2
                        WHERE d2.CreatorId = d.CreatorId
                          AND (d2.CreatedAt < d.CreatedAt OR (d2.CreatedAt = d.CreatedAt AND d2.Id < d.Id))
                    )
                );");

            migrationBuilder.Sql(@"
                UPDATE Accounts SET IsPrimary = 1
                WHERE Id IN (
                    SELECT a.Id FROM Accounts a
                    WHERE a.IsPersonalScope = 0
                      AND NOT EXISTS (
                        SELECT 1 FROM Accounts a2
                        WHERE a2.UserId = a.UserId AND a2.IsPersonalScope = 0
                          AND (a2.CreatedAt < a.CreatedAt OR (a2.CreatedAt = a.CreatedAt AND a2.Id < a.Id))
                    )
                );");

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_CreatorId_Personal",
                table: "Dashboards",
                columns: new[] { "CreatorId", "IsPersonal" },
                unique: true,
                filter: "[IsPersonal] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId_Primary",
                table: "Accounts",
                columns: new[] { "UserId", "IsPrimary" },
                unique: true,
                filter: "[IsPrimary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Dashboards_CreatorId_Personal",
                table: "Dashboards");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId_Primary",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsPersonal",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "Accounts");
        }
    }
}
