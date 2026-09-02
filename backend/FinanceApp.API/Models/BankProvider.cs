namespace FinanceApp.API.Models;

/// <summary>
/// Qui alimente une connexion bancaire. Stocké en texte (nom de la valeur) pour rester lisible en
/// base et compatible avec les lignes existantes, qui portaient déjà ces trois mots en clair.
/// </summary>
public enum BankProvider
{
    /// <summary>Open Banking PSD2 via GoCardless Bank Account Data.</summary>
    GoCardless,
    /// <summary>Courtier Trade Republic, API WebSocket non documentée.</summary>
    TradeRepublic,
    /// <summary>Connexion fictive qui porte les comptes manuels (livret non connecté, espèces).</summary>
    Manual,
}
