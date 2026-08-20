namespace FinanceApp.API.Models;

/// <summary>Nature de l'actif. Détermine le mode de valorisation.</summary>
public enum InvestmentKind
{
    Security = 0,
    Metal = 1,
    InsuranceContract = 2,
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
