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
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public TransactionController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    // Récupère les IDs de comptes visibles : soit via dashboardId, soit le dashboard personnel
    private async Task<List<int>> GetAccountIds(int? dashboardId)
    {
        var userId = GetUserId();

        if (dashboardId.HasValue)
            return await _dashboardService.GetDashboardAccountIds(dashboardId.Value, userId);

        // Fallback : dashboard personnel (premier dashboard créé par le user)
        var personalDashboard = await _context.Dashboards
            .Where(d => d.CreatorId == userId)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (personalDashboard == null)
            return new List<int>();

        return await _dashboardService.GetDashboardAccountIds(personalDashboard.Id, userId);
    }

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
            // La contrepartie et son IBAN sont cherchables : sur un virement, le libellé arrive souvent
            // vide et c'est le bénéficiaire qui identifie la ligne (crèche communale, école, assurance).
            var searchIban = GoCardlessTransactionFields.Normalize(search);
            query = query.Where(t => t.Description.Contains(search)
                                  || t.Category.Name.Contains(search)
                                  || t.Account.Name.Contains(search)
                                  || (t.CounterpartyName != null && t.CounterpartyName.Contains(search))
                                  || (t.CounterpartyIban != null && t.CounterpartyIban.Contains(searchIban)));
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
        transaction.CategoryId = dto.CategoryId;
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

    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? bankAccountId,
        [FromQuery] bool includeExceptional = true)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any())
        {
            return Ok(new TransactionSummaryDto
            {
                TotalIncome = 0,
                TotalExpenses = 0,
                Balance = 0,
                CategoryBreakdown = new(),
                MonthlyBalance = new()
            });
        }

        // SQLite ne supporte pas Sum(decimal) en SQL → tout en client
        var baseQuery = _context.Transactions.Where(t => accountIds.Contains(t.AccountId));
        if (from.HasValue) baseQuery = baseQuery.Where(t => t.Date >= from.Value);
        if (to.HasValue) baseQuery = baseQuery.Where(t => t.Date <= to.Value);
        if (bankAccountId.HasValue) baseQuery = baseQuery.Where(t => t.BankAccountId == bankAccountId.Value);

        var rawAll = await baseQuery
            .Select(t => new
            {
                t.Type,
                t.Amount,
                CategoryId = t.Category.Id,
                CategoryName = t.Category.Name,
                CategoryIcon = t.Category.Icon,
                CategoryColor = t.Category.Color,
                IsTransfer = t.Category.IsTransfer,
                t.IsExceptional,
                t.IsRefund,
            })
            .ToListAsync();

        // Dépenses exceptionnelles (non-transfert) de la période — toujours calculée, sert au libellé "dont X € exceptionnels"
        var exceptionalExpenses = rawAll.Where(t => t.Type == TransactionType.Expense && !t.IsTransfer && t.IsExceptional).Sum(t => t.Amount);

        // Filtre flux : quand includeExceptional == false, on retire les transactions exceptionnelles des agrégats de flux
        // (mais PAS de la courbe solde total plus bas — ces dépenses ont réellement eu lieu)
        var flux = includeExceptional ? rawAll : rawAll.Where(t => !t.IsExceptional).ToList();

        // Exclure les transferts internes (épargne, comptes joints) des stats dépenses/revenus.
        // Un remboursement (revenu marqué IsRefund) n'est pas une rentrée : il s'impute en négatif sur
        // les dépenses de sa catégorie, sinon les deux côtés du bilan gonflent du même montant (Refunds).
        var totalIncome = flux
            .Where(t => t.Type == TransactionType.Income && !t.IsTransfer && !Refunds.Applies(t.Type, t.IsRefund))
            .Sum(t => t.Amount);
        var totalExpenses = flux
            .Where(t => !t.IsTransfer && (t.Type == TransactionType.Expense || Refunds.Applies(t.Type, t.IsRefund)))
            .Sum(t => Refunds.Applies(t.Type, t.IsRefund) ? -t.Amount : t.Amount);
        // Mise de côté = catégories transfert (Épargne perso, etc.), nette des retraits (Income transfert en négatif)
        var totalSavings = flux.Where(t => t.IsTransfer)
            .Sum(t => t.Type == TransactionType.Expense ? t.Amount : -t.Amount);

        var expensesByCategory = flux
            .Where(t => !t.IsTransfer && (t.Type == TransactionType.Expense || Refunds.Applies(t.Type, t.IsRefund)))
            .GroupBy(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(t => Refunds.Applies(t.Type, t.IsRefund) ? -t.Amount : t.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = totalExpenses > 0 ? Math.Round(amount / totalExpenses * 100, 1) : 0,
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var incomeByCategory = flux
            .Where(t => t.Type == TransactionType.Income && !t.IsTransfer && !Refunds.Applies(t.Type, t.IsRefund))
            .GroupBy(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(t => t.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = totalIncome > 0 ? Math.Round(amount / totalIncome * 100, 1) : 0,
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var savingsByCategory = flux
            .Where(t => t.IsTransfer)
            .GroupBy(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(t => t.Type == TransactionType.Expense ? t.Amount : -t.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = totalSavings > 0 ? Math.Round(amount / totalSavings * 100, 1) : 0,
                };
            })
            .Where(c => c.Amount != 0)
            .OrderByDescending(c => c.Amount)
            .ToList();

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-5);
        var startOfMonth = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        // SQLite : agrégation client (exclut transferts internes)
        var rawMonthly = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOfMonth)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Type, t.Amount, t.Category.IsTransfer, t.IsExceptional, t.IsRefund })
            .ToListAsync();

        // Barres Income/Expenses : mêmes exclusions que les KPI (transferts + exceptionnelles si includeExceptional == false)
        var monthlyData = rawMonthly
            .Where(t => !t.IsTransfer && (includeExceptional || !t.IsExceptional))
            .GroupBy(t => new { t.Year, t.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(t => t.Type == TransactionType.Income && !Refunds.Applies(t.Type, t.IsRefund)).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                          - g.Where(t => Refunds.Applies(t.Type, t.IsRefund)).Sum(t => t.Amount),
            })
            .ToList();

        // Solde total actuel du dashboard, ancré sur le solde booké (hors pending) plutôt que RealBalance/interimAvailable —
        // cohérence avec le net mensuel qui n'agrège que des transactions booked (sinon le pending décale tous les points passés).
        var currentTotalBalance = await GetTotalBookedBalanceAsync(dashboardId);

        // Net mensuel sur tous les comptes du dashboard (ignore le filtre bankAccountId — le solde reste consolidé)
        // Les IsTransfer s'annulent (transferts internes) ou n'existent pas (alimente un manuel, dont l'effet est dans currentTotal)
        // Exclut les provisions : l'ancre (solde banque booké) ne contient pas cet argent, l'inclure ici
        // décalerait tous les points passés de la courbe du montant provisionné jusqu'au versement réel
        var rawForBalance = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOfMonth && !t.IsProvisional)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Type, t.Amount, IsTransfer = t.Category.IsTransfer })
            .ToListAsync();
        var monthlyNet = rawForBalance
            .Where(t => !t.IsTransfer)
            .GroupBy(t => new { t.Year, t.Month })
            .ToDictionary(
                g => (g.Key.Year, g.Key.Month),
                g => g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                   - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount));

        var monthlyBalance = Enumerable.Range(0, 6)
            .Select(i => startOfMonth.AddMonths(i))
            .Select(month =>
            {
                var data = monthlyData.FirstOrDefault(d => d.Year == month.Year && d.Month == month.Month);
                // Solde fin-de-mois = solde actuel - net cumulé des mois strictement postérieurs
                var netAfter = monthlyNet
                    .Where(kv => kv.Key.Year > month.Year || (kv.Key.Year == month.Year && kv.Key.Month > month.Month))
                    .Sum(kv => kv.Value);
                return new MonthlyBalanceDto
                {
                    Month = month.ToString("MMM yyyy"),
                    Income = data?.Income ?? 0,
                    Expenses = data?.Expenses ?? 0,
                    Balance = (data?.Income ?? 0) - (data?.Expenses ?? 0),
                    TotalBalance = currentTotalBalance - netAfter
                };
            })
            .ToList();

        return Ok(new TransactionSummaryDto
        {
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Balance = totalIncome - totalExpenses,
            TotalSavings = totalSavings,
            ExceptionalExpenses = exceptionalExpenses,
            CategoryBreakdown = expensesByCategory,
            IncomeBreakdown = incomeByCategory,
            SavingsBreakdown = savingsByCategory,
            MonthlyBalance = monthlyBalance
        });
    }

    /// <summary>
    /// Bilan mensuel en blocs (modèle Excel d'Audrey) : ENTRÉES − FIXE − MISES DE CÔTÉ − VARIABLE = TOTAL.
    /// Agrégation client (pattern SQLite de GetSummary — SQLite ne sait pas Sum(decimal) en SQL).
    /// </summary>
    [HttpGet("monthly-report")]
    public async Task<ActionResult<MonthlyReportDto>> GetMonthlyReport(
        [FromQuery] int? dashboardId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (month < 1 || month > 12) return BadRequest("Mois invalide (1-12).");

        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any())
            return Ok(new MonthlyReportDto { Year = year, Month = month });

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var raw = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= start && t.Date < end)
            .Select(t => new
            {
                t.Type,
                t.Amount,
                t.IsExceptional,
                CategoryId = t.Category.Id,
                CategoryName = t.Category.Name,
                CategoryIcon = t.Category.Icon,
                CategoryColor = t.Category.Color,
                IsTransfer = t.Category.IsTransfer,
                ExcludeFromMonthlyReport = t.Category.ExcludeFromMonthlyReport,
                t.IsFixed,
                t.IsRefund,
            })
            .ToListAsync();

        // Hors bilan : les mouvements dont le montant du mois ne se décide pas dans le mois, et dont
        // la contrepartie est déjà comptée ailleurs. Le balayage du compte joint vers le livret emporte
        // le reliquat du mois précédent, donc le soustraire ici compte le même euro en positif en M−1
        // puis en négatif en M. Les virements entre deux comptes suivis gonflent, eux, les deux côtés
        // du bilan à la fois. Ces lignes sont rendues à part, en information, jamais soustraites.
        // Diagnostic complet : projects/app-finance/double-comptage-2026-08-27.md dans le repo Yen.
        var horsBilanItems = raw
            .Where(t => t.ExcludeFromMonthlyReport)
            .Select(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, Amount = t.Type == TransactionType.Expense ? t.Amount : -t.Amount })
            .ToList();

        // Tout le reste alimente les quatre blocs du modèle d'Audrey.
        var retenu = raw.Where(t => !t.ExcludeFromMonthlyReport).ToList();

        // Bloc ENTRÉES : revenus non-transfert, hors régularisations fixes (déduites du bloc FIXE) et
        // hors remboursements (déduits du bloc de leur catégorie — voir Refunds)
        var entreesItems = retenu
            .Where(t => t.Type == TransactionType.Income && !t.IsTransfer && !t.IsFixed && !Refunds.Applies(t.Type, t.IsRefund))
            .ToList();
        // Bloc FIXE : dépenses non-transfert marquées charge fixe, nettes des régularisations
        // (revenus fixes : remboursement énergie…) qui comptent en négatif
        var fixeItems = retenu
            .Where(t => !t.IsTransfer && t.IsFixed)
            .Select(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, Amount = t.Type == TransactionType.Expense ? t.Amount : -t.Amount })
            .ToList();
        // Bloc MISES DE CÔTÉ : catégories transfert gardées au bilan (achat de titres, ordre permanent
        // décidé dans le mois), nettes des retraits (un retrait = Income transfert, compté en négatif)
        var misesItems = retenu
            .Where(t => t.IsTransfer)
            .Select(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, Amount = t.Type == TransactionType.Expense ? t.Amount : -t.Amount })
            .ToList();
        // Bloc VARIABLE : dépenses non-transfert non-fixes (le reste), nettes des remboursements reçus
        // sur ces mêmes catégories (une avance rendue annule la dépense, elle n'est pas une rentrée)
        var variableItems = retenu
            .Where(t => !t.IsTransfer && !t.IsFixed && (t.Type == TransactionType.Expense || Refunds.Applies(t.Type, t.IsRefund)))
            .Select(t => new
            {
                t.CategoryId,
                t.CategoryName,
                t.CategoryIcon,
                t.CategoryColor,
                t.IsExceptional,
                Amount = t.Type == TransactionType.Expense ? t.Amount : -t.Amount,
            })
            .ToList();

        var entrees = entreesItems.Sum(t => t.Amount);
        var fixe = fixeItems.Sum(t => t.Amount);
        var mises = misesItems.Sum(t => t.Amount);
        var variable = variableItems.Sum(t => t.Amount);
        var variableExceptionnel = variableItems.Where(t => t.IsExceptional).Sum(t => t.Amount);
        var horsBilan = horsBilanItems.Sum(t => t.Amount);

        static List<CategoryBreakdownDto> Breakdown(IEnumerable<(int Id, string Name, string Icon, string Color, decimal Amount)> items) =>
            items
                .GroupBy(x => new { x.Id, x.Name, x.Icon, x.Color })
                .Select(g => new CategoryBreakdownDto
                {
                    CategoryId = g.Key.Id,
                    CategoryName = g.Key.Name,
                    CategoryIcon = g.Key.Icon,
                    CategoryColor = g.Key.Color,
                    Amount = g.Sum(x => x.Amount),
                    Percentage = 0,
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

        return Ok(new MonthlyReportDto
        {
            Year = year,
            Month = month,
            Entrees = entrees,
            Fixe = fixe,
            MisesDeCote = mises,
            Variable = variable,
            VariableExceptionnel = variableExceptionnel,
            HorsBilan = horsBilan,
            Total = entrees - fixe - mises - variable,
            EntreesByCategory = Breakdown(entreesItems.Select(t => (t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, t.Amount))),
            FixeByCategory = Breakdown(fixeItems.Select(t => (t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, t.Amount)))
                .Where(c => c.Amount != 0).ToList(),
            MisesDeCoteByCategory = Breakdown(misesItems.Select(t => (t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, t.Amount)))
                .Where(c => c.Amount != 0).ToList(),
            VariableByCategory = Breakdown(variableItems.Select(t => (t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, t.Amount))),
            HorsBilanByCategory = Breakdown(horsBilanItems.Select(t => (t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor, t.Amount)))
                .Where(c => c.Amount != 0).ToList(),
        });
    }

    /// <summary>
    /// Historique mensuel des dépenses d'une catégorie sur les N derniers mois calendaires (mois courant inclus,
    /// mois vides à 0). Sépare courant / exceptionnel pour barres empilées côté front. Exclut les transferts internes.
    /// </summary>
    [HttpGet("category-history")]
    public async Task<ActionResult<List<CategoryMonthHistoryDto>>> GetCategoryHistory(
        [FromQuery] int? dashboardId,
        [FromQuery] int categoryId,
        [FromQuery] int months = 12)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<CategoryMonthHistoryDto>());

        months = Math.Clamp(months, 1, 36);

        var now = DateTime.UtcNow;
        var startMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));

        // SQLite ne supporte pas Sum(decimal) en SQL → agrégation client (même pattern que GetSummary)
        var raw = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.CategoryId == categoryId
                     && t.Type == TransactionType.Expense
                     && !t.Category.IsTransfer
                     && t.Date >= startMonth)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Amount, t.IsExceptional })
            .ToListAsync();

        var byMonth = raw
            .GroupBy(t => new { t.Year, t.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.ToList());

        var culture = new System.Globalization.CultureInfo("fr-FR");
        var result = Enumerable.Range(0, months)
            .Select(i => startMonth.AddMonths(i))
            .Select(month =>
            {
                var txns = byMonth.GetValueOrDefault((month.Year, month.Month)) ?? new();
                var total = txns.Sum(t => t.Amount);
                var exceptional = txns.Where(t => t.IsExceptional).Sum(t => t.Amount);
                return new CategoryMonthHistoryDto
                {
                    Month = month.ToString("yyyy-MM"),
                    Label = month.ToString("MMM yyyy", culture),
                    Total = total,
                    CurrentTotal = total - exceptional,
                    ExceptionalTotal = exceptional,
                };
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Burn-down du « reste du mois » : courbe jour par jour du reste (entrées − dépenses non-transfert)
    /// + projection de fin de mois. Agrégation client (pattern SQLite de GetSummary).
    /// Remplace la note manuelle d'Audrey (ce qui reste pour finir le mois).
    /// </summary>
    [HttpGet("burndown")]
    public async Task<ActionResult<BurndownDto>> GetBurndown(
        [FromQuery] int? dashboardId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        if (month < 1 || month > 12) return BadRequest("Mois invalide (1-12).");

        var lastDay = DateTime.DaysInMonth(year, month);
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any())
            return Ok(new BurndownDto { Year = year, Month = month, RecurringIncluded = true });

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var now = DateTime.UtcNow;

        var isPast = end <= now;
        var isCurrent = start <= now && now < end;
        var isFuture = start > now;

        // Jour de référence « aujourd'hui » dans ce mois : courant → now.Day, passé → dernier jour, futur → 0
        var todayDay = isCurrent ? now.Day : (isPast ? lastDay : 0);

        // Transactions du mois, hors transferts internes (mêmes filtres que GetSummary)
        var raw = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= start && t.Date < end && !t.Category.IsTransfer)
            .Select(t => new { t.Date, t.Type, t.Amount })
            .ToListAsync();

        var spentDelta = new decimal[lastDay + 1];
        var incomeDelta = new decimal[lastDay + 1];
        foreach (var t in raw)
        {
            var d = t.Date.Day;
            if (d < 1 || d > lastDay) continue;
            if (t.Type == TransactionType.Expense) spentDelta[d] += t.Amount;
            else if (t.Type == TransactionType.Income) incomeDelta[d] += t.Amount;
        }

        var days = new List<BurndownDayDto>(lastDay);
        decimal cumSpent = 0, cumIncome = 0, remainingToday = 0;
        for (var d = 1; d <= lastDay; d++)
        {
            cumSpent += spentDelta[d];
            cumIncome += incomeDelta[d];
            var isFutureDay = isFuture || (isCurrent && d > todayDay);
            days.Add(new BurndownDayDto
            {
                Day = d,
                Date = start.AddDays(d - 1).ToString("yyyy-MM-dd"),
                Spent = isFutureDay ? null : cumSpent,
                Income = isFutureDay ? null : cumIncome,
                Remaining = isFutureDay ? null : cumIncome - cumSpent,
            });
            if (!isFutureDay) remainingToday = cumIncome - cumSpent; // dernière valeur non-future = aujourd'hui / fin de mois passé
        }

        var daysRemaining = isCurrent ? lastDay - todayDay : (isFuture ? lastDay : 0);

        // Rythme variable : moyenne journalière des dépenses variables (non-transfert, non-fixes) sur 14 jours glissants
        var paceStart = now.Date.AddDays(-13);
        var paceEnd = now.Date.AddDays(1);
        var paceRaw = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.Type == TransactionType.Expense
                     && !t.Category.IsTransfer
                     && !t.IsFixed
                     && t.Date >= paceStart && t.Date < paceEnd)
            .Select(t => t.Amount)
            .ToListAsync();
        var dailyPaceVariable = paceRaw.Sum() / 14m;

        // Récurrentes restant à tomber ce mois-ci. Modèle exploitable (Frequency + DayOfMonth/StartDate) → toujours calculable.
        decimal upcomingExpenses = 0, upcomingIncome = 0;
        var recurringIncluded = true;
        if (!isPast)
        {
            var effectiveDashboardId = dashboardId;
            if (effectiveDashboardId == null)
            {
                var userId = GetUserId();
                var personal = await _context.Dashboards
                    .Where(d => d.CreatorId == userId)
                    .OrderBy(d => d.CreatedAt)
                    .FirstOrDefaultAsync();
                effectiveDashboardId = personal?.Id;
            }

            if (effectiveDashboardId.HasValue)
            {
                // Include Category pour exclure les transferts internes, cohérent avec la courbe (qui filtre !IsTransfer)
                var recs = await _context.RecurringTransactions
                    .Include(r => r.Category)
                    .Where(r => r.DashboardId == effectiveDashboardId.Value && r.IsActive
                             && (r.Category == null || !r.Category.IsTransfer))
                    .ToListAsync();

                // Récurrentes provisionnées : jamais en « à venir ». Leur montant est déjà dans le
                // cumul, soit via la provision du jour 1, soit via le versement réel réconcilié
                // (qui peut tomber avant DayOfMonth — le compter ici le doublerait).
                // On garde aussi l'exclusion par provision existante pour couvrir une récurrente
                // dont le flag aurait été désactivé en cours de mois.
                var provisionedRecurringIds = await _context.Transactions
                    .Where(t => t.IsProvisional && t.RecurringTransactionId != null
                             && t.Date >= start && t.Date < end)
                    .Select(t => t.RecurringTransactionId!.Value)
                    .ToListAsync();

                var fromDay = isCurrent ? todayDay + 1 : 1; // futur : tout le mois est « à venir »
                foreach (var r in recs)
                {
                    if (r.ProvisionAtMonthStart || provisionedRecurringIds.Contains(r.Id)) continue;
                    foreach (var _ in RecurringOccurrenceDays(r, year, month, fromDay, lastDay))
                    {
                        if (r.Type == "Expense") upcomingExpenses += r.Amount;
                        else if (r.Type == "Income") upcomingIncome += r.Amount;
                    }
                }
            }
        }

        var projected = isPast
            ? remainingToday // mois passé : la « projection » est la valeur finale réelle
            : remainingToday - dailyPaceVariable * daysRemaining - upcomingExpenses + upcomingIncome;

        return Ok(new BurndownDto
        {
            Year = year,
            Month = month,
            Days = days,
            RemainingToday = remainingToday,
            DailyPaceVariable = dailyPaceVariable,
            UpcomingRecurringExpenses = upcomingExpenses,
            UpcomingRecurringIncome = upcomingIncome,
            RecurringIncluded = recurringIncluded,
            ProjectedEndOfMonth = projected,
            DaysRemaining = daysRemaining,
            IsPast = isPast,
            TodayDay = isCurrent ? todayDay : (int?)null,
        });
    }

    /// <summary>
    /// Jours du mois (year, month) dans [fromDay, toDay] où une récurrente tombe, en respectant Start/End et la fréquence.
    /// Monthly : DayOfMonth (clampé au dernier jour). Yearly : anniversaire StartDate. Weekly : cadence 7j depuis StartDate.
    /// </summary>
    private static IEnumerable<int> RecurringOccurrenceDays(RecurringTransaction r, int year, int month, int fromDay, int toDay)
    {
        if (fromDay > toDay) yield break;
        var lastDay = DateTime.DaysInMonth(year, month);
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, lastDay);
        if (r.StartDate > monthEnd) yield break;
        if (r.EndDate.HasValue && r.EndDate.Value < monthStart) yield break;

        switch (r.Frequency)
        {
            case "Monthly":
            {
                var day = Math.Min(r.DayOfMonth ?? r.StartDate.Day, lastDay);
                var occ = new DateOnly(year, month, day);
                if (day >= fromDay && day <= toDay && occ >= r.StartDate
                    && (!r.EndDate.HasValue || occ <= r.EndDate.Value))
                    yield return day;
                break;
            }
            case "Yearly":
            {
                if (r.StartDate.Month != month) break;
                var day = Math.Min(r.StartDate.Day, lastDay);
                var occ = new DateOnly(year, month, day);
                if (day >= fromDay && day <= toDay && occ >= r.StartDate
                    && (!r.EndDate.HasValue || occ <= r.EndDate.Value))
                    yield return day;
                break;
            }
            case "Weekly":
            {
                for (var day = fromDay; day <= toDay; day++)
                {
                    var occ = new DateOnly(year, month, day);
                    if (occ < r.StartDate) continue;
                    if (r.EndDate.HasValue && occ > r.EndDate.Value) continue;
                    if ((occ.DayNumber - r.StartDate.DayNumber) % 7 == 0)
                        yield return day;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Les comptes bancaires actifs de l'utilisateur (connectés ou manuels) qui relèvent du périmètre du
    /// dashboard. Les agrégats (bilan, courbes) filtrent déjà par compte logique. Les soldes, eux, sont
    /// portés par le compte bancaire, qui n'appartient à aucun dashboard : avant le 28/08/2026 le widget
    /// Soldes du Commun comptait l'Argenta perso et divergeait du bilan d'exactement ce montant.
    /// Règle : un dashboard qui ne contient que le compte logique Perso ne voit que les comptes bancaires
    /// marqués perso, un dashboard sans compte Perso ne voit que les autres, un dashboard mixte voit tout.
    /// </summary>
    private async Task<List<BankAccount>> GetBankAccountsInScopeAsync(List<int> accountIds)
    {
        var userId = GetUserId();

        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive && (
                (ba.BankConnection != null && ba.BankConnection.UserId == userId)
                || (ba.IsManual && ba.UserId == userId)
            ))
            .ToListAsync();

        var scopes = await _context.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => a.IsPersonalScope)
            .Distinct()
            .ToListAsync();
        var hasPerso = scopes.Contains(true);
        var hasCommon = scopes.Contains(false);

        if (hasPerso && !hasCommon) return bankAccounts.Where(ba => ba.IsPersonal).ToList();
        if (hasCommon && !hasPerso) return bankAccounts.Where(ba => !ba.IsPersonal).ToList();
        return bankAccounts;
    }

    /// <summary>
    /// Total des soldes du dashboard, ancré sur le solde booké (hors pending) pour les comptes GoCardless.
    /// Réplique la résolution manuel/GoCardless de <see cref="GetAccountBalances"/> (branche non-historique),
    /// mais préfère BookedBalance à RealBalance — RealBalance (interimAvailable) reste utilisé partout ailleurs
    /// (KPI, liste de comptes) et n'est pas affecté par ce helper.
    /// </summary>
    private async Task<decimal> GetTotalBookedBalanceAsync(int? dashboardId)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return 0m;

        var bankAccounts = await GetBankAccountsInScopeAsync(accountIds);

        if (!bankAccounts.Any())
        {
            // Fallback : pas de banque connectée → solde calculé par Account interne (identique à GetAccountBalances)
            var rawTxns = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId))
                .Select(t => new { t.Type, t.Amount })
                .ToListAsync();
            return rawTxns.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                 - rawTxns.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        }

        var bankAccountIds = bankAccounts.Where(ba => !ba.IsManual).Select(ba => ba.Id).ToList();
        var rawByBank = await _context.Transactions
            .Where(t => t.BankAccountId != null && bankAccountIds.Contains(t.BankAccountId.Value))
            .Select(t => new { BankAccountId = t.BankAccountId!.Value, t.Type, t.Amount })
            .ToListAsync();

        var byBank = rawByBank
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                   - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount));

        var manualAccounts = bankAccounts.Where(ba => ba.IsManual).ToList();
        var manualTransfers = new Dictionary<int, decimal>();
        foreach (var m in manualAccounts)
        {
            if (m.SourceBankAccountId == null || m.IncrementCategoryId == null) continue;
            // Expense sur le compte source = versement vers le manuel, Income = retrait (miroir exact)
            var transfers = await _context.Transactions
                .Where(t => t.BankAccountId == m.SourceBankAccountId
                         && t.CategoryId == m.IncrementCategoryId
                         && (m.InitialBalanceDate == null || t.Date >= m.InitialBalanceDate))
                .Select(t => new { t.Type, t.Amount })
                .ToListAsync();
            manualTransfers[m.Id] = transfers.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                                  - transfers.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        }

        var total = 0m;
        foreach (var ba in bankAccounts)
        {
            if (ba.IsManual)
            {
                // Comptes manuels : résolution inchangée (pas de notion de booké/pending côté banque)
                total += (ba.InitialBalance ?? 0) + manualTransfers.GetValueOrDefault(ba.Id, 0);
            }
            else
            {
                var netFallback = byBank.GetValueOrDefault(ba.Id, 0);
                total += ba.BookedBalance ?? ba.RealBalance ?? netFallback;
            }
        }

        return total;
    }

    /// <summary>Transactions encore catégorisées en "Autres" (CategoryId == 10).</summary>
    [HttpGet("uncategorized")]
    public async Task<ActionResult<List<TransactionDto>>> GetUncategorized(
        [FromQuery] int? dashboardId,
        [FromQuery] int? bankAccountId,
        [FromQuery] int limit = 50)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        const int othersCategoryId = 10;
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
    /// Soldes par compte bancaire physique (un par BankAccount). Préfère le solde réel banque.
    /// Si <paramref name="to"/> est passé et antérieur à maintenant, calcule le solde rétrospectif à cette date.
    /// </summary>
    [HttpGet("account-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> GetAccountBalances(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? to)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<AccountBalanceDto>());

        var now = DateTime.UtcNow;
        var asOf = to;
        // Si la borne dépasse maintenant (ex: période "Cette année" jusqu'à 31 déc), on plafonne à now
        if (asOf.HasValue && asOf.Value > now) asOf = null;
        var historical = asOf.HasValue;

        var bankAccounts = await GetBankAccountsInScopeAsync(accountIds);

        if (!bankAccounts.Any())
        {
            // Fallback : pas de banque connectée → un solde calculé par Account interne
            var fallback = await _context.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .ToListAsync();
            var rawTxns = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId)
                         && (!historical || t.Date <= asOf!.Value))
                .Select(t => new { t.AccountId, t.Type, t.Amount, t.Date })
                .ToListAsync();
            return Ok(fallback.Select(a =>
            {
                var g = rawTxns.Where(t => t.AccountId == a.Id).ToList();
                return new AccountBalanceDto
                {
                    AccountId = a.Id,
                    AccountName = a.Name,
                    Balance = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                            - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                    IsRealBalance = false,
                    LastTransactionDate = g.Any() ? g.Max(t => t.Date) : (DateTime?)null,
                };
            }).ToList());
        }

        // Stats par BankAccountId (pour les comptes connectés : Σ income/expense, et stats post-asOf pour rétrospective)
        var bankAccountIds = bankAccounts.Where(ba => !ba.IsManual).Select(ba => ba.Id).ToList();
        var rawByBank = await _context.Transactions
            .Where(t => t.BankAccountId != null && bankAccountIds.Contains(t.BankAccountId.Value))
            .Select(t => new { BankAccountId = t.BankAccountId!.Value, t.Type, t.Amount, t.Date })
            .ToListAsync();

        var byBank = rawByBank
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(g => g.Key, g => new
            {
                Income = g.Where(t => t.Type == TransactionType.Income && (!historical || t.Date <= asOf!.Value)).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense && (!historical || t.Date <= asOf!.Value)).Sum(t => t.Amount),
                IncomeAfter = historical ? g.Where(t => t.Type == TransactionType.Income && t.Date > asOf!.Value).Sum(t => t.Amount) : 0m,
                ExpensesAfter = historical ? g.Where(t => t.Type == TransactionType.Expense && t.Date > asOf!.Value).Sum(t => t.Amount) : 0m,
                LastDate = g.Where(t => !historical || t.Date <= asOf!.Value).Select(t => (DateTime?)t.Date).DefaultIfEmpty(null).Max(),
            });

        // Pour les comptes manuels : charger les transferts entrants (transactions cat=IncrementCategory, ba=SourceBankAccount, expense, date>=InitialDate)
        // Si asOf est passé, on borne à asOf — solde rétrospectif au passage de la borne.
        var manualAccounts = bankAccounts.Where(ba => ba.IsManual).ToList();
        var manualTransfers = new Dictionary<int, (decimal Sum, DateTime? LastDate)>();
        foreach (var m in manualAccounts)
        {
            if (m.SourceBankAccountId == null || m.IncrementCategoryId == null) continue;
            // Expense sur le compte source = versement vers le manuel, Income = retrait (miroir exact)
            var transfers = await _context.Transactions
                .Where(t => t.BankAccountId == m.SourceBankAccountId
                         && t.CategoryId == m.IncrementCategoryId
                         && (m.InitialBalanceDate == null || t.Date >= m.InitialBalanceDate)
                         && (!historical || t.Date <= asOf!.Value))
                .Select(t => new { t.Type, t.Amount, t.Date })
                .ToListAsync();
            manualTransfers[m.Id] = (
                transfers.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                    - transfers.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                transfers.Any() ? transfers.Max(t => t.Date) : (DateTime?)null
            );
        }

        var result = bankAccounts.Select(ba =>
        {
            decimal balance;
            DateTime? lastDate;
            bool isReal;

            if (ba.IsManual)
            {
                var (sum, last) = manualTransfers.GetValueOrDefault(ba.Id, (0, null));
                balance = (ba.InitialBalance ?? 0) + sum;
                lastDate = last;
                isReal = false; // calculé, pas un solde banque
            }
            else
            {
                var stats = byBank.GetValueOrDefault(ba.Id);
                if (historical && ba.RealBalance.HasValue && stats != null)
                {
                    // Solde rétrospectif : RealBalance d'aujourd'hui - net des transactions postérieures à asOf
                    balance = ba.RealBalance.Value - stats.IncomeAfter + stats.ExpensesAfter;
                    isReal = false;
                }
                else
                {
                    balance = ba.RealBalance ?? (stats != null ? stats.Income - stats.Expenses : 0);
                    isReal = !historical && ba.RealBalance.HasValue;
                }
                lastDate = stats?.LastDate;
            }

            return new AccountBalanceDto
            {
                AccountId = ba.Id,
                AccountName = !string.IsNullOrWhiteSpace(ba.AccountName) ? ba.AccountName : ba.Iban,
                BankInstitutionName = ba.BankConnection?.InstitutionName ?? (ba.IsManual ? "Manuel" : null),
                Balance = balance,
                IsRealBalance = isReal,
                IsManual = ba.IsManual,
                LastTransactionDate = lastDate,
                BalanceUpdatedAt = ba.BalanceUpdatedAt ?? ba.InitialBalanceDate,
            };
        })
        .OrderByDescending(b => b.Balance)
        .ToList();

        return Ok(result);
    }

    /// <summary>Force la récupération des soldes réels via GoCardless (sans attendre le sync 6h).</summary>
    [HttpPost("refresh-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> RefreshBalances(
        [FromQuery] int? dashboardId,
        [FromServices] GoCardlessClient goCardless)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<AccountBalanceDto>());

        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive)
            .ToListAsync();

        foreach (var ba in bankAccounts)
        {
            try
            {
                var data = await goCardless.GetBalancesAsync(ba.ExternalAccountId);
                if (data.TryGetProperty("balances", out var arr))
                {
                    foreach (var b in arr.EnumerateArray())
                    {
                        if (b.TryGetProperty("balanceAmount", out var amt) && amt.TryGetProperty("amount", out var v))
                        {
                            if (decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var bal))
                            {
                                ba.RealBalance = bal;
                                ba.BalanceUpdatedAt = DateTime.UtcNow;
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* silencieux : on garde le fallback */ }
        }
        await _context.SaveChangesAsync();

        return await GetAccountBalances(dashboardId, null);
    }
}

