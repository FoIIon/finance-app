using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Le solde espèces d'une connexion Trade Republic, exposé comme un compte bancaire à part entière.
///
/// Pourquoi (31/08/2026) : les espèces vivaient sur BankConnection.CashBalance et n'étaient lues que
/// par l'écran Dettes pour le patrimoine net. Elles n'apparaissaient donc ni dans la liste des soldes
/// par compte, ni dans le KPI Solde global, ni dans la courbe du solde total. En faire un compte
/// bancaire les fait entrer partout d'un coup, sans cas particulier dans chaque agrégat.
///
/// Le compte est marqué perso : le cash TR est alimenté depuis l'Argenta perso de Sébastien, il
/// appartient à son périmètre, pas au bilan commun. Aucune transaction ne lui est rattachée (les
/// lignes de la timeline TR portent BankAccountId = null), donc marquer ce compte perso ne route rien
/// vers le Perso au passage : une dépense carte TR reste commune par défaut (PersoScopeRouter).
/// </summary>
public static class TradeRepublicCashAccount
{
    public const string AccountName = "Trade Republic (espèces)";

    /// <summary>Identifiant stable, qui sert de clé d'upsert et n'est jamais appelé chez GoCardless.</summary>
    public static string ExternalIdFor(int connectionId) => $"tr-cash-{connectionId}";

    /// <summary>
    /// Crée ou met à jour le compte espèces d'une connexion Trade Republic à partir de son
    /// CashBalance. Sans solde espèces connu, ne crée rien.
    /// </summary>
    public static async Task UpsertAsync(AppDbContext context, BankConnection connection)
    {
        if (connection.CashBalance == null) return;

        var externalId = ExternalIdFor(connection.Id);
        var account = await context.BankAccounts
            .FirstOrDefaultAsync(ba => ba.ExternalAccountId == externalId);

        if (account == null)
        {
            account = new BankAccount
            {
                BankConnectionId = connection.Id,
                ExternalAccountId = externalId,
                Iban = string.Empty,
                OwnerName = string.Empty,
                AccountName = AccountName,
                Currency = "EUR",
                IsActive = true,
                IsManual = false,
                UserId = connection.UserId,
                IsPersonal = true,
            };
            context.BankAccounts.Add(account);
        }

        // Le solde TR est un solde réel, sans notion de booké ni de pending : les deux ancres reçoivent
        // la même valeur, sinon la courbe du solde total ignorerait ces espèces.
        account.RealBalance = connection.CashBalance;
        account.BookedBalance = connection.CashBalance;
        account.BalanceUpdatedAt = connection.CashBalanceUpdatedAt ?? DateTime.UtcNow;

        await context.SaveChangesAsync();
    }
}
