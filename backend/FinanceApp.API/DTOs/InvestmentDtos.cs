using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

/// <summary>Ligne d'investissement enrichie de sa performance calculée.</summary>
public class InvestmentDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Holder { get; set; } = string.Empty;
    public InvestmentKind Kind { get; set; }
    public string? Isin { get; set; }
    public string? MetalCode { get; set; }
    public decimal Quantity { get; set; }
    public InvestmentUnit Unit { get; set; }
    public decimal CostBasis { get; set; }
    public DateTime? FirstPurchaseDate { get; set; }
    public InvestmentSource Source { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>PRU. Null pour un contrat d'assurance-vie.</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Valeur de la dernière valorisation. Null si aucune n'a été saisie.</summary>
    public decimal? MarketValue { get; set; }
    public DateTime? ValuationAsOf { get; set; }
    public bool IsStale { get; set; }
    public decimal? GainAmount { get; set; }
    public decimal? GainPercent { get; set; }
    /// <summary>CAGR approximatif. Null tant qu'aucune date d'entrée n'est renseignée.</summary>
    public decimal? AnnualizedReturn { get; set; }
}

public class CreateInvestmentDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Holder { get; set; } = string.Empty;

    [Required]
    public InvestmentKind Kind { get; set; }

    [MaxLength(12)]
    public string? Isin { get; set; }

    [MaxLength(10)]
    public string? MetalCode { get; set; }

    [Range(0.000001, 999999999)]
    public decimal Quantity { get; set; }

    [Required]
    public InvestmentUnit Unit { get; set; }

    [Range(0, 99999999.99)]
    public decimal CostBasis { get; set; }

    public DateTime? FirstPurchaseDate { get; set; }
}

public class UpdateInvestmentDto
{
    [MaxLength(120)]
    public string? Name { get; set; }

    [MaxLength(60)]
    public string? Holder { get; set; }

    [MaxLength(12)]
    public string? Isin { get; set; }

    [MaxLength(10)]
    public string? MetalCode { get; set; }

    [Range(0.000001, 999999999)]
    public decimal? Quantity { get; set; }

    [Range(0, 99999999.99)]
    public decimal? CostBasis { get; set; }

    public DateTime? FirstPurchaseDate { get; set; }

    public bool? IsArchived { get; set; }
}

public class CreateValuationDto
{
    [Required]
    public DateOnly AsOf { get; set; }

    [Range(0, 99999999.99)]
    public decimal MarketValue { get; set; }

    [Range(0, 99999999.999999)]
    public decimal? UnitPrice { get; set; }
}

public class InvestmentValuationDto
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    public DateTime AsOf { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal MarketValue { get; set; }
    public ValuationSource Source { get; set; }
}
