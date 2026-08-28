using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalScopeAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonalScope",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Reprise : le compte « Perso » créé par nom avant cette migration devient le compte de
            // périmètre perso. Un seul par utilisateur (le plus ancien), l'index unique ci-dessous
            // refuserait le reste.
            migrationBuilder.Sql(
                "UPDATE Accounts SET IsPersonalScope = 1 " +
                "WHERE Id IN (SELECT MIN(Id) FROM Accounts WHERE Name = 'Perso' GROUP BY UserId);");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId_PersonalScope",
                table: "Accounts",
                columns: new[] { "UserId", "IsPersonalScope" },
                unique: true,
                filter: "[IsPersonalScope] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserId_PersonalScope",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsPersonalScope",
                table: "Accounts");
        }
    }
}
