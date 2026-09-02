using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

/// <summary>
/// Les transactions : lecture, écriture, drapeaux, et les endpoints de reporting qui délèguent à
/// <see cref="ReportingService"/> et <see cref="AccountBalanceService"/>. Le contrôleur n'agrège
/// rien lui-même depuis le 02/09/2026 : il résout le périmètre (les comptes logiques du dashboard)
/// et rend ce que les services calculent.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;
    private readonly ReportingService _reporting;
    private readonly AccountBalanceService _balances;

    public TransactionController(
        AppDbContext context,
        IDashboardService dashboardService,
        ReportingService reporting,
        AccountBalanceService balances)
    {
        _context = context;
        _dashboardService = dashboardService;
        _reporting = reporting;
        _balances = balances;
    }

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    /// <summary>
    /// Le dashboard personnel de l'utilisateur, celui créé à l'inscription. Cible de repli quand un
    /// endpoint est appelé sans dashboardId.
    /// </summary>
    private async Task<int?> PersonalDashboardIdAsync()
    {
        return await _dashboardService.GetPersonalDashboardIdAsync(GetUserId());
    }

    /// <summary>Les comptes logiques visibles : ceux du dashboard demandé, sinon ceux du dashboard personnel.</summary>
    private async Task<List<int>> GetAccountIds(int? dashboardId)
    {
        var userId = GetUserId();
        var effective = dashboardId ?? await PersonalDashboardIdAsync();
        if (effective == null) return new List<int>();
        return await _dashboardService.GetDashboardAccountIds(effective.Value, userId);
    }

    /// <summary>
    /// Caractère d'échappement des jokers LIKE. Le point d'exclamation plutôt que l'antislash : SQLite
    /// accepte n'importe quel caractère, et celui-là n'a pas besoin d'être échappé lui-même en C#.
    /// </summary>
    private const string EchappementLike = "!";

    /// <summary>
    /// Neutralise les jokers d'un motif LIKE saisi par l'utilisateur. Sans ça, chercher « 100% » ou
    /// « _ » renverrait n'importe quoi.
    /// </summary>
    private static string EchapperLike(string valeur) =>
        valeur.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    private static TransactionDto MapToDto(Transaction t)
    {
        return new TransactionDto
        {
            Id = t.Id,
            Amount = t.Amount,
            Description = t.Description,
            Date = t.Date,
            Type = t.Type,
            CategoryId = t.CategoryId,
            CategoryName = t.Category.Name,
            CategoryIcon = t.Category.Icon,
            CategoryColor = t.Category.Color,
            AccountId = t.AccountId,
            AccountName = t.Account.Name,
            ExternalId = t.ExternalId,
            IsImported = t.IsImported,
            CounterpartyName = t.CounterpartyName,
            CounterpartyIban = t.CounterpartyIban,
            IsExceptional = t.IsExceptional,
            IsRefund = t.IsRefund,
            CategorySetManuallyAt = t.CategorySetManuallyAt,
            CategoryBeforeManualName = t.CategoryBeforeManual?.Name,
            IsFixed = t.IsFixed,
            IsProvisional = t.IsProvisional,
            BankAccountName = t.BankAccount?.AccountName,
            BankInstitutionName = t.BankAccount?.BankConnection?.InstitutionName,
            ProjectEnvelopeId = t.ProjectEnvelopeId,
            ProjectEnvelopeName = t.ProjectEnvelope?.Name
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetAll(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? categoryId,
        [FromQuery] TransactionType? type,
        [FromQuery] int? accountId,
        [FromQuery] int? bankAccountId,
        [FromQuery] bool? isExceptional,
        [FromQuery] bool? isFixed,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool? sortDesc)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .Where(t => accountIds.Contains(t.AccountId));

        if (from.HasValue) query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue) query = query.Where(t => t.Date <= to.Value);
        if (categoryId.HasValue) query = query.Where(t => t.CategoryId == categoryId.Value);
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);
        if (accountId.HasValue) query = query.Where(t => t.AccountId == accountId.Value);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);
        if (isExceptional.HasValue) query = query.Where(t => t.IsExceptional == isExceptional.Value);
        if (isFixed.HasValue) query = query.Where(t => t.IsFixed == isFixed.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // LIKE et non Contains : EF traduit Contains par instr(), qui compare octet par octet, donc
            // la recherche était sensible à la casse. Mesuré sur la prod le 31/08/2026 : « colruyt » ne
            // rendait aucune ligne, « COLRUYT » en rendait cinq, et une comparaison insensible à la
            // casse soixante-dix-huit. LIKE est insensible à la casse sur l'ASCII dans SQLite.
            // Les accents restent non gérés (« Intermarche » ne trouve pas « Intermarché »), il faudrait
            // une colonne normalisée pour ça.
            //
            // La contrepartie et son IBAN sont cherchables : sur un virement, le libellé arrive souvent
            // vide et c'est le bénéficiaire qui identifie la ligne (crèche communale, école, assurance).
            var motif = $"%{EchapperLike(search)}%";
            var chercherIban = CategoryRuleMatcher.LooksLikeIban(search);
            var motifIban = chercherIban
                ? $"%{EchapperLike(GoCardlessTransactionFields.Normalize(search))}%"
                : motif;

            query = query.Where(t => EF.Functions.Like(t.Description, motif, EchappementLike)
                                  || EF.Functions.Like(t.Category.Name, motif, EchappementLike)
                                  || EF.Functions.Like(t.Account.Name, motif, EchappementLike)
                                  || (t.CounterpartyName != null && EF.Functions.Like(t.CounterpartyName, motif, EchappementLike))
                                  || (chercherIban && t.CounterpartyIban != null && EF.Functions.Like(t.CounterpartyIban, motifIban, EchappementLike)));
        }

        var descending = sortDesc ?? true;
        query = sortBy?.ToLower() switch
        {
            "description" => descending ? query.OrderByDescending(t => t.Description) : query.OrderBy(t => t.Description),
            "account" => descending ? query.OrderByDescending(t => t.Account.Name) : query.OrderBy(t => t.Account.Name),
            "category" => descending ? query.OrderByDescending(t => t.Category.Name) : query.OrderBy(t => t.Category.Name),
            "amount" => descending ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            _ => descending ? query.OrderByDescending(t => t.Date) : query.OrderBy(t => t.Date),
        };

        var transactions = await query
            .Select(t => MapToDto(t))
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionDto>> GetById(int id)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        return Ok(MapToDto(transaction));
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(CreateTransactionDto dto)
    {
        var userId = GetUserId();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return BadRequest("Compte invalide.");

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        var transaction = new Transaction
        {
            Amount = dto.Amount,
            Description = dto.Description,
            Date = dto.Date,
            Type = dto.Type,
            CategoryId = dto.CategoryId,
            AccountId = dto.AccountId
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = transaction.Id }, new TransactionDto
        {
            Id = transaction.Id,
            Amount = transaction.Amount,
            Description = transaction.Description,
            Date = transaction.Date,
            Type = transaction.Type,
            CategoryId = transaction.CategoryId,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
            CategoryColor = category.Color,
            AccountId = account.Id,
            AccountName = account.Name,
            ExternalId = transaction.ExternalId,
            IsImported = transaction.IsImported,
            CounterpartyName = transaction.CounterpartyName,
            IsExceptional = transaction.IsExceptional,
            IsFixed = transaction.IsFixed,
            IsProvisional = transaction.IsProvisional
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TransactionDto>> Update(int id, UpdateTransactionDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return BadRequest("Compte invalide.");

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        transaction.Amount = dto.Amount;
        transaction.Description = dto.Description;
        transaction.Date = dto.Date;
        transaction.Type = dto.Type;
        // Une catégorie choisie ici l'est à la main : on garde la trace pour le tri suivant.
        ManualCategoryTrace.Apply(transaction, dto.CategoryId, DateTime.UtcNow);
        transaction.AccountId = dto.AccountId;

        await _context.SaveChangesAsync();

        return Ok(new TransactionDto
        {
            Id = transaction.Id,
            Amount = transaction.Amount,
            Description = transaction.Description,
            Date = transaction.Date,
            Type = transaction.Type,
            CategoryId = transaction.CategoryId,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
            CategoryColor = category.Color,
            AccountId = account.Id,
            AccountName = account.Name,
            ExternalId = transaction.ExternalId,
            IsImported = transaction.IsImported,
            CounterpartyName = transaction.CounterpartyName,
            IsExceptional = transaction.IsExceptional,
            IsRefund = transaction.IsRefund,
            CategorySetManuallyAt = transaction.CategorySetManuallyAt,
            IsFixed = transaction.IsFixed,
            IsProvisional = transaction.IsProvisional,
            BankAccountName = transaction.BankAccount?.AccountName,
            BankInstitutionName = transaction.BankAccount?.BankConnection?.InstitutionName,
            ProjectEnvelopeId = transaction.ProjectEnvelopeId,
            ProjectEnvelopeName = transaction.ProjectEnvelope?.Name
        });
    }

    [HttpPut("{id}/exceptional")]
    public async Task<ActionResult<TransactionDto>> SetExceptional(int id, SetExceptionalDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        transaction.IsExceptional = dto.IsExceptional;
        await _context.SaveChangesAsync();

        return Ok(MapToDto(transaction));
    }

    /// <summary>Marque (ou démarque) une transaction comme charge fixe.</summary>
    [HttpPut("{id}/fixed")]
    public async Task<ActionResult<TransactionDto>> SetFixed(int id, SetFixedDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        transaction.IsFixed = dto.IsFixed;
        await _context.SaveChangesAsync();

        return Ok(MapToDto(transaction));
    }

    /// <summary>
    /// Marque (ou démarque) un revenu comme remboursement d'une dépense. La ligne sort alors du bloc
    /// ENTRÉES du bilan et s'impute en négatif sur le bloc de sa catégorie (voir Refunds).
    /// </summary>
    [HttpPut("{id}/refund")]
    public async Task<ActionResult<TransactionDto>> SetRefund(int id, SetRefundDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        transaction.IsRefund = dto.IsRefund;
        await _context.SaveChangesAsync();

        return Ok(MapToDto(transaction));
    }

    /// <summary>
    /// Change la seule catégorie d'une transaction, et garde la trace que le choix est humain.
    /// Utilisé par le détail d'une catégorie, où l'on reclasse une ligne sans rien toucher d'autre.
    /// </summary>
    [HttpPut("{id}/category")]
    public async Task<ActionResult<TransactionDto>> SetCategory(int id, SetCategoryDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.CategoryBeforeManual)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        if (ManualCategoryTrace.Apply(transaction, dto.CategoryId, DateTime.UtcNow))
        {
            await _context.SaveChangesAsync();
            await _context.Entry(transaction).Reference(t => t.Category).LoadAsync();
            await _context.Entry(transaction).Reference(t => t.CategoryBeforeManual).LoadAsync();
        }

        return Ok(MapToDto(transaction));
    }

    /// <summary>
    /// Depuis quand l'historique du dashboard est un bilan, et depuis quand il n'est qu'un relevé.
    ///
    /// Pourquoi (31/08/2026) : la timeline Trade Republic remonte à novembre 2023 alors que les comptes
    /// bancaires ont été connectés en 2026. Sur « Tout », l'app additionnait donc deux ans de dépenses
    /// carte sans un seul revenu en face, et affichait un net de −26 000 €. La première transaction
    /// bancaire borne la période où les deux côtés existent.
    /// </summary>
    [HttpGet("coverage")]
    public async Task<ActionResult<CoverageDto>> GetCoverage([FromQuery] int? dashboardId)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new CoverageDto());

        var portee = _context.Transactions.Where(t => accountIds.Contains(t.AccountId));

        return Ok(new CoverageDto
        {
            FirstBankTransactionDate = await portee
                .Where(t => t.BankAccountId != null)
                .OrderBy(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync(),
            FirstTransactionDate = await portee
                .OrderBy(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync(),
        });
    }

    /// <summary>
    /// Les catégories corrigées à la main, de la plus récente à la plus ancienne. À lire avant chaque
    /// séance de tri : chaque ligne dit qu'une règle manque ou se trompe (ManualCategoryTrace).
    /// </summary>
    [HttpGet("manual-recategorizations")]
    public async Task<ActionResult<List<ManualRecategorizationDto>>> GetManualRecategorizations(
        [FromQuery] int? dashboardId,
        [FromQuery] int limit = 100)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<ManualRecategorizationDto>());

        var lignes = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.CategorySetManuallyAt != null)
            .OrderByDescending(t => t.CategorySetManuallyAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(t => new ManualRecategorizationDto
            {
                TransactionId = t.Id,
                Date = t.Date,
                Description = t.Description,
                CounterpartyName = t.CounterpartyName,
                Amount = t.Amount,
                FromCategory = t.CategoryBeforeManual != null ? t.CategoryBeforeManual.Name : null,
                ToCategory = t.Category.Name,
                CorrectedAt = t.CategorySetManuallyAt!.Value,
            })
            .ToListAsync();

        return Ok(lignes);
    }

    /// <summary>Rattache (ou détache si null) une transaction à une enveloppe projet.</summary>
    [HttpPut("{id}/envelope")]
    public async Task<ActionResult<TransactionDto>> SetEnvelope(int id, SetEnvelopeDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        if (dto.ProjectEnvelopeId.HasValue)
        {
            // Valider que l'enveloppe existe et appartient à un dashboard accessible par le user
            var envelope = await _context.ProjectEnvelopes
                .FirstOrDefaultAsync(e => e.Id == dto.ProjectEnvelopeId.Value);
            if (envelope == null) return BadRequest("Enveloppe projet introuvable.");
            if (!await UserCanAccessDashboard(envelope.DashboardId, userId)) return Forbid();

            transaction.ProjectEnvelopeId = envelope.Id;
        }
        else
        {
            transaction.ProjectEnvelopeId = null;
        }

        await _context.SaveChangesAsync();
        await _context.Entry(transaction).Reference(t => t.ProjectEnvelope).LoadAsync();

        return Ok(MapToDto(transaction));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("recategorize")]
    public async Task<ActionResult<object>> Recategorize()
    {
        var userId = GetUserId();

        // Même ordre qu'à l'import : le mot-clé le plus long gagne.
        var rules = await CategoryRuleMatcher
            .InApplicationOrder(_context.CategoryRules.Where(cr => cr.UserId == userId))
            .ToListAsync();

        var defaultCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Name == "Autres" && c.IsDefault);
        var defaultCategoryId = defaultCategory?.Id ?? 10;

        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        // Catégorie : ne toucher que les transactions encore en catégorie par défaut, tout le reste est
        // soit un match de règle antérieur, soit une correction manuelle qu'on ne doit pas écraser.
        // Flag fixe : réappliqué sur toutes les transactions importées qui matchent une règle
        // (permet de rattraper le stock existant quand on coche « charge fixe » sur une règle).
        var transactions = await _context.Transactions
            .Where(t => accounts.Contains(t.AccountId) && t.IsImported)
            .ToListAsync();

        int updated = 0;
        int fixedUpdated = 0;
        foreach (var tx in transactions)
        {
            var rule = CategoryRuleMatcher.FirstMatch(rules, tx.Description, tx.CounterpartyName, tx.CounterpartyIban);
            if (rule == null) continue;

            if (tx.CategoryId == defaultCategoryId)
            {
                tx.CategoryId = rule.CategoryId;
                updated++;
            }
            if (tx.IsFixed != rule.MarkAsFixed)
            {
                tx.IsFixed = rule.MarkAsFixed;
                fixedUpdated++;
            }
            // Le périmètre perso/commun n'est volontairement pas rejoué ici : une ligne déplacée à la
            // main par Sébastien ne doit pas revenir de force au Commun. Voir PersoScopeRouter.
        }

        await _context.SaveChangesAsync();
        return Ok(new { updated, fixedUpdated, total = transactions.Count });
    }

    /// <summary>
    /// Résumé de la période : indicateurs, répartitions et tendance six mois. Règles dans
    /// <see cref="SummaryBuilder"/>, requêtes dans <see cref="ReportingService"/>.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? bankAccountId,
        [FromQuery] bool includeExceptional = true)
    {
        var accountIds = await GetAccountIds(dashboardId);
        return Ok(await _reporting.SummaryAsync(GetUserId(), accountIds, from, to, bankAccountId, includeExceptional));
    }

    /// <summary>Bilan mensuel en cinq blocs : ENTRÉES − FIXE − MISES DE CÔTÉ − VARIABLE = TOTAL, le hors bilan à part.</summary>
    [HttpGet("monthly-report")]
    public async Task<ActionResult<MonthlyReportDto>> GetMonthlyReport(
        [FromQuery] int? dashboardId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (month < 1 || month > 12) return BadRequest("Mois invalide (1-12).");

        var accountIds = await GetAccountIds(dashboardId);
        return Ok(await _reporting.MonthlyReportAsync(accountIds, year, month));
    }

    /// <summary>Dépenses d'une catégorie par mois, nettes des remboursements, part exceptionnelle séparée.</summary>
    [HttpGet("category-history")]
    public async Task<ActionResult<List<CategoryMonthHistoryDto>>> GetCategoryHistory(
        [FromQuery] int? dashboardId,
        [FromQuery] int categoryId,
        [FromQuery] int months = 12)
    {
        var accountIds = await GetAccountIds(dashboardId);
        return Ok(await _reporting.CategoryHistoryAsync(accountIds, categoryId, months));
    }

    /// <summary>
    /// Historique deux sens d'une catégorie, avec le même mois douze mois plus tôt quand la couverture
    /// bancaire le rend comparable. Mêmes filtres que le résumé qui a produit la ligne cliquée.
    /// </summary>
    [HttpGet("category-flow-history")]
    public async Task<ActionResult<CategoryFlowHistoryDto>> GetCategoryFlowHistory(
        [FromQuery] int? dashboardId,
        [FromQuery] int categoryId,
        [FromQuery] int months = 12,
        [FromQuery] int? bankAccountId = null,
        [FromQuery] bool includeExceptional = true)
    {
        var accountIds = await GetAccountIds(dashboardId);
        var result = await _reporting.CategoryFlowHistoryAsync(accountIds, categoryId, months, bankAccountId, includeExceptional);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Reste du mois jour par jour et projection de fin de mois. Remplace la note manuelle d'Audrey.</summary>
    [HttpGet("burndown")]
    public async Task<ActionResult<BurndownDto>> GetBurndown(
        [FromQuery] int? dashboardId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (month < 1 || month > 12) return BadRequest("Mois invalide (1-12).");

        var accountIds = await GetAccountIds(dashboardId);
        var effectiveDashboardId = dashboardId ?? await PersonalDashboardIdAsync();
        return Ok(await _reporting.BurndownAsync(accountIds, effectiveDashboardId, year, month));
    }

    /// <summary>Transactions encore dans la catégorie système « Autres », celle qu'aucune règle n'a attrapée.</summary>
    [HttpGet("uncategorized")]
    public async Task<ActionResult<List<TransactionDto>>> GetUncategorized(
        [FromQuery] int? dashboardId,
        [FromQuery] int? bankAccountId,
        [FromQuery] int limit = 50)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var othersCategoryId = await SystemCategories.AutresIdAsync(_context);
        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .Where(t => accountIds.Contains(t.AccountId) && t.CategoryId == othersCategoryId);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        var raw = await query.ToListAsync();
        var txns = raw.OrderByDescending(t => t.Amount).Take(limit).ToList();
        return Ok(txns.Select(MapToDto).ToList());
    }

    /// <summary>5 dernières transactions, indépendamment de tout filtre.</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<List<TransactionDto>>> GetRecent(
        [FromQuery] int? dashboardId,
        [FromQuery] int? bankAccountId,
        [FromQuery] int limit = 5)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .Where(t => accountIds.Contains(t.AccountId));
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        var txns = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .ToListAsync();

        return Ok(txns.Select(MapToDto).ToList());
    }

    /// <summary>Top dépenses inhabituelles : 3 plus grosses au-dessus de 2× la médiane catégorie.</summary>
    [HttpGet("anomalies")]
    public async Task<ActionResult<List<TransactionDto>>> GetAnomalies(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 3)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var now = DateTime.UtcNow;
        var fromDate = from ?? new DateTime(now.Year, now.Month, 1);
        var toDate = to ?? fromDate.AddMonths(1).AddTicks(-1);

        var historyStart = fromDate.AddMonths(-6);
        var allExpenses = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.Type == TransactionType.Expense
                     && !t.Category.IsTransfer
                     && t.Date >= historyStart
                     && t.Date <= toDate)
            .Select(t => new { t.Id, t.CategoryId, t.Amount, t.Date })
            .ToListAsync();

        var avgByCategory = allExpenses
            .Where(e => e.Date < fromDate)
            .GroupBy(e => e.CategoryId)
            .ToDictionary(g => g.Key, g => g.Average(e => e.Amount));

        var anomalyIds = allExpenses
            .Where(e => e.Date >= fromDate && e.Date <= toDate)
            .Where(e => avgByCategory.TryGetValue(e.CategoryId, out var avg) && e.Amount > avg * 2 && e.Amount > 30)
            .OrderByDescending(e => e.Amount)
            .Take(limit)
            .Select(e => e.Id)
            .ToList();

        if (!anomalyIds.Any()) return Ok(new List<TransactionDto>());

        // SQLite ne sait pas trier par decimal → tri client
        var rawTxns = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .Where(t => anomalyIds.Contains(t.Id))
            .ToListAsync();
        var txns = rawTxns.OrderByDescending(t => t.Amount).ToList();

        return Ok(txns.Select(MapToDto).ToList());
    }
    /// <summary>
    /// Soldes par compte bancaire physique, rétrospectifs si <paramref name="to"/> est antérieur à
    /// maintenant. Voir <see cref="AccountBalanceService"/>.
    /// </summary>
    [HttpGet("account-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> GetAccountBalances(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? to)
    {
        var accountIds = await GetAccountIds(dashboardId);
        return Ok(await _balances.AccountBalancesAsync(GetUserId(), accountIds, to));
    }

    /// <summary>Force la récupération des soldes réels via GoCardless, sans attendre la boucle de six heures.</summary>
    [HttpPost("refresh-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> RefreshBalances(
        [FromQuery] int? dashboardId,
        [FromServices] GoCardlessClient goCardless)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (accountIds.Count == 0) return Ok(new List<AccountBalanceDto>());

        var userId = GetUserId();
        await _balances.RefreshRealBalancesAsync(userId, goCardless);
        return Ok(await _balances.AccountBalancesAsync(userId, accountIds, null));
    }
}
