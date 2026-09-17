using System.Text.Json;

namespace FinanceApp.API.Services;

/// <summary>
/// Lecture des champs d'une transaction GoCardless qui demandent plus qu'un TryGetProperty.
///
/// Pourquoi l'IBAN de la contrepartie est stocké (31/08/2026) : les paiements à la commune
/// arrivent sous deux libellés (« Ville de Demoville » et « ADMINISTRATION COMMUNALE DE
/// DEMOVILLE- »), avec un libellé de virement vide, et rangés en Logement par les règles. Impossible de
/// savoir lesquels sont la crèche de Emma sans le compte du bénéficiaire, que l'app jetait à l'import.
/// Un même bénéficiaire garde son IBAN quand la banque change l'orthographe de son nom.
/// </summary>
public static class GoCardlessTransactionFields
{
    /// <summary>
    /// Compte de la contrepartie : le créditeur d'abord (une dépense), le débiteur ensuite (un revenu),
    /// même ordre que l'extraction du nom. Null quand la banque ne le sert pas, ce qui est le cas de
    /// tous les paiements par carte : c'est le commerçant qui encaisse, pas un compte identifié.
    /// </summary>
    public static string? CounterpartyIban(JsonElement tx) =>
        FromAccount(tx, "creditorAccount") ?? FromAccount(tx, "debtorAccount");

    private static string? FromAccount(JsonElement tx, string property)
    {
        if (!tx.TryGetProperty(property, out var account) || account.ValueKind != JsonValueKind.Object)
            return null;

        // Certaines banques ne servent qu'un numéro national (bban), qui identifie le bénéficiaire tout autant.
        foreach (var field in new[] { "iban", "bban" })
        {
            if (!account.TryGetProperty(field, out var value)) continue;
            var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(raw)) return Normalize(raw);
        }

        return null;
    }

    /// <summary>Sans espaces, en majuscules : un IBAN doit se comparer et se chercher à l'identique.</summary>
    public static string Normalize(string value) => value.Replace(" ", "").ToUpperInvariant();

    /// <summary>
    /// Le texte d'un champ de remittance Berlin Group, servi soit en chaîne (<paramref name="name"/>), soit en
    /// tableau (<paramref name="name"/> + « Array »). Seuls les éléments de type chaîne sont gardés, joints par
    /// un espace : certaines banques remplissent le tableau structuré d'objets <c>{reference, referenceType,
    /// referenceIssuer}</c>, et un <c>GetString()</c> sur l'un d'eux levait, faisait échouer l'import du compte
    /// et rejouait la même erreur à chaque cycle. Null quand ni l'un ni l'autre ne porte de texte.
    /// </summary>
    public static string? RemittanceText(JsonElement tx, string name)
    {
        if (tx.TryGetProperty(name, out var single) && single.ValueKind == JsonValueKind.String)
        {
            var text = single.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        if (tx.TryGetProperty(name + "Array", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            var parts = array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (parts.Count > 0) return string.Join(" ", parts);
        }

        return null;
    }
}
