using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class EcheanceDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    /// <summary>Null quand le montant n'est pas encore connu, voir IsAmountKnown.</summary>
    public decimal? Amount { get; set; }
    public bool IsAmountKnown { get; set; }
    public string? Notes { get; set; }
    /// <summary>AVenir, EnRetard ou Payee. Calculé à la lecture, jamais stocké.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public int? TransactionId { get; set; }
    /// <summary>IBAN du bénéficiaire, normalisé (sans espaces, majuscules). Null si non renseigné.</summary>
    public string? CounterpartyIban { get; set; }
    /// <summary>Les douze chiffres de la communication structurée attendue. Null si non renseignée.</summary>
    public string? StructuredCommunication { get; set; }
    /// <summary>Instant UTC du rapprochement automatique. Null quand le lien est manuel ou absent.</summary>
    public DateTime? MatchedAt { get; set; }
    /// <summary>Instant UTC où l'utilisateur a défait un rapprochement automatique. Posé : le rapprocheur ignore
    /// cette échéance jusqu'à ce que l'IBAN ou la communication change.</summary>
    public DateTime? AutoMatchRefusedAt { get; set; }
    /// <summary>La transaction qui règle l'échéance, pour l'écran. Null sans lien.</summary>
    public EcheancePaymentDto? Payment { get; set; }
    public List<int> DocumentIds { get; set; } = new();
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Ce que l'écran montre de la transaction qui règle une échéance. Date dans le fuseau du ménage.</summary>
public class EcheancePaymentDto
{
    public int TransactionId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CounterpartyName { get; set; }
}

public class CreateEcheanceDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [Required]
    public DateOnly DueDate { get; set; }

    [Range(0, 9999999.99)]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Saisie brute, espaces tolérés (34 caractères plus huit espaces de groupes) : le contrôleur normalise.</summary>
    [MaxLength(42)]
    public string? CounterpartyIban { get; set; }

    /// <summary>Saisie brute (« +++123/4567/89012+++ » accepté) : le contrôleur normalise et refuse un contrôle 97 faux.</summary>
    [MaxLength(20)]
    public string? StructuredCommunication { get; set; }
}

/// <summary>Remplacement complet : un champ absent revient à null (le montant redevient inconnu).</summary>
public class UpdateEcheanceDto
{
    [Required, MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [Required]
    public DateOnly DueDate { get; set; }

    [Range(0, 9999999.99)]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Transaction qui règle l'échéance, sur un compte du dashboard. Null : détachée.</summary>
    public int? TransactionId { get; set; }

    [MaxLength(42)]
    public string? CounterpartyIban { get; set; }

    [MaxLength(20)]
    public string? StructuredCommunication { get; set; }
}
