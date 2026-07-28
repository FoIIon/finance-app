# Lot 1 du suivi des investissements, plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permettre la saisie manuelle de lignes d'investissement (titres, métal, contrat d'assurance-vie) avec valorisations datées, et afficher PRU, plus-value et rendement selon des règles qui refusent tout chiffre non fondé.

**Architecture:** Deux entités EF Core rattachées au `Dashboard` (`Investment`, `InvestmentValuation`), un calculateur pur sans `DbContext` couvert par des tests unitaires xUnit, un controller CRUD suivant le pattern d'autorisation existant, et une page React de saisie. Aucun écran ni calcul existant n'est modifié.

**Tech Stack:** ASP.NET Core 8, EF Core 8 sur SQLite, xUnit, React 19, TypeScript, TanStack Query, Tailwind 4.

**Spec:** `docs/superpowers/specs/2026-07-28-investissements-design.md`

## Global Constraints

- Cible `net8.0`. EF Core `8.0.*`, provider SQLite.
- Précision décimale explicite obligatoire : `HasPrecision(18, 2)` pour tout montant en euros, `HasPrecision(18, 6)` pour toute quantité. Sans cela la perte est silencieuse.
- **SQLite ne sait pas exécuter `Sum(decimal)` en SQL.** Toute agrégation de décimaux se fait côté client après `ToListAsync()`, comme dans `ProjectEnvelopeController.GetAll`.
- Autorisation obligatoire sur chaque endpoint : `GetUserId()` depuis les claims JWT, puis `UserCanAccessDashboard(dashboardId, userId)` avant toute lecture ou écriture. Aucune entité n'est atteignable autrement que par son `DashboardId`.
- **Interdit de modifier** le Bilan, le Solde total, le burn-down, les courbes existantes ou tout fichier sous `pages/dashboard/`.
- **Aucun rendement annualisé ne s'affiche sans date d'entrée renseignée.** Règle non négociable, voir Task 3.
- Les valorisations ne s'écrasent jamais, elles s'empilent. `AsOf` porte la date de la valeur, jamais la date de saisie.
- Libellés d'interface en français, sans point-virgule ni tiret cadratin dans les textes affichés.

## Périmètre des trois entités

Les trois entités de la spec sont créées ici, `InvestmentMovement` comprise, pour n'avoir qu'une seule migration à appliquer en production. Décision de Sébastien du 28/07/2026.

La table existe, mais **rien ne l'alimente au lot 1** : aucun endpoint d'écriture de mouvement, aucun champ de saisie. Elle se remplira à l'intégration Trade Republic du lot 4.

Conséquence à ne pas perdre de vue : le TRI (XIRR) n'est **pas** implémenté dans ce lot, seul le CAGR l'est. Une table vide ne produit pas de rendement. La règle d'affichage de la spec reste celle qui gouverne, et elle réserve le TRI aux lignes disposant réellement d'un historique de mouvements.

## Structure des fichiers

**Backend, créés :**
- `backend/FinanceApp.Tests/FinanceApp.Tests.csproj` : projet de tests unitaires
- `backend/FinanceApp.Tests/InvestmentCalculatorTests.cs` : tests du calculateur
- `backend/FinanceApp.API/Models/InvestmentEnums.cs` : `InvestmentKind`, `InvestmentUnit`, `InvestmentSource`, `ValuationSource`
- `backend/FinanceApp.API/Models/Investment.cs` : la ligne détenue
- `backend/FinanceApp.API/Models/InvestmentValuation.cs` : la valeur datée
- `backend/FinanceApp.API/Models/InvestmentMovement.cs` : le mouvement, table créée mais non alimentée au lot 1
- `backend/FinanceApp.API/Services/InvestmentCalculator.cs` : calculateur pur, aucune dépendance
- `backend/FinanceApp.API/DTOs/InvestmentDtos.cs` : DTOs entrée et sortie
- `backend/FinanceApp.API/Controllers/InvestmentController.cs` : CRUD et valorisations

**Backend, modifiés :**
- `backend/FinanceApp.API.sln` : ajout du projet de tests
- `backend/FinanceApp.API/Data/AppDbContext.cs` : DbSets et configuration

**Frontend, créés :**
- `frontend/src/types/investment.ts`
- `frontend/src/api/investments.ts`
- `frontend/src/pages/Investments.tsx`

**Frontend, modifiés :**
- `frontend/src/hooks/queries.ts` : hook de requête
- `frontend/src/App.tsx` : route `/investments`

**Tests E2E, modifiés :**
- `tests/e2e/finance-app.spec.ts` : le nouveau test s'ajoute à la série existante, voir Task 9

Le calculateur est isolé du `DbContext` à dessein : c'est la seule façon de tester les règles de rendement sans base de données, et ce sont ces règles qui portent le risque d'erreur silencieuse.

---

### Task 1: Projet de tests unitaires et modèles

**Files:**
- Create: `backend/FinanceApp.Tests/FinanceApp.Tests.csproj` (via CLI)
- Create: `backend/FinanceApp.API/Models/InvestmentEnums.cs`
- Create: `backend/FinanceApp.API/Models/Investment.cs`
- Create: `backend/FinanceApp.API/Models/InvestmentValuation.cs`
- Modify: `backend/FinanceApp.API.sln`

**Interfaces:**
- Consumes: rien
- Produces: `InvestmentKind` (`Security`, `Metal`, `InsuranceContract`), `InvestmentUnit` (`Share`, `Gram`, `Ounce`, `Contract`), `InvestmentSource` (`Manual`, `TradeRepublic`), `ValuationSource` (`Manual`, `TradeRepublic`, `SpotApi`), classes `Investment` et `InvestmentValuation`

- [ ] **Step 1: Créer le projet de tests et le rattacher à la solution**

```bash
cd backend
dotnet new xunit -n FinanceApp.Tests -o FinanceApp.Tests
dotnet sln FinanceApp.API.sln add FinanceApp.Tests/FinanceApp.Tests.csproj
dotnet add FinanceApp.Tests/FinanceApp.Tests.csproj reference FinanceApp.API/FinanceApp.API.csproj
```

- [ ] **Step 2: Vérifier que le squelette compile et tourne**

Run: `cd backend && dotnet test`
Expected: succès, 0 test ou 1 test généré par le template selon la version du SDK. Si le template a créé `UnitTest1.cs`, le supprimer.

- [ ] **Step 3: Créer les enums**

Fichier `backend/FinanceApp.API/Models/InvestmentEnums.cs` :

```csharp
namespace FinanceApp.API.Models;

/// <summary>Nature de l'actif. Détermine le mode de valorisation.</summary>
public enum InvestmentKind
{
    Security = 0,
    Metal = 1,
    InsuranceContract = 2,
}

public enum InvestmentUnit
{
    Share = 0,
    Gram = 1,
    Ounce = 2,
    Contract = 3,
}

/// <summary>Qui écrit les données de la ligne. Distinct de InvestmentKind.</summary>
public enum InvestmentSource
{
    Manual = 0,
    TradeRepublic = 1,
}

public enum ValuationSource
{
    Manual = 0,
    TradeRepublic = 1,
    SpotApi = 2,
}
```

- [ ] **Step 4: Créer l'entité Investment**

Fichier `backend/FinanceApp.API/Models/Investment.cs` :

```csharp
namespace FinanceApp.API.Models;

/// <summary>
/// Une ligne détenue du patrimoine investi : un titre coté, une quantité de métal
/// ou un contrat d'assurance-vie. Rattachée au dashboard, comme SavingsGoal et ProjectEnvelope.
/// </summary>
public class Investment
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Titulaire, texte libre (ex. « Sébastien », « Audrey », « Commun »). Permet le total combiné.</summary>
    public string Holder { get; set; } = string.Empty;
    public InvestmentKind Kind { get; set; }
    public string? Isin { get; set; }
    /// <summary>Code métal, ex. XAU (or), XAG (argent).</summary>
    public string? MetalCode { get; set; }
    /// <summary>Vaut 1 par convention pour un contrat d'assurance-vie.</summary>
    public decimal Quantity { get; set; }
    public InvestmentUnit Unit { get; set; }
    /// <summary>Total réellement versé, en euros.</summary>
    public decimal CostBasis { get; set; }
    /// <summary>Sans cette date, aucun rendement annualisé n'est affiché.</summary>
    public DateTime? FirstPurchaseDate { get; set; }
    public InvestmentSource Source { get; set; } = InvestmentSource.Manual;
    /// <summary>Identifiant côté courtier, pour la réconciliation à l'import.</summary>
    public string? ExternalId { get; set; }
    public bool IsArchived { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public ICollection<InvestmentValuation> Valuations { get; set; } = new List<InvestmentValuation>();
}
```

- [ ] **Step 5: Créer l'entité InvestmentValuation**

Fichier `backend/FinanceApp.API/Models/InvestmentValuation.cs` :

```csharp
namespace FinanceApp.API.Models;

/// <summary>
/// Valeur d'une ligne à une date. On n'écrase jamais une valorisation, on en empile une nouvelle :
/// c'est ce qui produit la courbe du patrimoine et ce qui empêche une correction
/// de réécrire l'historique rétroactivement.
/// </summary>
public class InvestmentValuation
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    /// <summary>Date de la valeur (relevé, cours), jamais la date de saisie.</summary>
    public DateTime AsOf { get; set; }
    /// <summary>Cours unitaire quand il est connu. Null pour un relevé qui ne donne qu'un total.</summary>
    public decimal? UnitPrice { get; set; }
    /// <summary>Valeur totale de la ligne, en euros.</summary>
    public decimal MarketValue { get; set; }
    public ValuationSource Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Investment Investment { get; set; } = null!;
}
```

- [ ] **Step 6: Créer l'entité InvestmentMovement**

Table créée dès maintenant pour n'avoir qu'une seule migration à appliquer en production. Rien ne l'alimente au lot 1 : aucun endpoint, aucun champ de saisie. Elle se remplira à l'intégration Trade Republic.

Ajouter le type de mouvement dans `backend/FinanceApp.API/Models/InvestmentEnums.cs` :

```csharp
public enum MovementType
{
    Buy = 0,
    Sell = 1,
    Dividend = 2,
    Fee = 3,
}
```

Fichier `backend/FinanceApp.API/Models/InvestmentMovement.cs` :

```csharp
namespace FinanceApp.API.Models;

/// <summary>
/// Achat, vente, dividende ou frais sur une ligne. Alimentée par l'import Trade Republic.
/// Table créée au lot 1 pour n'avoir qu'une migration, mais non alimentée avant le lot 4 :
/// aucun endpoint d'écriture n'existe encore.
/// </summary>
public class InvestmentMovement
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    public MovementType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Montant total signé, en euros.</summary>
    public decimal Amount { get; set; }
    /// <summary>Identifiant côté courtier. Unique, pour la déduplication à l'import.</summary>
    public string? ExternalId { get; set; }
    public InvestmentSource Source { get; set; } = InvestmentSource.Manual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Investment Investment { get; set; } = null!;
}
```

Ajouter la collection dans `Investment` (fichier `Models/Investment.cs`), après `Valuations` :

```csharp
    public ICollection<InvestmentMovement> Movements { get; set; } = new List<InvestmentMovement>();
```

- [ ] **Step 7: Vérifier la compilation**

Run: `cd backend && dotnet build`
Expected: `Build succeeded`, 0 erreur.

- [ ] **Step 8: Commit**

```bash
git add backend/FinanceApp.Tests backend/FinanceApp.API.sln backend/FinanceApp.API/Models/Investment.cs backend/FinanceApp.API/Models/InvestmentValuation.cs backend/FinanceApp.API/Models/InvestmentMovement.cs backend/FinanceApp.API/Models/InvestmentEnums.cs
git commit -m "feat(investissements): entités Investment, InvestmentValuation et InvestmentMovement + projet de tests unitaires"
```

---

### Task 2: Calculateur, PRU et plus-value

**Files:**
- Create: `backend/FinanceApp.API/Services/InvestmentCalculator.cs`
- Test: `backend/FinanceApp.Tests/InvestmentCalculatorTests.cs`

**Interfaces:**
- Consumes: `InvestmentKind` de Task 1
- Produces: `InvestmentCalculator.ComputeUnitCost(InvestmentKind kind, decimal costBasis, decimal quantity) → decimal?`, `InvestmentCalculator.ComputeGain(decimal costBasis, decimal? marketValue) → (decimal? Amount, decimal? Percent)`

- [ ] **Step 1: Écrire les tests qui échouent**

Fichier `backend/FinanceApp.Tests/InvestmentCalculatorTests.cs` :

```csharp
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class InvestmentCalculatorTests
{
    [Fact]
    public void ComputeUnitCost_Security_DivisesCostBasisByQuantity()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 1000m, 8m);
        Assert.Equal(125m, result);
    }

    [Fact]
    public void ComputeUnitCost_InsuranceContract_ReturnsNull()
    {
        // Un contrat a une quantité de 1 par convention : le PRU se confondrait
        // avec le montant versé sans rien apprendre.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.InsuranceContract, 5000m, 1m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_ZeroQuantity_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Metal, 3000m, 0m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_FractionalQuantity_KeepsPrecision()
    {
        // Les quantités Trade Republic descendent à six décimales.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 100m, 0.512345m);
        Assert.Equal(195.18m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeGain_PositiveGain_ReturnsAmountAndPercent()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 1250m);
        Assert.Equal(250m, amount);
        Assert.Equal(25m, percent);
    }

    [Fact]
    public void ComputeGain_Loss_ReturnsNegativeValues()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 800m);
        Assert.Equal(-200m, amount);
        Assert.Equal(-20m, percent);
    }

    [Fact]
    public void ComputeGain_NoValuation_ReturnsNulls()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, null);
        Assert.Null(amount);
        Assert.Null(percent);
    }

    [Fact]
    public void ComputeGain_ZeroCostBasis_ReturnsAmountButNoPercent()
    {
        // Une ligne reçue en donation a un coût nul : le pourcentage n'a pas de sens,
        // le gain en euros si.
        var (amount, percent) = InvestmentCalculator.ComputeGain(0m, 500m);
        Assert.Equal(500m, amount);
        Assert.Null(percent);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `cd backend && dotnet test`
Expected: échec de compilation, `The name 'InvestmentCalculator' does not exist in the current context`.

- [ ] **Step 3: Écrire l'implémentation minimale**

Fichier `backend/FinanceApp.API/Services/InvestmentCalculator.cs` :

```csharp
using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Calculs de performance des investissements. Volontairement pur : aucune dépendance
/// au DbContext, pour que les règles restent testables unitairement. Ces règles portent
/// le risque d'erreur silencieuse (un chiffre faux reste plausible à l'œil).
/// </summary>
public static class InvestmentCalculator
{
    /// <summary>
    /// Prix de revient unitaire. Null pour un contrat d'assurance-vie (quantité de 1 par
    /// convention) et null si la quantité est nulle.
    /// </summary>
    public static decimal? ComputeUnitCost(InvestmentKind kind, decimal costBasis, decimal quantity)
    {
        if (kind == InvestmentKind.InsuranceContract) return null;
        if (quantity == 0m) return null;
        return costBasis / quantity;
    }

    /// <summary>
    /// Plus-value latente en euros et en pourcentage. Le pourcentage est null quand le
    /// coût de revient est nul, cas où il n'a pas de sens.
    /// </summary>
    public static (decimal? Amount, decimal? Percent) ComputeGain(decimal costBasis, decimal? marketValue)
    {
        if (marketValue is null) return (null, null);

        var amount = marketValue.Value - costBasis;
        var percent = costBasis == 0m ? (decimal?)null : amount / costBasis * 100m;
        return (amount, percent);
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `cd backend && dotnet test`
Expected: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 5: Commit**

```bash
git add backend/FinanceApp.API/Services/InvestmentCalculator.cs backend/FinanceApp.Tests/InvestmentCalculatorTests.cs
git commit -m "feat(investissements): calcul PRU et plus-value latente, couvert par tests unitaires"
```

---

### Task 3: Calculateur, rendement annualisé et fraîcheur

C'est la tâche qui porte la règle non négociable de la spec : aucun rendement sans date d'entrée.

**Files:**
- Modify: `backend/FinanceApp.API/Services/InvestmentCalculator.cs`
- Test: `backend/FinanceApp.Tests/InvestmentCalculatorTests.cs`

**Interfaces:**
- Consumes: `ComputeGain` de Task 2, `ValuationSource` de Task 1
- Produces: `InvestmentCalculator.ComputeCagr(decimal costBasis, decimal? marketValue, DateTime? firstPurchaseDate, DateTime asOf) → decimal?`, `InvestmentCalculator.IsStale(ValuationSource source, DateTime asOf, DateTime now) → bool`

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `backend/FinanceApp.Tests/InvestmentCalculatorTests.cs`, dans la classe existante :

```csharp
    [Fact]
    public void ComputeCagr_NoFirstPurchaseDate_ReturnsNull()
    {
        // Règle non négociable de la spec : pas de date d'entrée, pas de rendement.
        // Une case vide vaut mieux qu'un chiffre reposant sur une hypothèse invisible.
        var result = InvestmentCalculator.ComputeCagr(1000m, 1500m, null, new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_HoldingShorterThanOneYear_ReturnsNull()
    {
        // Annualiser six mois de détention produit un chiffre spectaculaire et faux.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 1200m, new DateTime(2026, 3, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_TwoYearsDoubling_ReturnsAboutFortyOnePercent()
    {
        // 1000 → 2000 en 2 ans = racine carrée de 2, soit environ 41,42 %.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 2000m, new DateTime(2024, 7, 28), new DateTime(2026, 7, 28));
        Assert.NotNull(result);
        Assert.Equal(41.42m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeCagr_NoValuation_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(1000m, null, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_ZeroOrNegativeCostBasis_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(0m, 500m, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void IsStale_ManualWithinThirtyDays_IsFresh()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-29), now));
    }

    [Fact]
    public void IsStale_ManualBeyondThirtyDays_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-31), now));
    }

    [Fact]
    public void IsStale_AutomaticBeyondFortyEightHours_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-49), now));
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-47), now));
    }
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `cd backend && dotnet test`
Expected: échec de compilation, `'InvestmentCalculator' does not contain a definition for 'ComputeCagr'`.

- [ ] **Step 3: Écrire l'implémentation**

Ajouter à `backend/FinanceApp.API/Services/InvestmentCalculator.cs`, dans la classe :

```csharp
    /// <summary>Seuil de péremption d'une valorisation saisie à la main.</summary>
    private static readonly TimeSpan ManualStaleThreshold = TimeSpan.FromDays(30);

    /// <summary>Seuil de péremption d'une valorisation automatique.</summary>
    private static readonly TimeSpan AutomaticStaleThreshold = TimeSpan.FromHours(48);

    /// <summary>
    /// Rendement annualisé approximatif (CAGR). Renvoie null dans tous les cas où le chiffre
    /// ne serait pas fondé : pas de date d'entrée, pas de valorisation, coût de revient nul,
    /// ou détention de moins d'un an (annualiser une durée courte produit un chiffre absurde).
    /// Le TRI exact viendra avec l'historique de mouvements, au lot Trade Republic.
    /// </summary>
    public static decimal? ComputeCagr(decimal costBasis, decimal? marketValue, DateTime? firstPurchaseDate, DateTime asOf)
    {
        if (firstPurchaseDate is null) return null;
        if (marketValue is null) return null;
        if (costBasis <= 0m) return null;

        var years = (asOf - firstPurchaseDate.Value).TotalDays / 365.25;
        if (years < 1.0) return null;

        var ratio = (double)(marketValue.Value / costBasis);
        if (ratio <= 0) return null;

        var cagr = Math.Pow(ratio, 1.0 / years) - 1.0;
        return (decimal)(cagr * 100.0);
    }

    /// <summary>
    /// Une valorisation périmée doit se voir périmée. Le seuil dépend de la source :
    /// 30 jours pour une saisie manuelle, 48 heures pour une source automatique.
    /// </summary>
    public static bool IsStale(ValuationSource source, DateTime asOf, DateTime now)
    {
        var threshold = source == ValuationSource.Manual ? ManualStaleThreshold : AutomaticStaleThreshold;
        return now - asOf > threshold;
    }
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `cd backend && dotnet test`
Expected: `Passed! - Failed: 0, Passed: 16`

- [ ] **Step 5: Commit**

```bash
git add backend/FinanceApp.API/Services/InvestmentCalculator.cs backend/FinanceApp.Tests/InvestmentCalculatorTests.cs
git commit -m "feat(investissements): CAGR et fraîcheur, aucun rendement affiché sans date d'entrée"
```

---

### Task 4: Persistance EF Core et migration

**Files:**
- Modify: `backend/FinanceApp.API/Data/AppDbContext.cs`
- Create: migration générée sous `backend/FinanceApp.API/Migrations/`

**Interfaces:**
- Consumes: `Investment`, `InvestmentValuation` de Task 1
- Produces: `AppDbContext.Investments`, `AppDbContext.InvestmentValuations`

- [ ] **Step 1: Ajouter les DbSets**

Dans `backend/FinanceApp.API/Data/AppDbContext.cs`, après la ligne `public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();` :

```csharp
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<InvestmentValuation> InvestmentValuations => Set<InvestmentValuation>();
    public DbSet<InvestmentMovement> InvestmentMovements => Set<InvestmentMovement>();
```

- [ ] **Step 2: Ajouter la configuration dans OnModelCreating**

Dans le même fichier, à la fin de `OnModelCreating`, après le bloc `ShoppingItem` :

```csharp
        // Investment
        // Précision explicite obligatoire : SQLite ne porte pas de précision native sur decimal,
        // et les quantités fractionnaires (parts Trade Republic) descendent à six décimales.
        modelBuilder.Entity<Investment>()
            .Property(i => i.Quantity)
            .HasPrecision(18, 6);

        modelBuilder.Entity<Investment>()
            .Property(i => i.CostBasis)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Investment>()
            .HasOne(i => i.Dashboard)
            .WithMany()
            .HasForeignKey(i => i.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Investment>()
            .HasIndex(i => i.DashboardId);

        // InvestmentValuation
        modelBuilder.Entity<InvestmentValuation>()
            .Property(v => v.MarketValue)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InvestmentValuation>()
            .Property(v => v.UnitPrice)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentValuation>()
            .HasOne(v => v.Investment)
            .WithMany(i => i.Valuations)
            .HasForeignKey(v => v.InvestmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Une seule valorisation par ligne et par date : une correction remplace la valeur
        // du jour, elle ne crée pas un doublon qui fausserait la courbe.
        modelBuilder.Entity<InvestmentValuation>()
            .HasIndex(v => new { v.InvestmentId, v.AsOf })
            .IsUnique();

        // InvestmentMovement
        // Table créée dès maintenant pour n'avoir qu'une migration. Non alimentée au lot 1.
        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.Quantity)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.UnitPrice)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InvestmentMovement>()
            .HasOne(m => m.Investment)
            .WithMany(i => i.Movements)
            .HasForeignKey(m => m.InvestmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvestmentMovement>()
            .HasIndex(m => m.InvestmentId);

        // Déduplication des imports courtier. Filtré pour tolérer plusieurs mouvements
        // saisis à la main sans identifiant externe.
        modelBuilder.Entity<InvestmentMovement>()
            .HasIndex(m => m.ExternalId)
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");
```

- [ ] **Step 3: Générer la migration**

```bash
cd backend/FinanceApp.API
dotnet ef migrations add AddInvestments
```
Expected: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 4: Relire la migration générée avant de l'appliquer**

Ouvrir le fichier `Migrations/<timestamp>_AddInvestments.cs` et vérifier trois points :
- Trois tables créées, `Investments`, `InvestmentValuations` et `InvestmentMovements`, et **aucune autre table modifiée**
- Les index uniques sont présents : `(InvestmentId, AsOf)` sur les valorisations, `ExternalId` filtré sur les mouvements
- Aucun `DropColumn` ni `DropTable` nulle part

Si une table existante apparaît dans la migration, s'arrêter et signaler : le modèle a divergé de la base et ce n'est pas au lot 1 de le corriger.

- [ ] **Step 5: Appliquer la migration**

```bash
cd backend/FinanceApp.API
dotnet ef database update
```
Expected: `Done.`

- [ ] **Step 6: Vérifier le schéma réellement créé**

```bash
cd backend/FinanceApp.API
sqlite3 finance.db ".schema Investments" ".schema InvestmentValuations" ".schema InvestmentMovements"
```
Expected: les trois tables existent, avec l'index unique `IX_InvestmentValuations_InvestmentId_AsOf` et l'index filtré `IX_InvestmentMovements_ExternalId`.

- [ ] **Step 7: Commit**

```bash
git add backend/FinanceApp.API/Data/AppDbContext.cs backend/FinanceApp.API/Migrations/
git commit -m "feat(investissements): DbSets, configuration EF et migration AddInvestments"
```

---

### Task 5: DTOs et controller CRUD

**Files:**
- Create: `backend/FinanceApp.API/DTOs/InvestmentDtos.cs`
- Create: `backend/FinanceApp.API/Controllers/InvestmentController.cs`

**Interfaces:**
- Consumes: `InvestmentCalculator` (Tasks 2 et 3), `AppDbContext.Investments` (Task 4)
- Produces: endpoints `GET /api/investment?dashboardId=`, `POST /api/investment`, `PUT /api/investment/{id}`, `DELETE /api/investment/{id}` ; DTOs `InvestmentDto`, `CreateInvestmentDto`, `UpdateInvestmentDto`

- [ ] **Step 1: Créer les DTOs**

Fichier `backend/FinanceApp.API/DTOs/InvestmentDtos.cs` :

```csharp
using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

/// <summary>Ligne d'investissement enrichie de sa performance calculée.</summary>
public class InvestmentDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Holder { get; set; } = string.Empty;
    public InvestmentKind Kind { get; set; }
    public string? Isin { get; set; }
    public string? MetalCode { get; set; }
    public decimal Quantity { get; set; }
    public InvestmentUnit Unit { get; set; }
    public decimal CostBasis { get; set; }
    public DateTime? FirstPurchaseDate { get; set; }
    public InvestmentSource Source { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>PRU. Null pour un contrat d'assurance-vie.</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Valeur de la dernière valorisation. Null si aucune n'a été saisie.</summary>
    public decimal? MarketValue { get; set; }
    public DateTime? ValuationAsOf { get; set; }
    public bool IsStale { get; set; }
    public decimal? GainAmount { get; set; }
    public decimal? GainPercent { get; set; }
    /// <summary>CAGR approximatif. Null tant qu'aucune date d'entrée n'est renseignée.</summary>
    public decimal? AnnualizedReturn { get; set; }
}

public class CreateInvestmentDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Holder { get; set; } = string.Empty;

    [Required]
    public InvestmentKind Kind { get; set; }

    [MaxLength(12)]
    public string? Isin { get; set; }

    [MaxLength(10)]
    public string? MetalCode { get; set; }

    [Range(0.000001, 999999999)]
    public decimal Quantity { get; set; }

    [Required]
    public InvestmentUnit Unit { get; set; }

    [Range(0, 99999999.99)]
    public decimal CostBasis { get; set; }

    public DateTime? FirstPurchaseDate { get; set; }
}

public class UpdateInvestmentDto
{
    [MaxLength(120)]
    public string? Name { get; set; }

    [MaxLength(60)]
    public string? Holder { get; set; }

    [MaxLength(12)]
    public string? Isin { get; set; }

    [MaxLength(10)]
    public string? MetalCode { get; set; }

    [Range(0.000001, 999999999)]
    public decimal? Quantity { get; set; }

    [Range(0, 99999999.99)]
    public decimal? CostBasis { get; set; }

    public DateTime? FirstPurchaseDate { get; set; }

    public bool? IsArchived { get; set; }
}

public class CreateValuationDto
{
    [Required]
    public DateTime AsOf { get; set; }

    [Range(0, 99999999.99)]
    public decimal MarketValue { get; set; }

    [Range(0, 99999999.999999)]
    public decimal? UnitPrice { get; set; }
}
```

- [ ] **Step 2: Créer le controller**

Fichier `backend/FinanceApp.API/Controllers/InvestmentController.cs` :

```csharp
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

    public InvestmentController(AppDbContext context)
    {
        _context = context;
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
}
```

- [ ] **Step 3: Vérifier la compilation et les tests**

Run: `cd backend && dotnet build && dotnet test`
Expected: `Build succeeded`, `Passed: 16`

- [ ] **Step 4: Vérifier les endpoints dans Swagger**

Lancer `cd backend/FinanceApp.API && dotnet run`, ouvrir `http://localhost:5000/swagger`, confirmer que les quatre endpoints `api/investment` apparaissent. Arrêter le serveur ensuite.

- [ ] **Step 5: Commit**

```bash
git add backend/FinanceApp.API/DTOs/InvestmentDtos.cs backend/FinanceApp.API/Controllers/InvestmentController.cs
git commit -m "feat(investissements): DTOs et controller CRUD avec contrôle d'accès par dashboard"
```

---

### Task 6: Endpoint de valorisation

**Files:**
- Modify: `backend/FinanceApp.API/Controllers/InvestmentController.cs`

**Interfaces:**
- Consumes: `CreateValuationDto` de Task 5
- Produces: `POST /api/investment/{id}/valuation`, `GET /api/investment/{id}/valuations`

- [ ] **Step 1: Ajouter le DTO de sortie des valorisations**

Ajouter à `backend/FinanceApp.API/DTOs/InvestmentDtos.cs` :

```csharp
public class InvestmentValuationDto
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    public DateTime AsOf { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal MarketValue { get; set; }
    public ValuationSource Source { get; set; }
}
```

- [ ] **Step 2: Ajouter les deux endpoints**

Ajouter à la fin de la classe `InvestmentController` :

```csharp
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

        // AsOf porte la date de la valeur, pas la date de saisie. On normalise à la journée
        // pour que la contrainte d'unicité fasse son travail.
        var asOf = dto.AsOf.Date;

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
```

- [ ] **Step 3: Vérifier la compilation et les tests**

Run: `cd backend && dotnet build && dotnet test`
Expected: `Build succeeded`, `Passed: 16`

- [ ] **Step 4: Commit**

```bash
git add backend/FinanceApp.API/Controllers/InvestmentController.cs backend/FinanceApp.API/DTOs/InvestmentDtos.cs
git commit -m "feat(investissements): endpoints de valorisation datée, empilement sans écrasement de l'historique"
```

---

### Task 7: Client frontend (types, API, hook)

**Files:**
- Create: `frontend/src/types/investment.ts`
- Create: `frontend/src/api/investments.ts`
- Modify: `frontend/src/hooks/queries.ts`

**Interfaces:**
- Consumes: endpoints de Tasks 5 et 6
- Produces: `investmentsApi` (`getAll`, `create`, `update`, `delete`, `addValuation`, `getValuations`), `useInvestmentsQuery(dashboardId)`

- [ ] **Step 1: Créer les types**

Fichier `frontend/src/types/investment.ts` :

```typescript
export const InvestmentKind = {
  Security: 0,
  Metal: 1,
  InsuranceContract: 2,
} as const;
export type InvestmentKind = (typeof InvestmentKind)[keyof typeof InvestmentKind];

export const InvestmentUnit = {
  Share: 0,
  Gram: 1,
  Ounce: 2,
  Contract: 3,
} as const;
export type InvestmentUnit = (typeof InvestmentUnit)[keyof typeof InvestmentUnit];

export const InvestmentSource = {
  Manual: 0,
  TradeRepublic: 1,
} as const;
export type InvestmentSource = (typeof InvestmentSource)[keyof typeof InvestmentSource];

export const ValuationSource = {
  Manual: 0,
  TradeRepublic: 1,
  SpotApi: 2,
} as const;
export type ValuationSource = (typeof ValuationSource)[keyof typeof ValuationSource];

export interface Investment {
  id: number;
  dashboardId: number;
  name: string;
  holder: string;
  kind: InvestmentKind;
  isin: string | null;
  metalCode: string | null;
  quantity: number;
  unit: InvestmentUnit;
  costBasis: number;
  firstPurchaseDate: string | null;
  source: InvestmentSource;
  isArchived: boolean;
  createdAt: string;
  /** PRU. null pour un contrat d'assurance-vie. */
  unitCost: number | null;
  marketValue: number | null;
  valuationAsOf: string | null;
  isStale: boolean;
  gainAmount: number | null;
  gainPercent: number | null;
  /** null tant qu'aucune date d'entrée n'est renseignée. */
  annualizedReturn: number | null;
}

export interface InvestmentValuation {
  id: number;
  investmentId: number;
  asOf: string;
  unitPrice: number | null;
  marketValue: number;
  source: ValuationSource;
}

export interface CreateInvestment {
  dashboardId: number;
  name: string;
  holder: string;
  kind: InvestmentKind;
  isin?: string | null;
  metalCode?: string | null;
  quantity: number;
  unit: InvestmentUnit;
  costBasis: number;
  firstPurchaseDate?: string | null;
}

export interface UpdateInvestment {
  name?: string;
  holder?: string;
  isin?: string | null;
  metalCode?: string | null;
  quantity?: number;
  costBasis?: number;
  firstPurchaseDate?: string | null;
  isArchived?: boolean;
}

export interface CreateValuation {
  asOf: string;
  marketValue: number;
  unitPrice?: number | null;
}
```

- [ ] **Step 2: Créer le client API**

Fichier `frontend/src/api/investments.ts` :

```typescript
import apiClient from './client';
import type {
  Investment,
  InvestmentValuation,
  CreateInvestment,
  UpdateInvestment,
  CreateValuation,
} from '../types/investment';

export const investmentsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<Investment[]>('/investment', { params: { dashboardId } }),

  create: (data: CreateInvestment) =>
    apiClient.post<Investment>('/investment', data),

  update: (id: number, data: UpdateInvestment) =>
    apiClient.put<Investment>(`/investment/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/investment/${id}`),

  addValuation: (id: number, data: CreateValuation) =>
    apiClient.post<Investment>(`/investment/${id}/valuation`, data),

  getValuations: (id: number) =>
    apiClient.get<InvestmentValuation[]>(`/investment/${id}/valuations`),
};
```

- [ ] **Step 3: Ajouter le hook de requête**

Dans `frontend/src/hooks/queries.ts`, ajouter en suivant exactement la forme de `useProjectEnvelopesQuery` déjà présente dans ce fichier (mêmes options, même gestion du `enabled`) :

```typescript
export const useInvestmentsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['investments', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await investmentsApi.getAll(dashboardId!);
      return res.data;
    },
  });
```

Ajouter l'import `import { investmentsApi } from '../api/investments';` en tête du fichier, avec les autres imports d'API.

- [ ] **Step 4: Vérifier la compilation TypeScript**

Run: `cd frontend && npm run build`
Expected: build réussi, aucune erreur TypeScript.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types/investment.ts frontend/src/api/investments.ts frontend/src/hooks/queries.ts
git commit -m "feat(investissements): types, client API et hook de requête côté frontend"
```

---

### Task 8: Page de saisie

Page volontairement sobre : la mise en forme riche (groupements, courbe, totaux consolidés) est le lot 2. Ici, l'objectif est que les lignes puissent être saisies et relues.

**Files:**
- Create: `frontend/src/pages/Investments.tsx`
- Modify: `frontend/src/App.tsx`

**Interfaces:**
- Consumes: `useInvestmentsQuery`, `investmentsApi` de Task 7
- Produces: route `/investments`

- [ ] **Step 1: Créer la page**

Fichier `frontend/src/pages/Investments.tsx` :

```tsx
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import { useInvestmentsQuery } from '../hooks/queries';
import { investmentsApi } from '../api/investments';
import { InvestmentKind, InvestmentUnit } from '../types/investment';
import type { Investment, CreateInvestment } from '../types/investment';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

interface InvestmentForm {
  name: string;
  holder: string;
  kind: number;
  quantity: string;
  unit: number;
  costBasis: string;
  firstPurchaseDate: string;
}

const emptyForm: InvestmentForm = {
  name: '',
  holder: '',
  kind: InvestmentKind.Security,
  quantity: '',
  unit: InvestmentUnit.Share,
  costBasis: '',
  firstPurchaseDate: '',
};

const kindLabels: Record<number, string> = {
  [InvestmentKind.Security]: 'Titre coté',
  [InvestmentKind.Metal]: 'Métal',
  [InvestmentKind.InsuranceContract]: 'Assurance-vie',
};

const unitLabels: Record<number, string> = {
  [InvestmentUnit.Share]: 'part',
  [InvestmentUnit.Gram]: 'g',
  [InvestmentUnit.Ounce]: 'oz',
  [InvestmentUnit.Contract]: 'contrat',
};

const Investments = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const { data: investments, isLoading } = useInvestmentsQuery(dashboardId);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [form, setForm] = useState<InvestmentForm>(emptyForm);
  const [valuationFor, setValuationFor] = useState<Investment | null>(null);
  const [valuationValue, setValuationValue] = useState('');
  const [valuationDate, setValuationDate] = useState(new Date().toISOString().slice(0, 10));

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['investments', dashboardId] });

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dashboardId) return;

    const isContract = form.kind === InvestmentKind.InsuranceContract;
    const payload: CreateInvestment = {
      dashboardId,
      name: form.name,
      holder: form.holder,
      kind: form.kind as CreateInvestment['kind'],
      quantity: isContract ? 1 : parseFloat(form.quantity || '0'),
      unit: (isContract ? InvestmentUnit.Contract : form.unit) as CreateInvestment['unit'],
      costBasis: parseFloat(form.costBasis || '0'),
      firstPurchaseDate: form.firstPurchaseDate || null,
    };

    try {
      await investmentsApi.create(payload);
      setForm(emptyForm);
      refresh();
      showToast('Ligne ajoutée', 'success');
    } catch {
      showToast("Impossible d'ajouter la ligne", 'error');
    }
  };

  const handleValuation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!valuationFor) return;

    try {
      await investmentsApi.addValuation(valuationFor.id, {
        asOf: valuationDate,
        marketValue: parseFloat(valuationValue || '0'),
      });
      setValuationFor(null);
      setValuationValue('');
      refresh();
      showToast('Valorisation enregistrée', 'success');
    } catch {
      showToast("Impossible d'enregistrer la valorisation", 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await investmentsApi.delete(id);
      refresh();
      showToast('Ligne supprimée', 'success');
    } catch {
      showToast('Impossible de supprimer la ligne', 'error');
    }
  };

  if (isLoading) return <div className="p-6 text-white/60">Chargement...</div>;

  const total = (investments ?? []).reduce((sum, i) => sum + (i.marketValue ?? 0), 0);

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-baseline justify-between">
        <h1 className="text-2xl font-semibold text-white">Investissements</h1>
        <div className="text-white/60">
          Total valorisé <span className="text-white font-semibold">{formatCurrency(total)}</span>
        </div>
      </div>

      <form onSubmit={handleCreate} className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-4 grid gap-3 md:grid-cols-7">
        <input
          required
          placeholder="Nom"
          className="bg-white/5 rounded-lg px-3 py-2 text-white md:col-span-2"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
        <input
          required
          list="holders"
          placeholder="Titulaire"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.holder}
          onChange={(e) => setForm({ ...form, holder: e.target.value })}
        />
        <datalist id="holders">
          {[...new Set((investments ?? []).map((i) => i.holder))].map((h) => (
            <option key={h} value={h} />
          ))}
        </datalist>
        <select
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.kind}
          onChange={(e) => {
            // L'unité suit la nature de l'actif : une ligne d'or saisie en « part »
            // rendrait la conversion du cours spot impossible au lot suivant.
            const kind = Number(e.target.value);
            const unit =
              kind === InvestmentKind.Metal
                ? InvestmentUnit.Gram
                : kind === InvestmentKind.InsuranceContract
                  ? InvestmentUnit.Contract
                  : InvestmentUnit.Share;
            setForm({ ...form, kind, unit });
          }}
        >
          {Object.entries(kindLabels).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        {form.kind === InvestmentKind.Metal && (
          <select
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.unit}
            onChange={(e) => setForm({ ...form, unit: Number(e.target.value) })}
          >
            <option value={InvestmentUnit.Gram}>gramme</option>
            <option value={InvestmentUnit.Ounce}>once</option>
          </select>
        )}
        {form.kind !== InvestmentKind.InsuranceContract && (
          <input
            required
            type="number"
            step="0.000001"
            placeholder="Quantité"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.quantity}
            onChange={(e) => setForm({ ...form, quantity: e.target.value })}
          />
        )}
        <input
          required
          type="number"
          step="0.01"
          placeholder="Montant investi"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.costBasis}
          onChange={(e) => setForm({ ...form, costBasis: e.target.value })}
        />
        <input
          type="date"
          title="Date d'entrée, nécessaire pour afficher un rendement annualisé"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.firstPurchaseDate}
          onChange={(e) => setForm({ ...form, firstPurchaseDate: e.target.value })}
        />
        <button type="submit" className="bg-indigo-500 hover:bg-indigo-400 rounded-lg px-4 py-2 text-white font-medium">
          Ajouter
        </button>
      </form>

      <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-white/50 border-b border-white/10">
            <tr>
              <th className="text-left p-3">Ligne</th>
              <th className="text-left p-3">Titulaire</th>
              <th className="text-right p-3">Quantité</th>
              <th className="text-right p-3">PRU</th>
              <th className="text-right p-3">Investi</th>
              <th className="text-right p-3">Valeur</th>
              <th className="text-right p-3">Plus-value</th>
              <th className="text-right p-3">Rendement</th>
              <th className="p-3"></th>
            </tr>
          </thead>
          <tbody>
            {(investments ?? []).map((i) => (
              <tr key={i.id} className="border-b border-white/5 text-white/90">
                <td className="p-3">
                  {i.name}
                  <span className="text-white/40 ml-2">{kindLabels[i.kind]}</span>
                </td>
                <td className="p-3">{i.holder}</td>
                <td className="p-3 text-right">
                  {i.kind === InvestmentKind.InsuranceContract ? '—' : `${i.quantity} ${unitLabels[i.unit]}`}
                </td>
                <td className="p-3 text-right">{i.unitCost != null ? formatCurrency(i.unitCost) : '—'}</td>
                <td className="p-3 text-right">{formatCurrency(i.costBasis)}</td>
                <td className={`p-3 text-right ${i.isStale ? 'text-white/40' : ''}`}>
                  {i.marketValue != null ? formatCurrency(i.marketValue) : '—'}
                  {i.valuationAsOf && (
                    <div className="text-xs text-white/40">
                      au {new Date(i.valuationAsOf).toLocaleDateString('fr-BE')}
                    </div>
                  )}
                </td>
                <td className={`p-3 text-right ${(i.gainAmount ?? 0) >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                  {i.gainAmount != null ? formatCurrency(i.gainAmount) : '—'}
                  {i.gainPercent != null && (
                    <div className="text-xs opacity-70">{i.gainPercent.toFixed(1)} %</div>
                  )}
                </td>
                <td className="p-3 text-right">
                  {i.annualizedReturn != null ? (
                    <span title="Approximatif, calculé sur la date d'entrée">
                      {i.annualizedReturn.toFixed(1)} % / an
                    </span>
                  ) : (
                    <span className="text-white/30" title="Renseigne une date d'entrée pour obtenir un rendement">
                      —
                    </span>
                  )}
                </td>
                <td className="p-3 text-right whitespace-nowrap">
                  <button
                    onClick={() => { setValuationFor(i); setValuationValue(''); }}
                    className="text-indigo-300 hover:text-indigo-200 mr-3"
                  >
                    Valoriser
                  </button>
                  <button onClick={() => handleDelete(i.id)} className="text-white/40 hover:text-rose-400">
                    Supprimer
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {valuationFor && (
        <form onSubmit={handleValuation} className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-4 flex flex-wrap gap-3 items-center">
          <span className="text-white">Valoriser {valuationFor.name}</span>
          <input
            required
            type="number"
            step="0.01"
            placeholder="Valeur actuelle"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={valuationValue}
            onChange={(e) => setValuationValue(e.target.value)}
          />
          <input
            required
            type="date"
            title="Date du relevé, pas date de saisie"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={valuationDate}
            onChange={(e) => setValuationDate(e.target.value)}
          />
          <button type="submit" className="bg-indigo-500 hover:bg-indigo-400 rounded-lg px-4 py-2 text-white">
            Enregistrer
          </button>
          <button type="button" onClick={() => setValuationFor(null)} className="text-white/50 hover:text-white">
            Annuler
          </button>
        </form>
      )}
    </div>
  );
};

export default Investments;
```

- [ ] **Step 2: Déclarer la route**

Dans `frontend/src/App.tsx`, ajouter l'import paresseux à côté de celui d'`Envelopes` :

```tsx
const Investments = lazy(() => import('./pages/Investments'));
```

Puis la route, juste après `<Route path="/envelopes" element={<Envelopes />} />` :

```tsx
                  <Route path="/investments" element={<Investments />} />
```

- [ ] **Step 3: Vérifier la compilation**

Run: `cd frontend && npm run build && npm run lint`
Expected: build réussi, lint sans erreur.

- [ ] **Step 4: Vérifier à l'écran**

Lancer le backend (`cd backend/FinanceApp.API && dotnet run`) et le frontend (`cd frontend && npm run dev`), ouvrir `http://localhost:5173/investments`. Créer une ligne de chaque type et vérifier trois choses :
- Le formulaire masque le champ quantité pour une assurance-vie
- Le choix « Métal » fait apparaître le sélecteur gramme ou once, et l'unité choisie s'affiche bien dans la colonne quantité du tableau
- Une ligne sans date d'entrée affiche un tiret dans la colonne rendement, jamais un chiffre
- Après valorisation, la plus-value s'affiche et la date de valeur apparaît sous le montant

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/Investments.tsx frontend/src/App.tsx
git commit -m "feat(investissements): page de saisie des lignes et des valorisations"
```

---

### Task 9: Test E2E du parcours

**Files:**
- Modify: `tests/e2e/finance-app.spec.ts`

**Interfaces:**
- Consumes: la page `/investments` de Task 8
- Produces: rien

**Contrainte structurelle à respecter.** Les tests E2E existants vivent dans un unique `test.describe.serial('FinanceApp E2E')` qui partage une seule instance `page` créée en `beforeAll`, et qui s'authentifie en s'inscrivant au premier test de la série. Il n'existe **aucun helper de login réutilisable**, et l'inscription passe par une confirmation email. Un fichier de spec séparé ne pourrait donc pas s'authentifier. Le nouveau test s'ajoute à la fin de la série existante et réutilise `page`, déjà connectée.

- [ ] **Step 1: Repérer le point d'insertion**

Ouvrir `tests/e2e/finance-app.spec.ts` et repérer le dernier `test(...)` de la série (le neuvième). Le nouveau test s'insère juste après, **à l'intérieur** du `test.describe.serial`, avant l'accolade fermante.

- [ ] **Step 2: Écrire le test**

Insérer dans `tests/e2e/finance-app.spec.ts`, en respectant la numérotation existante (adapter `Test 10` si le dernier test porte un autre numéro) :

```typescript
  test('Test 10 : Investissement créé, valorisé, plus-value affichée', async () => {
    await page.goto('/investments');
    await page.waitForURL('**/investments');

    const investmentName = `ETF World ${Date.now()}`;

    await page.getByPlaceholder('Nom').fill(investmentName);
    await page.getByPlaceholder('Titulaire').fill('Sébastien');
    await page.getByPlaceholder('Quantité').fill('10');
    await page.getByPlaceholder('Montant investi').fill('1000');
    await page.getByRole('button', { name: 'Ajouter' }).click();

    const row = page.getByRole('row').filter({ hasText: investmentName });
    await expect(row).toBeVisible();

    // Sans date d'entrée renseignée, aucun rendement annualisé ne doit apparaître.
    // C'est la règle non négociable de la spec, vérifiée de bout en bout.
    await expect(row).not.toContainText('% / an');

    await row.getByRole('button', { name: 'Valoriser' }).click();
    await page.getByPlaceholder('Valeur actuelle').fill('1250');
    await page.getByRole('button', { name: 'Enregistrer' }).click();

    // 1000 investis, 1250 valorisés : 250 € de plus-value, soit 25 %.
    await expect(row).toContainText('25,0');
  });
```

- [ ] **Step 3: Lancer la suite complète**

Backend et frontend doivent tourner.
Run: `cd tests && npm run test`
Expected: 10 tests passés. La série étant `serial`, un échec sur un test antérieur fait tomber les suivants : lire le premier échec, pas le dernier.

- [ ] **Step 4: Commit**

```bash
git add tests/e2e/finance-app.spec.ts
git commit -m "test(investissements): parcours E2E création, valorisation et plus-value"
```

---

## Vérification de fin de lot

- [ ] `cd backend && dotnet test` : 16 tests passés
- [ ] `cd frontend && npm run build && npm run lint` : aucune erreur
- [ ] `cd tests && npm run test` : 10 tests passés
- [ ] `git diff --stat master` ne montre aucune modification sous `frontend/src/pages/dashboard/`, ni dans les services de calcul du Bilan ou du burn-down
- [ ] Les trois familles ont été saisies à la main dans l'app et s'affichent correctement

## Déploiement

Ce lot introduit une migration EF. La base de production tourne sur le Raspberry Pi 5 (`http://raspberrypi5:5001`) et n'est **pas** mise à jour par ce plan. Le déploiement suit le skill `deployer-finance-app-pi`, dans une séance distincte, une fois le lot validé en local. Ne pas mélanger les deux : une migration appliquée en prod avant validation locale se défait mal.

Le lot 2 (écran riche, groupements, totaux consolidés, courbe) démarre à partir de là.
