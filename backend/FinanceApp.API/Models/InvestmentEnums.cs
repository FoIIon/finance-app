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
    /// <summary>Obligations et fonds obligataires à échéance.</summary>
    Bond = 4,
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
    /// <summary>
    /// Cours de clôture passé, reconstitué depuis l'historique Trade Republic. La valeur
    /// totale portée par ces lignes applique la quantité ACTUELLE à un cours ancien : elle
    /// donne la tendance d'un actif, elle ne dit pas ce que le portefeuille valait ce
    /// jour-là. Exclue à ce titre du calcul de la courbe du patrimoine.
    /// </summary>
    TradeRepublicHistory = 3,
    /// <summary>
    /// Valeur du portefeuille reconstruite depuis la timeline Trade Republic (achats, ventes,
    /// plans d'épargne depuis le premier ordre) et les cours de clôture : quantité détenue
    /// chaque jour × cours du jour. Tient compte des positions vendues et des pertes réalisées.
    /// La quantité de chaque mouvement est déduite du montant et du cours de clôture du jour,
    /// donc approchée : la timeline ne donne pas la quantité exécutée.
    /// </summary>
    Reconstructed = 4,
}

public enum MovementType
{
    Buy = 0,
    Sell = 1,
    Dividend = 2,
    Fee = 3,
}
