using System.Globalization;
using System.Text;

namespace FinanceApp.API.Services;

/// <summary>
/// Reconnaît qu'un libellé bancaire nomme l'une des personnes de la famille, malgré les variantes
/// d'écriture des banques : accents, civilités, initiales, séparateurs. « SEBASTIEN LIBERT »,
/// « Mr SÉBASTIEN LIBERT » et « Sebastien Jean R Libert » désignent le même homme, « LIBERT - LAMBRECHT »
/// et « LIBERT S + LAMBRECHT A » le même couple.
///
/// Sert à repérer les virements internes : un débit dont la contrepartie est le titulaire lui-même
/// n'est pas une dépense du ménage.
/// </summary>
public static class PersonNameMatcher
{
    /// <summary>Civilités et mots de liaison, sans valeur d'identification.</summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "MR", "MME", "MLLE", "MRS", "MISS", "MONSIEUR", "MADAME", "THE", "AND",
    };

    /// <summary>
    /// Nombre de jetons communs exigé. Deux, parce qu'un seul nom de famille partagé avec un commerçant
    /// suffirait à escamoter une vraie dépense, et qu'escamoter coûte plus cher que rater.
    /// </summary>
    private const int RequiredCommonTokens = 2;

    /// <summary>Vrai si <paramref name="label"/> nomme l'un des <paramref name="knownNames"/>.</summary>
    public static bool MatchesAny(string? label, IEnumerable<string> knownNames)
    {
        var labelTokens = Tokenize(label);
        if (labelTokens.Count < RequiredCommonTokens) return false;

        foreach (var known in knownNames)
        {
            var knownTokens = Tokenize(known);
            if (knownTokens.Count < RequiredCommonTokens) continue;
            if (labelTokens.Intersect(knownTokens, StringComparer.Ordinal).Count() >= RequiredCommonTokens)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Jetons significatifs d'un nom : majuscules sans accent, trois lettres minimum, hors civilités.
    /// Le seuil de trois lettres écarte les initiales (« S », « A », « R »), qui varient d'une banque à l'autre.
    /// </summary>
    public static HashSet<string> Tokenize(string? value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return result;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var letters = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            letters.Append(char.IsLetter(c) ? char.ToUpperInvariant(c) : ' ');
        }

        foreach (var token in letters.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 3) continue;
            if (Noise.Contains(token)) continue;
            result.Add(token);
        }
        return result;
    }
}
