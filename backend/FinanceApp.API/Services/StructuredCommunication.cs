using System.Text.RegularExpressions;

namespace FinanceApp.API.Services;

/// <summary>
/// La communication structurée belge : douze chiffres écrits <c>+++123/4567/89012+++</c> ou
/// <c>***123/4567/89012***</c>, dont les deux derniers sont le reste des dix premiers modulo 97
/// (97 quand le reste vaut 0). Un virement qui la porte est identifié sans ambiguïté par l'émetteur
/// de la facture, c'est la seule clé de rapprochement qui ne dépende ni du montant ni du libellé.
/// Pure, statique, testée seule. Une communication qui échoue au contrôle n'est pas une clé fiable :
/// elle rend null plutôt qu'une valeur qui rapprocherait n'importe quoi.
/// </summary>
public static partial class StructuredCommunication
{
    public const int Length = 12;

    // Les trois signes d'ouverture, trois groupes de 3, 4 et 5 chiffres, barres et espaces facultatifs
    // entre les groupes, trois signes de fermeture. La fermeture n'a pas à répéter l'ouverture : les
    // banques mélangent parfois les deux formes sur un même libellé.
    [GeneratedRegex(@"(?:\+{3}|\*{3})\s*(\d{3})\s*/?\s*(\d{4})\s*/?\s*(\d{5})\s*(?:\+{3}|\*{3})")]
    private static partial Regex Delimited();

    [GeneratedRegex(@"^\d{12}$")]
    private static partial Regex TwelveDigits();

    /// <summary>
    /// La première communication structurée valide d'un libellé, réduite à ses douze chiffres. Null si
    /// le libellé n'en contient aucune qui passe le contrôle 97. Quand un libellé en porte deux valides,
    /// la première l'emporte.
    /// </summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (Match m in Delimited().Matches(text))
        {
            var digits = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value;
            if (IsValid(digits)) return digits;
        }
        return null;
    }

    /// <summary>
    /// Une saisie au formulaire, avec ou sans <c>+</c>, <c>*</c>, <c>/</c> et espaces, ramenée à ses douze
    /// chiffres. Null si autre chose que douze chiffres reste après le nettoyage ou si le contrôle échoue.
    /// </summary>
    public static string? Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = input.Replace("+", "").Replace("*", "").Replace("/", "").Replace(" ", "");
        return TwelveDigits().IsMatch(digits) && IsValid(digits) ? digits : null;
    }

    /// <summary>Douze chiffres exactement, et les deux derniers valent les dix premiers modulo 97, avec 0 → 97.</summary>
    public static bool IsValid(string twelveDigits)
    {
        if (twelveDigits is null || !TwelveDigits().IsMatch(twelveDigits)) return false;
        var body = long.Parse(twelveDigits.AsSpan(0, 10));
        var check = int.Parse(twelveDigits.AsSpan(10, 2));
        var expected = (int)(body % 97);
        if (expected == 0) expected = 97;
        return check == expected;
    }

    /// <summary>« +++123/4567/89012+++ » à partir des douze chiffres, pour l'affichage. Rend l'entrée telle quelle si elle n'a pas douze chiffres.</summary>
    public static string Format(string twelveDigits) =>
        twelveDigits.Length == Length
            ? $"+++{twelveDigits[..3]}/{twelveDigits[3..7]}/{twelveDigits[7..]}+++"
            : twelveDigits;
}
