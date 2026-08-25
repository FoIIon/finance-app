using FinanceApp.API.Data;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Une session Trade Republic ne vit que quelques minutes. Mesuré en production le
/// 25/08/2026 : une synchronisation passe à 15h34, la suivante prend un 401 à 15h40 sur
/// le même jeton. Le jeton stocké à la connexion est donc périmé bien avant la
/// synchronisation suivante, qui tourne toutes les six heures.
///
/// Conséquence observée avant ce correctif : la synchronisation levait « Session TR
/// expirée » et l'import de portefeuille, qui envoie ce jeton dans sa souscription
/// WebSocket, se faisait répondre AUTHENTICATION_ERROR par Trade Republic. Les deux
/// chemins ne fonctionnaient que dans les minutes suivant une connexion manuelle.
///
/// On rafraîchit donc avant chaque opération, plutôt que d'espérer tomber dans la fenêtre.
/// </summary>
public static class TradeRepublicSession
{
    /// <summary>
    /// Renouvelle la session à partir du refresh token, persiste le nouveau jeton chiffré
    /// sur la connexion, et renvoie le jeton en clair pour l'appel qui suit.
    /// </summary>
    public static async Task<string> RefreshAndStoreAsync(
        BankConnection connection,
        TradeRepublicClient client,
        AppDbContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(connection.EncryptedRefreshToken))
            throw new InvalidOperationException(
                "Connexion Trade Republic incomplète : relancez la connexion dans Banques.");

        var refreshToken = client.DecryptToken(connection.EncryptedRefreshToken);
        var deviceToken = string.IsNullOrEmpty(connection.EncryptedDeviceToken)
            ? string.Empty
            : client.DecryptToken(connection.EncryptedDeviceToken);

        string sessionToken;
        try
        {
            sessionToken = await client.RefreshSessionAsync(refreshToken, deviceToken, ct);
        }
        catch (Exception ex)
        {
            // Le refresh token lui-même est mort : on le dit en clair, au lieu de laisser
            // remonter le message brut de Trade Republic.
            throw new InvalidOperationException(
                "La session Trade Republic n'a pas pu être renouvelée. Relancez la connexion dans Banques.", ex);
        }

        connection.EncryptedSessionToken = client.EncryptToken(sessionToken);
        await context.SaveChangesAsync(ct);

        return sessionToken;
    }
}
