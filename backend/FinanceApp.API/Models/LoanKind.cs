namespace FinanceApp.API.Models;

public enum LoanKind
{
    /// <summary>Crédit logement contracté auprès d'une banque.</summary>
    Mortgage = 0,
    /// <summary>Prêt entre particuliers, typiquement sans intérêt.</summary>
    Family = 1,
    /// <summary>Crédit à la consommation, prêt voiture, prêt rénovation.</summary>
    Consumer = 2
}
