namespace FinanceApp.API.Models;

public class BankAccount
{
    public int Id { get; set; }
    /// <summary>Null pour les comptes manuels (non connectés à une banque via Open Banking).</summary>
    public int? BankConnectionId { get; set; }
    public string ExternalAccountId { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Compte personnel : ses transactions sont routées vers le dashboard Perso au lieu du Commun.
    /// Sert à sortir les dépenses perso de Sébastien du bilan commun d'Audrey (compte Argenta perso).
    /// Un compte non marqué est commun par défaut. Voir PersoScopeRouter et le doc
    /// projects/app-finance/perso-commun-2026-08-27.md dans le repo Yen.
    /// </summary>
    public bool IsPersonal { get; set; } = false;
    /// <summary>Solde réel récupéré via l'API banque (GoCardless balances). Pour comptes manuels = solde calculé à BalanceUpdatedAt.</summary>
    public decimal? RealBalance { get; set; }
    /// <summary>Solde booké (hors transactions pending) récupéré via l'API banque (GoCardless balances). Sert d'ancrage stable pour les courbes rétrospectives (RealBalance peut inclure du pending).</summary>
    public decimal? BookedBalance { get; set; }
    public DateTime? BalanceUpdatedAt { get; set; }

    // === Comptes manuels (sans connexion Open Banking) ===
    public bool IsManual { get; set; } = false;
    /// <summary>UserId — requis pour les comptes manuels (pour les comptes liés à BankConnection on passe par BankConnection.UserId).</summary>
    public int? UserId { get; set; }
    /// <summary>Solde au point de référence saisi par l'utilisateur.</summary>
    public decimal? InitialBalance { get; set; }
    /// <summary>Date à laquelle le solde initial a été saisi. Le calcul du solde courant ajoute les transferts depuis cette date.</summary>
    public DateTime? InitialBalanceDate { get; set; }
    /// <summary>Compte source (BankAccount) qui alimente ce compte manuel via des transferts.</summary>
    public int? SourceBankAccountId { get; set; }
    /// <summary>Catégorie qui marque les transactions à compter comme entrée sur ce compte (ex: Épargne).</summary>
    public int? IncrementCategoryId { get; set; }

    public BankConnection? BankConnection { get; set; }
    public User? User { get; set; }
    public BankAccount? SourceBankAccount { get; set; }
    public Category? IncrementCategory { get; set; }
}
