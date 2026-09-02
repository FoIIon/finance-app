namespace FinanceApp.API.Models;

public class BankConnection
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionLogo { get; set; } = string.Empty;
    public string RequisitionId { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public BankConnectionStatus Status { get; set; }
    public BankProvider Provider { get; set; } = BankProvider.GoCardless;
    public string? EncryptedSessionToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public string? EncryptedDeviceToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// Solde espèces du compte, en euros. Relevé chez Trade Republic à l'import du
    /// portefeuille. Volontairement tenu hors de la valeur du portefeuille et de la
    /// plus-value : ce n'est pas un actif dont on mesure la performance.
    /// </summary>
    public decimal? CashBalance { get; set; }
    public DateTime? CashBalanceUpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<BankAccount> BankAccounts { get; set; } = new List<BankAccount>();
}
