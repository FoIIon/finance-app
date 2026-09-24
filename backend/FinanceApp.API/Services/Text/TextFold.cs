using System.Globalization;
using System.Text;

namespace FinanceApp.API.Services.Text;

/// <summary>
/// Repli d'un texte pour le comparer sans casse ni accents : « Décompte », « DECOMPTE » et « decompte » se
/// confondent. Partagé par le tri des mails (MailIngestRules) et le règlement des récurrentes
/// (RecurringSettlement), qui comparent tous deux un mot attendu à un libellé venu de l'extérieur.
/// </summary>
public static class TextFold
{
    /// <summary>Minuscules sans marques diacritiques. Null ou vide rend la chaîne vide.</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
