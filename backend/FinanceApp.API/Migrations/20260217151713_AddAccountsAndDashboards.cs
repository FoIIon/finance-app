using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Créer la table Accounts
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId");

            // 2. Créer un "Compte principal" pour chaque user existant
            migrationBuilder.Sql(@"
                INSERT INTO [Accounts] ([Name], [UserId], [CreatedAt])
                SELECT N'Compte principal', [Id], GETUTCDATE()
                FROM [Users]
            ");

            // 3. Supprimer la FK existante Transactions -> Users
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Users_UserId",
                table: "Transactions");

            // 4. Ajouter la colonne AccountId (nullable) sur Transactions
            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Transactions",
                type: "int",
                nullable: true);

            // 5. Populer AccountId via la correspondance UserId -> Account
            migrationBuilder.Sql(@"
                UPDATE t
                SET t.[AccountId] = a.[Id]
                FROM [Transactions] t
                INNER JOIN [Accounts] a ON t.[UserId] = a.[UserId]
            ");

            // 6. Rendre AccountId non-nullable
            migrationBuilder.AlterColumn<int>(
                name: "AccountId",
                table: "Transactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // 7. Supprimer l'ancien index et la colonne UserId
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transactions");

            // 8. Créer l'index sur AccountId et la FK
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 9. Créer la table Dashboards
            migrationBuilder.CreateTable(
                name: "Dashboards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dashboards_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_CreatorId",
                table: "Dashboards",
                column: "CreatorId");

            // 10. Créer DashboardMembers
            migrationBuilder.CreateTable(
                name: "DashboardMembers",
                columns: table => new
                {
                    DashboardId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardMembers", x => new { x.DashboardId, x.UserId });
                    table.ForeignKey(
                        name: "FK_DashboardMembers_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardMembers_UserId",
                table: "DashboardMembers",
                column: "UserId");

            // 11. Créer DashboardAccounts
            migrationBuilder.CreateTable(
                name: "DashboardAccounts",
                columns: table => new
                {
                    DashboardId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardAccounts", x => new { x.DashboardId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_DashboardAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DashboardAccounts_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardAccounts_AccountId",
                table: "DashboardAccounts",
                column: "AccountId");

            // 12. Créer DashboardInvitations
            migrationBuilder.CreateTable(
                name: "DashboardInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DashboardId = table.Column<int>(type: "int", nullable: false),
                    InvitedEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvitedByUserId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardInvitations_Dashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "Dashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardInvitations_Users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardInvitations_DashboardId",
                table: "DashboardInvitations",
                column: "DashboardId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardInvitations_InvitedByUserId",
                table: "DashboardInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardInvitations_Token",
                table: "DashboardInvitations",
                column: "Token",
                unique: true);

            // 13. Créer un dashboard "Personnel" pour chaque user et lier son account
            migrationBuilder.Sql(@"
                INSERT INTO [Dashboards] ([Name], [CreatorId], [CreatedAt])
                SELECT N'Personnel', [Id], GETUTCDATE()
                FROM [Users]
            ");

            migrationBuilder.Sql(@"
                INSERT INTO [DashboardMembers] ([DashboardId], [UserId], [JoinedAt])
                SELECT d.[Id], d.[CreatorId], GETUTCDATE()
                FROM [Dashboards] d
            ");

            migrationBuilder.Sql(@"
                INSERT INTO [DashboardAccounts] ([DashboardId], [AccountId])
                SELECT d.[Id], a.[Id]
                FROM [Dashboards] d
                INNER JOIN [Accounts] a ON d.[CreatorId] = a.[UserId]
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "DashboardAccounts");

            migrationBuilder.DropTable(
                name: "DashboardInvitations");

            migrationBuilder.DropTable(
                name: "DashboardMembers");

            migrationBuilder.DropTable(
                name: "Dashboards");

            // Restaurer UserId sur Transactions depuis Account
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE t
                SET t.[UserId] = a.[UserId]
                FROM [Transactions] t
                INNER JOIN [Accounts] a ON t.[AccountId] = a.[Id]
            ");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Transactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Users_UserId",
                table: "Transactions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
