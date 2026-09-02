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
public class InvestmentController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<InvestmentController> _logger;

    public InvestmentController(AppDbContext context, ILogger<InvestmentController> logger)
    {
        _context = context;
        _logger = logger;
    }

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
            IsPersonal = i.IsPersonal,
            CreatedAt = i.CreatedAt,
            UnitCost = InvestmentCalculator.ComputeUnitCost(i.Kind, i.CostBasis, i.Quantity),
            MarketValue = marketValue,
            UnitPrice = latest?.UnitPrice,
            ValuationAsOf = latest?.AsOf,
            IsStale = latest != null && InvestmentCalculator.IsStale(latest.Source, latest.AsOf, now),
            GainAmount = gainAmount,
            GainPercent = gainPercent,
            AnnualizedReturn = latest == null
                ? null
                : InvestmentCalculator.ComputeCagr(i.CostBasis, marketValue, i.FirstPurchaseDate, latest.AsOf),
        };
    }

    /// <summary>
    /// Les investissements que le dashboard demandé doit montrer.
    ///
    /// Un dashboard perso (tous ses comptes logiques marqués IsPersonalScope) voit les lignes marquées
    /// perso, où qu'elles soient rangées. Elles restent dans le portefeuille commun, qui accueillera
    /// aussi celles d'Audrey, donc les deux patrimoines se recouvrent de ces lignes : c'est voulu
    /// (demande du 31/08/2026). Sans ça le dashboard perso montrait les dépenses du compte Argenta
    /// perso et aucun rendement de l'épargne que ce compte alimente.
    ///
    /// Tout autre dashboard garde le périmètre historique : les lignes qui lui sont rattachées.
    /// </summary>
    private async Task<IQueryable<Investment>> InvestmentsInScopeAsync(int dashboardId, int userId)
    {
        var scopes = await _context.DashboardAccounts
            .Where(da => da.DashboardId == dashboardId)
            .Select(da => da.Account.IsPersonalScope)
            .Distinct()
            .ToListAsync();

        var isPersoDashboard = scopes.Contains(true) && !scopes.Contains(false);
        if (!isPersoDashboard)
            return _context.Investments.Where(i => i.DashboardId == dashboardId);

        var accessibles = _context.DashboardMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.DashboardId);

        return _context.Investments.Where(i => i.IsPersonal && accessibles.Contains(i.DashboardId));
    }

    [HttpGet]
    public async Task<ActionResult<List<InvestmentDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await (await InvestmentsInScopeAsync(dashboardId, userId))
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
        if (dto.IsPersonal.HasValue) investment.IsPersonal = dto.IsPersonal.Value;

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
        // Un ménage peut détenir plusieurs comptes Trade Republic (un par personne).
        // Ne renvoyer que le dernier rafraîchi ferait disparaître les autres du
        // patrimoine sans le moindre signe.
        var connections = await _context.BankConnections
            .Where(bc => bc.UserId == userId
                      && bc.Provider == BankProvider.TradeRepublic
                      && bc.CashBalance != null)
            .ToListAsync();

        if (connections.Count == 0)
            return Ok(new CashBalanceDto { Amount = null, UpdatedAt = null });

        // La date affichée est celle du relevé le plus ancien : c'est elle qui date
        // réellement le total.
        return Ok(new CashBalanceDto
        {
            Amount = connections.Sum(bc => bc.CashBalance),
            UpdatedAt = connections.Min(bc => bc.CashBalanceUpdatedAt),
        });
    }

    /// <summary>
    /// Où lire la série de valorisations du portefeuille. Un dashboard normal lit la sienne. Un
    /// dashboard perso, qui ne porte aucune série (l'import écrit au nom du dashboard depuis lequel il
    /// tourne), lit celles de tous les dashboards de l'utilisateur : ce sont les mêmes lignes TR.
    /// </summary>
    private async Task<List<int>> PortfolioSeriesDashboardIdsAsync(int dashboardId, int userId)
    {
        var propre = await _context.PortfolioValuations.AnyAsync(v => v.DashboardId == dashboardId);
        if (propre) return new List<int> { dashboardId };

        return await _context.DashboardMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.DashboardId)
            .ToListAsync();
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<InvestmentHistoryPointDto>>> GetHistory([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await (await InvestmentsInScopeAsync(dashboardId, userId))
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

        // Série réelle Trade Republic quand elle existe : elle remplace la reconstitution pour
        // les lignes TR (quantités du jour, positions vendues comprises), les autres lignes
        // s'y ajoutent. Voir InvestmentCalculator.MergeWithPortfolioSeries.
        // La série réelle est écrite au nom du dashboard depuis lequel l'import Trade Republic a été
        // lancé, c'est-à-dire le commun. Le dashboard perso montre les mêmes lignes TR : il lit donc la
        // série de n'importe quel dashboard de l'utilisateur, sinon sa courbe se réduirait à la
        // reconstitution ligne à ligne alors que l'historique réel existe (demande du 31/08/2026).
        var seriesDashboardIds = await PortfolioSeriesDashboardIdsAsync(dashboardId, userId);
        var trSeries = (await _context.PortfolioValuations
            .Where(v => seriesDashboardIds.Contains(v.DashboardId))
            .OrderBy(v => v.AsOf)
            .Select(v => new { v.AsOf, v.MarketValue, v.Invested })
            .ToListAsync())
            .GroupBy(v => v.AsOf)
            .Select(g => g.OrderByDescending(v => v.MarketValue).First())
            .OrderBy(v => v.AsOf)
            .Select(v => (v.AsOf, v.MarketValue, v.Invested))
            .ToList();

        if (trSeries.Count > 0)
        {
            var otherLines = investments
                .Where(i => i.Source != InvestmentSource.TradeRepublic)
                .Select(i => new PortfolioLine(
                    i.CostBasis,
                    i.IsArchived,
                    valuationsByInvestment.GetValueOrDefault(i.Id) ?? Array.Empty<(DateTime, decimal)>()))
                .ToList();
            var othersHistory = InvestmentCalculator.ComputePortfolioHistory(otherLines);
            history = InvestmentCalculator.MergeWithPortfolioSeries(trSeries, othersHistory, history);
        }

        var result = history
            .Select(p => new InvestmentHistoryPointDto
            {
                AsOf = p.AsOf,
                Value = p.Value,
                Invested = p.Invested,
                Reconstructed = p.Reconstructed,
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

        var scopedIds = await (await InvestmentsInScopeAsync(dashboardId, userId))
            .Select(i => i.Id)
            .ToListAsync();

        var valuations = await _context.InvestmentValuations
            .Where(v => scopedIds.Contains(v.InvestmentId) && !v.Investment.IsArchived)
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
            .FirstOrDefaultAsync(bc => bc.UserId == userId && bc.Provider == BankProvider.TradeRepublic
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

            // Et exposé comme un compte bancaire, pour entrer dans les soldes par compte, le KPI
            // Solde global et la courbe du solde total (demande du 31/08/2026).
            await TradeRepublicCashAccount.UpsertAsync(_context, connection);
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
                    // Le compte Trade Republic est celui de Sébastien, alimenté depuis son Argenta
                    // perso : une ligne qui en vient est perso, donc visible sur son dashboard en plus
                    // du portefeuille commun. Une ligne déjà en base garde le drapeau qu'elle porte.
                    IsPersonal = true,
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

        // Valeur du portefeuille depuis le début, rebâtie depuis la timeline complète. Un échec
        // ici ne remet pas en cause l'import des positions, déjà enregistré.
        var reconstruction = new ReconstructionResult();
        try
        {
            reconstruction = await ReconstructFromTimelineAsync(dashboardId, sessionToken, refreshToken, deviceToken, trClient, defaultHolder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import Trade Republic : reconstruction de l'historique du portefeuille échouée.");
        }

        return Ok(new TradeRepublicImportResultDto
        {
            Total = snapshots.Count,
            Created = created,
            Updated = updated,
            Valued = valued,
            HistoryPoints = historyPoints,
            PortfolioHistoryPoints = reconstruction.Points,
            Movements = reconstruction.Movements,
            SoldLinesAdded = reconstruction.SoldLinesAdded,
            IsinsWithoutPrices = reconstruction.IsinsWithoutPrices,
            HistoryIncomplete = reconstruction.Incomplete,
            CashBalance = import.CashBalance,
            Archived = aArchiver.Count,
        });
    }

    private sealed class ReconstructionResult
    {
        public int Points { get; set; }
        public int Movements { get; set; }
        public int SoldLinesAdded { get; set; }
        public List<string> IsinsWithoutPrices { get; set; } = new();
        public bool Incomplete { get; set; }
    }

    /// <summary>
    /// Lit la timeline Trade Republic complète, enregistre les mouvements de titres dans
    /// InvestmentMovements (dédupliqués par ExternalId « tr-… »), crée en lignes archivées les
    /// positions vendues qui n'existent plus, récupère leurs cours, puis rebâtit la valeur du
    /// portefeuille jour par jour dans PortfolioValuations (source Reconstructed).
    /// Voir InvestmentCalculator.ReconstructPortfolioHistory pour la méthode et ses limites.
    /// </summary>
    private async Task<ReconstructionResult> ReconstructFromTimelineAsync(
        int dashboardId, string sessionToken, string refreshToken, string deviceToken, TradeRepublicClient trClient, string defaultHolder)
    {
        var result = new ReconstructionResult();
        var timeline = await trClient.GetTimelineAllAsync(sessionToken, refreshToken, deviceToken);
        if (timeline.Count == 0) return result;

        // Diagnostic (28/08/2026) : les cryptos et les ventes manquent à la reconstruction, leur
        // eventType n'est pas reconnu. On écrit la timeline lue (sans données de carte : id, date,
        // montant, libellé, sous-titre, type, ISIN) à côté de la base, pour l'analyser telle quelle.
        try
        {
            var cs = _context.Database.GetConnectionString() ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(cs, @"Data Source=([^;]+)");
            var dir = m.Success ? Path.GetDirectoryName(Path.GetFullPath(m.Groups[1].Value)) : null;
            var chemin = Path.Combine(dir ?? AppContext.BaseDirectory, "tr-timeline-dump.json");
            await System.IO.File.WriteAllTextAsync(chemin, System.Text.Json.JsonSerializer.Serialize(
                timeline.Select(t => new { t.Id, t.Date, t.Amount, t.Title, t.Subtitle, t.EventType, t.Isin }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
            _logger.LogInformation("TR timeline : {n} lignes écrites dans {chemin}.", timeline.Count, chemin);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TR timeline : écriture du dump impossible.");
        }

        var lignes = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .ToListAsync();
        var parIsin = lignes.Where(i => i.Isin != null).ToDictionary(i => i.Isin!, i => i);
        var parNom = lignes.GroupBy(i => i.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Mouvements de titres : eventType d'abord, libellé exact d'un instrument connu à défaut.
        var instrumentNames = lignes.Select(i => i.Name).ToList();
        var mouvements = new List<(TradeRepublicPortfolioParser.TrTimelineItem Item, string Isin)>();
        var sansIsin = 0;
        foreach (var item in timeline)
        {
            var kind = TradeRepublicTimelineClassifier.Classify(item.Title, item.EventType, [], instrumentNames);
            if (kind != TrLineKind.Investment) continue;

            var isin = item.Isin;
            if (isin == null && parNom.TryGetValue(item.Title.Trim(), out var ligne)) isin = ligne.Isin;
            if (isin == null) { sansIsin++; continue; }
            mouvements.Add((item, isin));
        }
        if (sansIsin > 0)
            _logger.LogInformation("TR timeline : {n} mouvement(s) de titres sans ISIN identifiable, ignorés pour la reconstruction.", sansIsin);
        if (mouvements.Count == 0) return result;

        // Positions vendues : une ligne archivée par ISIN inconnu, avec son historique de cours.
        foreach (var isin in mouvements.Select(m => m.Isin).Distinct().Where(i => !parIsin.ContainsKey(i)).ToList())
        {
            var titre = mouvements.Where(m => m.Isin == isin).OrderByDescending(m => m.Item.Date).First().Item.Title;
            var vendue = new Investment
            {
                DashboardId = dashboardId,
                Name = string.IsNullOrWhiteSpace(titre) ? isin : titre,
                Holder = defaultHolder,
                Kind = InvestmentKindClassifier.FromTradeRepublic(isin, ""),
                Isin = isin,
                Quantity = 0,
                Unit = InvestmentUnit.Share,
                CostBasis = 0,
                Source = InvestmentSource.TradeRepublic,
                ExternalId = isin,
                IsArchived = true,
                FirstPurchaseDate = mouvements.Where(m => m.Isin == isin).Min(m => m.Item.Date).Date,
            };
            _context.Investments.Add(vendue);
            await _context.SaveChangesAsync();
            parIsin[isin] = vendue;
            result.SoldLinesAdded++;

            var cours = await trClient.GetPriceHistoryAsync(isin, sessionToken);
            foreach (var point in cours)
            {
                _context.InvestmentValuations.Add(new InvestmentValuation
                {
                    InvestmentId = vendue.Id,
                    AsOf = point.AsOf,
                    UnitPrice = point.Close,
                    MarketValue = 0,
                    Source = ValuationSource.TradeRepublicHistory,
                });
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("TR timeline : position vendue {isin} ({nom}) ajoutée en ligne archivée, {points} cours.", isin, vendue.Name, cours.Count);
        }

        // Cours par ISIN : toutes les valorisations qui portent un cours unitaire, quelle que soit la source.
        var ids = parIsin.Values.Select(i => i.Id).ToList();
        var coursParLigne = (await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId) && v.UnitPrice != null)
            .Select(v => new { v.InvestmentId, v.AsOf, v.UnitPrice })
            .ToListAsync())
            .GroupBy(v => v.InvestmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(DateTime, decimal)>)g.Select(v => (v.AsOf, v.UnitPrice!.Value)).ToList());
        var prices = parIsin
            .Where(kv => coursParLigne.ContainsKey(kv.Value.Id))
            .ToDictionary(kv => kv.Key, kv => coursParLigne[kv.Value.Id]);

        var timelineMovements = mouvements
            .Select(m => new InvestmentCalculator.TimelineMovement(m.Isin, m.Item.Date, m.Item.Amount))
            .ToList();
        // Calibrage sur les quantités détenues (voir ReconstructPortfolioHistoryCalibrated) : la
        // timeline TR commence au 24/11/2023, ce qui a été acheté avant entre en position d'ouverture.
        var detenues = parIsin.Values
            .Where(i => !i.IsArchived && i.Isin != null && i.Quantity > 0)
            .ToDictionary(i => i.Isin!, i => i.Quantity);
        var (points, fills, isinsSansCours, openings) = InvestmentCalculator.ReconstructPortfolioHistoryCalibrated(
            timelineMovements, prices, detenues, DateTime.UtcNow.Date);
        result.IsinsWithoutPrices = isinsSansCours;
        if (openings.Count > 0)
            _logger.LogInformation("TR reconstruction : {n} position(s) d'ouverture au {date:yyyy-MM-dd} : {detail}",
                openings.Count, openings[0].Date,
                string.Join(", ", openings.Select(o => $"{o.Isin} {(-o.Amount):0.00} EUR")));

        // Garde-fou (28/08/2026) : une timeline tronquée (pagination refusée) a produit une
        // « valeur du portefeuille » de 238 € à partir de quatre achats d'août, qui a remplacé la
        // vraie courbe à l'écran. La reconstruction n'est retenue que si son dernier point recolle
        // à la valeur réelle des lignes TR détenues aujourd'hui. Sinon on n'écrit rien, on efface
        // ce qu'une reconstruction précédente aurait laissé, et on le dit.
        var valeurReelle = await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId) && !v.Investment.IsArchived
                        && v.Source != ValuationSource.TradeRepublicHistory)
            .GroupBy(v => v.InvestmentId)
            .Select(g => g.OrderByDescending(v => v.AsOf).First().MarketValue)
            .ToListAsync();
        var totalReel = valeurReelle.Sum();
        var dernier = points.Count > 0 ? points[^1].Value : 0m;
        var coherent = totalReel <= 0m || (dernier >= totalReel * 0.8m && dernier <= totalReel * 1.2m);
        if (!coherent)
        {
            var anciennes = await _context.PortfolioValuations
                .Where(v => v.DashboardId == dashboardId && v.Source == ValuationSource.Reconstructed)
                .ToListAsync();
            _context.PortfolioValuations.RemoveRange(anciennes);
            await _context.SaveChangesAsync();
            result.Incomplete = true;
            _logger.LogWarning(
                "TR reconstruction rejetée : dernier point {dernier} EUR contre {reel} EUR de lignes détenues ({mouvements} mouvements lus depuis {debut:yyyy-MM-dd}). Timeline probablement tronquée. {supprimes} point(s) précédent(s) effacé(s).",
                dernier, totalReel, timelineMovements.Count, timelineMovements.Min(m => m.Date), anciennes.Count);
            return result;
        }

        // Mouvements : dédupliqués par ExternalId, quantité et cours tels que retenus par la reconstruction.
        var fillParCle = fills.Where(f => !f.Movement.IsOpening).ToDictionary(f => (f.Movement.Isin, f.Movement.Date, f.Movement.Amount));
        var externalIds = mouvements.Select(m => $"{PersoScopeRouter.TradeRepublicExternalIdPrefix}{m.Item.Id}").ToList();
        var dejaLa = (await _context.InvestmentMovements
            .Where(mv => mv.ExternalId != null && externalIds.Contains(mv.ExternalId))
            .Select(mv => mv.ExternalId!)
            .ToListAsync()).ToHashSet();
        foreach (var (item, isin) in mouvements)
        {
            var externalId = $"{PersoScopeRouter.TradeRepublicExternalIdPrefix}{item.Id}";
            if (string.IsNullOrEmpty(item.Id) || !dejaLa.Add(externalId)) continue;
            fillParCle.TryGetValue((isin, item.Date, item.Amount), out var fill);
            _context.InvestmentMovements.Add(new InvestmentMovement
            {
                InvestmentId = parIsin[isin].Id,
                Type = item.Amount < 0 ? MovementType.Buy : MovementType.Sell,
                Date = item.Date,
                Quantity = fill?.Quantity ?? 0,
                UnitPrice = fill?.UnitPrice ?? 0,
                Amount = item.Amount,
                ExternalId = externalId,
                Source = InvestmentSource.TradeRepublic,
            });
            result.Movements++;
        }
        await _context.SaveChangesAsync();

        // Série du portefeuille : remplace la reconstruction précédente point par point.
        var existantes = await _context.PortfolioValuations
            .Where(v => v.DashboardId == dashboardId)
            .ToDictionaryAsync(v => v.AsOf);
        foreach (var point in points)
        {
            if (existantes.TryGetValue(point.AsOf, out var pv))
            {
                if (pv.MarketValue == point.Value && pv.Invested == point.Invested) continue;
                pv.MarketValue = point.Value;
                pv.Invested = point.Invested;
                pv.Source = ValuationSource.Reconstructed;
            }
            else
            {
                var nouvelle = new PortfolioValuation
                {
                    DashboardId = dashboardId,
                    AsOf = point.AsOf,
                    MarketValue = point.Value,
                    Invested = point.Invested,
                    Source = ValuationSource.Reconstructed,
                };
                _context.PortfolioValuations.Add(nouvelle);
                existantes[point.AsOf] = nouvelle;
            }
            result.Points++;
        }
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "TR reconstruction : {mouvements} mouvement(s), {points} point(s) du {debut:yyyy-MM-dd} au {fin:yyyy-MM-dd}, {vendues} vendue(s), sans cours : {sansCours}.",
            result.Movements, points.Count,
            points.Count > 0 ? points[0].AsOf : DateTime.MinValue,
            points.Count > 0 ? points[^1].AsOf : DateTime.MinValue,
            result.SoldLinesAdded, string.Join(", ", isinsSansCours));
        return result;
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
