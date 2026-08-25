namespace FinanceApp.API.Models;

/// <summary>Nature de l'actif. Détermine le mode de valorisation.</summary>
public enum InvestmentKind
{
    Security = 0,
    Metal = 1,
    InsuranceContract = 2,
    /// <summary>
    /// Ajouté le 25/08/2026 : sans lui, les trois cryptos du portefeuille Trade Republic
    /// tombaient en Titre coté et la répartition par type affichait « Titre coté 100 % »
    /// sur un portefeuille dont un tiers n'est ni action ni fonds.
    /// </summary>
    Crypto = 3,
}

public enum InvestmentUnit
{
    Share = 0,
    Gram = 1,
    Ounce = 2,
    Contract = 3,
}

/// <summary>Qui écrit les données de la ligne. Distinct de InvestmentKind.</summary>
public enum InvestmentSource
{
    Manual = 0,
    TradeRepublic = 1,
}

public enum ValuationSource
{
    Manual = 0,
    TradeRepublic = 1,
    SpotApi = 2,
}

public enum MovementType
{
    Buy = 0,
    Sell = 1,
    Dividend = 2,
    Fee = 3,
}
