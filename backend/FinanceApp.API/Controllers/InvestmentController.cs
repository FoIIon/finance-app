using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/investment")]
[Authorize]
public class InvestmentController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<InvestmentController> _logger;

    public InvestmentController(AppDbContext context, ILogger<InvestmentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    /// <summary>Projette une ligne et sa dernière valorisation vers le DTO enrichi.</summary>
    private static InvestmentDto Map(Investment i, InvestmentValuation? latest, DateTime now)
    {
        var marketValue = latest?.MarketValue;
        var (gainAmount, gainPercent) = InvestmentCalculator.ComputeGain(i.CostBasis, marketValue);

        return new InvestmentDto
        {
            Id = i.Id,
            DashboardId = i.DashboardId,
            Name = i.Name,
            Holder = i.Holder,
            Kind = i.Kind,
            Isin = i.Isin,
            MetalCode = i.MetalCode,
            Quantity = i.Quantity,
            Unit = i.Unit,
            CostBasis = i.CostBasis,
            FirstPurchaseDate = i.FirstPurchaseDate,
            Source = i.Source,
            IsArchived = i.IsArchived,
            CreatedAt = i.CreatedAt,
            UnitCost = InvestmentCalculator.ComputeUnitCost(i.Kind, i.CostBasis, i.Quantity),
            MarketValue = marketValue,
            ValuationAsOf = latest?.AsOf,
            IsStale = latest != null && InvestmentCalculator.IsStale(latest.Source, latest.AsOf, now),
            GainAmount = gainAmount,
            GainPercent = gainPercent,
            AnnualizedReturn = latest == null
                ? null
                : InvestmentCalculator.ComputeCagr(i.CostBasis, marketValue, i.FirstPurchaseDate, latest.AsOf),
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<InvestmentDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .OrderBy(i => i.IsArchived)
            .ThenBy(i => i.Holder)
            .ThenBy(i => i.Name)
            .ToListAsync();

        var ids = investments.Select(i => i.Id).ToList();

        // Agrégation côté client : SQLite ne sait pas grouper sur decimal en SQL.
        var valuations = await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId))
            .ToListAsync();

        var latestByInvestment = valuations
            .GroupBy(v => v.InvestmentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.AsOf).First());

        var now = DateTime.UtcNow;
        var result = investments
            .Select(i => Map(i, latestByInvestment.GetValueOrDefault(i.Id), now))
            .ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<InvestmentDto>> Create(CreateInvestmentDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        // Un contrat d'assurance-vie n'a pas de quantité naturelle : 1 par convention.
        var quantity = dto.Kind == InvestmentKind.InsuranceContract ? 1m : dto.Quantity;
        var unit = dto.Kind == InvestmentKind.InsuranceContract ? InvestmentUnit.Contract : dto.Unit;

        var investment = new Investment
        {
            DashboardId = dto.DashboardId,
            Name = dto.Name,
            Holder = dto.Holder,
            Kind = dto.Kind,
            Isin = dto.Isin,
            MetalCode = dto.MetalCode,
            Quantity = quantity,
            Unit = unit,
            CostBasis = dto.CostBasis,
            FirstPurchaseDate = dto.FirstPurchaseDate,
            Source = InvestmentSource.Manual,
        };

        _context.Investments.Add(investment);
        await _context.SaveChangesAsync();

        return Ok(Map(investment, null, DateTime.UtcNow));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<InvestmentDto>> Update(int id, UpdateInvestmentDto dto)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        if (dto.Kind.HasValue) investment.Kind = dto.Kind.Value;
        if (dto.Name != null) investment.Name = dto.Name;
        if (dto.Holder != null) investment.Holder = dto.Holder;
        if (dto.Isin != null) investment.Isin = dto.Isin;
        if (dto.MetalCode != null) investment.MetalCode = dto.MetalCode;
        if (dto.Quantity.HasValue && investment.Kind != InvestmentKind.InsuranceContract)
            investment.Quantity = dto.Quantity.Value;
        if (dto.CostBasis.HasValue) investment.CostBasis = dto.CostBasis.Value;
        if (dto.FirstPurchaseDate.HasValue) investment.FirstPurchaseDate = dto.FirstPurchaseDate.Value;
        if (dto.IsArchived.HasValue) investment.IsArchived = dto.IsArchived.Value;

        await _context.SaveChangesAsync();

        var latest = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .FirstOrDefaultAsync();

        return Ok(Map(investment, latest, DateTime.UtcNow));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        // Les valorisations partent en cascade (configuré dans OnModelCreating).
        _context.Investments.Remove(investment);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Enregistre une valeur datée. Une valorisation existante à la même date est remplacée
    /// (contrainte unique InvestmentId + AsOf), les autres dates ne sont jamais touchées :
    /// l'historique reste intact et la courbe ne se réécrit pas rétroactivement.
    /// </summary>
    [HttpPost("{id}/valuation")]
    public async Task<ActionResult<InvestmentDto>> AddValuation(int id, CreateValuationDto dto)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        // Une date future rendrait cette valorisation la plus récente pour toujours, la
        // ligne resterait figée sur cette valeur, IsStale ne se déclencherait jamais (la date
        // n'est jamais dépassée) et ComputeCagr prendrait cet horizon comme référence. Seule
        // une suppression complète de la ligne permettrait d'en sortir, la corriger ne suffit
        // pas. On la rejette donc à l'entrée, avec une journée de marge par rapport à UtcNow
        // pour ne pas pénaliser un utilisateur dont le fuseau horaire local est en avance sur
        // l'UTC (le soir en Europe, la date locale a déjà changé alors que l'UTC est encore
        // la veille).
        if (dto.AsOf > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest("La date de valorisation ne peut pas être dans le futur.");

        // AsOf porte la date de la valeur, pas la date de saisie. Le DTO est typé DateOnly :
        // toute ambiguïté de fuseau horaire (offset dans la chaîne reçue) est rejetée par le
        // binding avant d'arriver ici, la contrainte d'unicité ne peut plus être contournée
        // par un décalage qui ferait glisser la date d'un jour.
        var asOf = dto.AsOf.ToDateTime(TimeOnly.MinValue);

        var existing = await _context.InvestmentValuations
            .FirstOrDefaultAsync(v => v.InvestmentId == id && v.AsOf == asOf);

        if (existing != null)
        {
            existing.MarketValue = dto.MarketValue;
            existing.UnitPrice = dto.UnitPrice;
            existing.Source = ValuationSource.Manual;
        }
        else
        {
            _context.InvestmentValuations.Add(new InvestmentValuation
            {
                InvestmentId = id,
                AsOf = asOf,
                MarketValue = dto.MarketValue,
                UnitPrice = dto.UnitPrice,
                Source = ValuationSource.Manual,
            });
        }

        await _context.SaveChangesAsync();

        var latest = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .FirstAsync();

        return Ok(Map(investment, latest, DateTime.UtcNow));
    }

    /// <summary>
    /// Courbe agrégée du patrimoine investi du dashboard, un point par date de valorisation.
    /// LinesTotal (lignes non archivées) est constant sur tous les points : il permet au
    /// frontend d'annoncer une courbe partielle (« X lignes sur Y valorisées »).
    /// </summary>
    /// <summary>
    /// Solde espèces du compte Trade Republic. Exposé à part : il ne fait pas partie du
    /// portefeuille et n'entre dans aucun calcul de performance.
    /// </summary>
    [HttpGet("cash")]
    public async Task<ActionResult<CashBalanceDto>> GetCash()
    {
        var userId = GetUserId();
        var connection = await _context.BankConnections
            .Where(bc => bc.UserId == userId && bc.Provider == "TradeRepublic")
            .OrderByDescending(bc => bc.CashBalanceUpdatedAt)
            .FirstOrDefaultAsync();

        return Ok(new CashBalanceDto
        {
            Amount = connection?.CashBalance,
            UpdatedAt = connection?.CashBalanceUpdatedAt,
        });
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<InvestmentHistoryPointDto>>> GetHistory([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .ToListAsync();

        var ids = investments.Select(i => i.Id).ToList();

        // Agrégation côté client : SQLite ne sait pas grouper sur decimal en SQL.
        // Les cours passés reconstitués appliquent la quantité actuelle à une date ancienne :
        // ils donnent la tendance d'un actif, jamais ce que le portefeuille valait ce jour-là.
        var valuations = await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId) && v.Source != ValuationSource.TradeRepublicHistory)
            .ToListAsync();

        var valuationsByInvestment = valuations
            .GroupBy(v => v.InvestmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(DateTime, decimal)>)g
                .Select(v => (v.AsOf, v.MarketValue))
                .ToList());

        var lines = investments
            .Select(i => new PortfolioLine(
                i.CostBasis,
                i.IsArchived,
                valuationsByInvestment.GetValueOrDefault(i.Id) ?? Array.Empty<(DateTime, decimal)>()))
            .ToList();

        var history = InvestmentCalculator.ComputePortfolioHistory(lines);
        var linesTotal = investments.Count(i => !i.IsArchived);

        var result = history
            .Select(p => new InvestmentHistoryPointDto
            {
                AsOf = p.AsOf,
                Value = p.Value,
                Invested = p.Invested,
                LinesIncluded = p.LinesIncluded,
                LinesTotal = linesTotal,
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Toutes les valorisations des lignes non archivées du dashboard, par date croissante.
    /// Sert aux sparklines du tableau : une requête au lieu d'une par ligne.
    /// </summary>
    [HttpGet("valuations")]
    public async Task<ActionResult<List<InvestmentValuationDto>>> GetAllValuations([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var valuations = await _context.InvestmentValuations
            .Where(v => v.Investment.DashboardId == dashboardId && !v.Investment.IsArchived)
            .OrderBy(v => v.AsOf)
            .ThenBy(v => v.InvestmentId)
            .Select(v => new InvestmentValuationDto
            {
                Id = v.Id,
                InvestmentId = v.InvestmentId,
                AsOf = v.AsOf,
                UnitPrice = v.UnitPrice,
                MarketValue = v.MarketValue,
                Source = v.Source,
            })
            .ToListAsync();

        return Ok(valuations);
    }

    /// <summary>
    /// Importe le portefeuille Trade Republic dans le dashboard : positions (quantité, prix de
    /// revient) et valorisation du jour au cours courant. Réconciliation par ISIN, une ligne
    /// manuelle du même ISIN est adoptée plutôt que dupliquée. Idempotent : relancer met à jour
    /// les lignes et remplace la valorisation du jour (contrainte unique InvestmentId + AsOf).
    /// Utilise le session token stocké, valable quelques minutes après la connexion.
    /// </summary>
    [HttpPost("import-trade-republic")]
    public async Task<ActionResult<TradeRepublicImportResultDto>> ImportTradeRepublic(
        [FromQuery] int dashboardId,
        [FromServices] TradeRepublicClient trClient,
        [FromServices] IConfiguration configuration)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var connection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.UserId == userId && bc.Provider == "TradeRepublic"
                && bc.EncryptedRefreshToken != null);

        if (connection == null)
            return BadRequest("Aucune connexion Trade Republic. Connecte-toi d'abord dans Banques.");
        // La session stockée est presque toujours périmée : on la renouvelle avant l'appel,
        // sinon la souscription WebSocket se fait répondre AUTHENTICATION_ERROR.
        string sessionToken;
        try
        {
            sessionToken = await TradeRepublicSession.RefreshAndStoreAsync(connection, trClient, _context, _logger);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var refreshToken = trClient.DecryptToken(connection.EncryptedRefreshToken!);
        var deviceToken = string.IsNullOrEmpty(connection.EncryptedDeviceToken)
            ? "" : trClient.DecryptToken(connection.EncryptedDeviceToken);

        TradeRepublicClient.TrPortfolioImport import;
        try
        {
            import = await trClient.ImportPortfolioSnapshotAsync(sessionToken, refreshToken, deviceToken);
        }
        catch (Exception ex)
        {
            return BadRequest($"Import Trade Republic échoué : {ex.Message}");
        }

        var snapshots = import.Positions;

        // Le solde espèces est rangé sur la connexion, pas sur une ligne d'investissement :
        // il s'affiche à part et n'entre ni dans la valeur du portefeuille ni dans la
        // plus-value, faute de quoi il gonflerait une performance qu'il ne produit pas.
        if (import.CashBalance.HasValue)
        {
            connection.CashBalance = import.CashBalance.Value;
            connection.CashBalanceUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        var defaultHolder = configuration["TradeRepublic:DefaultHolder"] ?? "Trade Republic";
        var today = DateTime.UtcNow.Date;
        int created = 0, updated = 0, valued = 0, historyPoints = 0;

        foreach (var snap in snapshots)
        {
            var pos = snap.Position;

            // Réconciliation : d'abord par ExternalId, puis adoption d'une ligne manuelle du même
            // ISIN (ExternalId nul) pour ne pas la dupliquer.
            var inv = await _context.Investments
                .FirstOrDefaultAsync(i => i.DashboardId == dashboardId && i.ExternalId == pos.Isin)
                ?? await _context.Investments
                .FirstOrDefaultAsync(i => i.DashboardId == dashboardId && i.Isin == pos.Isin && i.ExternalId == null);

            if (inv == null)
            {
                inv = new Investment
                {
                    DashboardId = dashboardId,
                    Name = pos.Name,
                    Holder = defaultHolder,
                    Kind = InvestmentKindClassifier.FromTradeRepublic(pos.Isin, pos.InstrumentType),
                    Isin = pos.Isin,
                    Quantity = pos.Quantity,
                    Unit = InvestmentUnit.Share,
                    CostBasis = pos.CostBasis,
                    Source = InvestmentSource.TradeRepublic,
                    ExternalId = pos.Isin,
                };
                _context.Investments.Add(inv);
                created++;
            }
            else
            {
                inv.Quantity = pos.Quantity;
                inv.CostBasis = pos.CostBasis;
                inv.Name = pos.Name;
                inv.ExternalId = pos.Isin;
                inv.Source = InvestmentSource.TradeRepublic;
                // Le type n'est PAS réécrit ici : Trade Republic ne distingue pas une
                // obligation d'un fonds actions (vérifié le 25/08, le fonds obligataire à
                // échéance sort en « fund »). L'import propose un type à la création, le
                // choix fait à la main dans l'application prime ensuite.
                updated++;
            }

            await _context.SaveChangesAsync();

            if (snap.MarketValue.HasValue)
            {
                var existing = await _context.InvestmentValuations
                    .FirstOrDefaultAsync(v => v.InvestmentId == inv.Id && v.AsOf == today);

                if (existing != null)
                {
                    existing.MarketValue = snap.MarketValue.Value;
                    existing.UnitPrice = snap.CurrentPrice;
                    existing.Source = ValuationSource.TradeRepublic;
                }
                else
                {
                    _context.InvestmentValuations.Add(new InvestmentValuation
                    {
                        InvestmentId = inv.Id,
                        AsOf = today,
                        MarketValue = snap.MarketValue.Value,
                        UnitPrice = snap.CurrentPrice,
                        Source = ValuationSource.TradeRepublic,
                    });
                }

                valued++;
            }

            // Historique de cours : un point par jour de bourse, sur un an. On n'écrase
            // jamais une valorisation existante, la valeur réelle du jour prime toujours.
            if (snap.PriceHistory.Count > 0)
            {
                var datesConnues = await _context.InvestmentValuations
                    .Where(v => v.InvestmentId == inv.Id)
                    .Select(v => v.AsOf)
                    .ToListAsync();

                var deja = datesConnues.ToHashSet();

                // La valorisation du jour vient d'être ajoutée au contexte et n'est pas
                // encore en base : une requête SQL ne la voit pas. Sans cette ligne, le jour
                // où Trade Republic renvoie un agrégat pour la séance en cours, l'index
                // unique (InvestmentId, AsOf) fait échouer tout l'import.
                deja.Add(today);

                foreach (var point in snap.PriceHistory)
                {
                    if (!deja.Add(point.AsOf)) continue;

                    _context.InvestmentValuations.Add(new InvestmentValuation
                    {
                        InvestmentId = inv.Id,
                        AsOf = point.AsOf,
                        UnitPrice = point.Close,
                        // Quantité actuelle appliquée à un cours ancien : c'est ce qui rend
                        // cette ligne inapte à la courbe du patrimoine, et la source le dit.
                        MarketValue = point.Close * inv.Quantity,
                        Source = ValuationSource.TradeRepublicHistory,
                    });
                    historyPoints++;
                }
            }

            await _context.SaveChangesAsync();
        }

        // Une position vendue disparaît simplement de la réponse : sans cette détection
        // elle resterait active avec sa dernière valorisation, comptée indéfiniment.
        var isinsPresents = snapshots.Select(s => s.Position.Isin).ToHashSet();
        var lignesDuTableau = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .ToListAsync();

        var aArchiver = SoldPositionDetector.LinesToArchive(lignesDuTableau, isinsPresents);
        foreach (var ligne in aArchiver)
        {
            ligne.IsArchived = true;
            _logger.LogInformation(
                "Import Trade Republic : la ligne {LigneId} ({Nom}) a disparu du portefeuille, archivée.",
                ligne.Id, ligne.Name);
        }
        if (aArchiver.Count > 0) await _context.SaveChangesAsync();

        return Ok(new TradeRepublicImportResultDto
        {
            Total = snapshots.Count,
            Created = created,
            Updated = updated,
            Valued = valued,
            HistoryPoints = historyPoints,
            CashBalance = import.CashBalance,
            Archived = aArchiver.Count,
        });
    }

    /// <summary>Historique des valorisations d'une ligne, de la plus récente à la plus ancienne.</summary>
    [HttpGet("{id}/valuations")]
    public async Task<ActionResult<List<InvestmentValuationDto>>> GetValuations(int id)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        var valuations = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .Select(v => new InvestmentValuationDto
            {
                Id = v.Id,
                InvestmentId = v.InvestmentId,
                AsOf = v.AsOf,
                UnitPrice = v.UnitPrice,
                MarketValue = v.MarketValue,
                Source = v.Source,
            })
            .ToListAsync();

        return Ok(valuations);
    }
}
