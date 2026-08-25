using System.Globalization;
using System.Text.Json;

namespace FinanceApp.API.Services;

/// <summary>
/// Une position telle que Trade Republic la renvoie dans compactPortfolioByType.
/// AverageBuyIn est le prix de revient unitaire, Quantity la quantité détenue (netSize).
/// Le prix courant n'est pas dans ce message : il vient d'un ticker séparé par ISIN.
/// </summary>
public record TrPortfolioPosition(
    string Isin,
    string Name,
    decimal Quantity,
    decimal AverageBuyIn,
    string InstrumentType)
{
    /// <summary>Montant réellement investi = prix de revient unitaire × quantité.</summary>
    public decimal CostBasis => AverageBuyIn * Quantity;
}

/// <summary>
/// Parsing des réponses de l'API Trade Republic (non documentée, forme observée le 2026-08-23).
/// Volontairement pur et statique pour rester testable sans WebSocket. Les nombres arrivent en
/// chaînes à point décimal : parsing en culture invariante obligatoire, sinon la virgule locale
/// belge décalerait silencieusement les montants.
/// </summary>
public static class TradeRepublicPortfolioParser
{
    private static decimal? ParseDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    /// <summary>Positions de compactPortfolioByType, toutes catégories confondues.</summary>
    public static List<TrPortfolioPosition> ParsePositions(string json)
    {
        var result = new List<TrPortfolioPosition>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("categories", out var categories)
            || categories.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var category in categories.EnumerateArray())
        {
            if (!category.TryGetProperty("positions", out var positions)
                || positions.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var p in positions.EnumerateArray())
            {
                var isin = p.TryGetProperty("isin", out var i) ? i.GetString() : null;
                if (string.IsNullOrEmpty(isin)) continue;

                var quantity = ParseDecimal(p, "netSize");
                var averageBuyIn = ParseDecimal(p, "averageBuyIn");
                if (quantity is null || averageBuyIn is null) continue;

                var name = p.TryGetProperty("name", out var n) ? n.GetString() ?? isin : isin;
                var type = p.TryGetProperty("instrumentType", out var t) ? t.GetString() ?? "" : "";

                result.Add(new TrPortfolioPosition(isin, name, quantity.Value, averageBuyIn.Value, type));
            }
        }

        return result;
    }

    /// <summary>Dernier cours coté d'un ticker (last.price).</summary>
    public static decimal? ParseTickerLastPrice(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("last", out var last)) return null;
        return ParseDecimal(last, "price");
    }

    /// <summary>Première place de cotation d'un instrument (exchangeIds[0]), pour bâtir l'id du ticker.</summary>
    public static string? ParseFirstExchange(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("exchangeIds", out var ex) || ex.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var e in ex.EnumerateArray())
            return e.GetString();
        return null;
    }

    /// <summary>
    /// Résumé brut du marquage de chaque position, pour le journal. Trade Republic ne
    /// documente pas comment elle distingue une obligation d'un fonds actions : cette
    /// trace donne les valeurs réelles au lieu de les faire deviner.
    /// </summary>
    public static string DescribeCategories(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("categories", out var categories)) return "aucune catégorie";

        var blocs = new List<string>();
        foreach (var category in categories.EnumerateArray())
        {
            var type = category.TryGetProperty("categoryType", out var ct) ? ct.GetString() ?? "?" : "?";
            var marquages = new List<string>();

            if (category.TryGetProperty("positions", out var positions))
            {
                foreach (var position in positions.EnumerateArray())
                {
                    var isin = position.TryGetProperty("isin", out var i) ? i.GetString() ?? "?" : "?";
                    var instrument = position.TryGetProperty("instrumentType", out var it) ? it.GetString() ?? "" : "";
                    var obligation = position.TryGetProperty("bondInfo", out var bi)
                        && bi.ValueKind != JsonValueKind.Null ? "+bondInfo" : "";
                    marquages.Add($"{isin}={instrument}{obligation}");
                }
            }

            blocs.Add($"{type} [{string.Join(", ", marquages)}]");
        }

        return string.Join(" | ", blocs);
    }
}
