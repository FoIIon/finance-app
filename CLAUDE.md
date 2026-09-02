# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (ASP.NET Core 8)
```bash
cd backend/FinanceApp.API

dotnet build
dotnet run                        # API on http://localhost:5000, Swagger at /swagger (Development only)
dotnet ef database update         # Create/update SQLite DB (finance.db) + seed default categories
dotnet ef migrations add <Name>   # Add a new migration after model changes
dotnet ef migrations has-pending-model-changes   # Must say "No changes" before committing a model change

cd ..
dotnet test FinanceApp.Tests/FinanceApp.Tests.csproj   # xunit, ~240 tests, no database except a few SQLite in-memory fixtures
```

### Frontend (React 19 + Vite)
```bash
cd frontend

npm install
npm run dev      # Dev server on http://localhost:5173
npm run build    # tsc -b && vite build
npm run lint     # Must be at 0 errors
```
There is no frontend unit test runner. Display logic is covered end to end only.

### E2E Tests (Playwright) — requires backend + frontend running
```bash
cd tests

npx playwright install chromium   # First time only
npm run test
npm run test:headed
```
11 tests in `tests/e2e/finance-app.spec.ts`. They register a fresh user each run and confirm the email through the dev email service (no SMTP in Development).

## Architecture

**Layered backend:** `Controllers → Services → AppDbContext (EF Core → SQLite)`. Controllers resolve the caller and the scope (which logical accounts a dashboard covers), then delegate. They do not aggregate.

**Frontend state:** react-query for server data, Context API for session-wide state (auth, current dashboard, period filter, toasts) → axios API client → backend.

### Backend

- **`Controllers/`** — 16 controllers on `ApiControllerBase` (which owns `GetUserId()`), plus `AuthController` (anonymous). `TransactionController` (~760 lines) carries CRUD, flags and the reporting endpoints, all of which delegate to `Services/Reporting`. `TradeRepublicExplorationController` is compiled in Debug only.
- **`Services/`** — Business logic, injected via DI. Two families:
  - *Pure, static, tested without a database*: `BilanClassifier` and the `Reporting/*Builder` classes, `CategoryFlowHistory`, `Refunds`, `PersoScopeRouter`, `InternalTransferReconciler`, `PersonNameMatcher`, `BankAccountReconciler`, `CategoryRuleMatcher`, `InvestmentCalculator`, `LoanCalculator`, `TradeRepublicTimelineClassifier`, `TradeRepublicPortfolioParser`.
  - *Stateful (DbContext or HTTP)*: `Reporting/ReportingService` and `Reporting/AccountBalanceService` (EF queries feeding the builders), `BankSyncService` (hosted service, syncs every 6h, imports and categorizes), `ProvisionService`, `GoCardlessClient`, `TradeRepublicClient`, `DashboardService`, `AccountService`, `InvitationService`, `EmailService`, `TokenService`.
- **`Models/`** — 21 EF Core entities. Key relationships: `User → Account (logical) → Transaction`, `Dashboard → DashboardMember/DashboardAccount` (composite keys), `BankConnection → BankAccount (physical)`. Reporting is scoped by *logical* account through the dashboard; balances are carried by *physical* bank accounts.
- **`DTOs/`** — DataAnnotations validation. Pattern: `CreateXDto` in, `XDto` out. Enums stored as text (`BankProvider`, `RecurringFrequency`, `TransactionType` on recurring) stay strings on the wire.
- **`Data/AppDbContext.cs`** — all configuration in `OnModelCreating` (indexes, FKs, string conversions, seed of 10 default categories). 20 migrations since the July 2026 baseline.

**The one rule that decides where a transaction counts:** `Services/Reporting/BilanClassifier.cs`. Every aggregation (monthly report, summary, burn-down, category histories) goes through it. Change a bilan rule there and nowhere else, then add a test.

**Explicit scopes (no more "oldest wins"):** `Dashboard.IsPersonal` marks the dashboard created at registration, `Account.IsPrimary` marks the logical account that receives common imports, `Account.IsPersonalScope` marks the Perso account created on demand by `PersoAccounts`. Each is unique per user (filtered indexes) and refused on delete.

**Authorization pattern:** `GetUserId()` from the JWT, then dashboard membership is checked before any data is read (IDOR prevention). `TransactionController.GetAccountIds(dashboardId)` is the single scope resolver; without a `dashboardId` it falls back to the personal dashboard.

**Auth flow:** Email confirmation required after registration. JWT (1h) + refresh token, BCrypt passwords. Rate limiting: `auth` and `login` partitioned by IP, `tr-login` and `tr-verify` by user. The rate limiter runs *after* authentication in the pipeline, on purpose.

**Database:** SQLite file `backend/FinanceApp.API/finance.db` in dev, `/home/admin/finance-app/data/finance.db` on the Pi. SQLite cannot `Sum(decimal)` in SQL, so every aggregation projects to `ReportLine` and sums client-side. `Transaction.ExternalId` is unique for import deduplication.

### Frontend

- **`api/`** — Axios client (relative `/api` in prod, `localhost:5000` in dev), Bearer injection, 401 → redirect. One file per resource.
- **`context/`** — `AuthContext`, `DashboardContext`, `PeriodContext`, `ToastContext`. Hooks live in `hooks/` (react-refresh lint rule).
- **`hooks/queries.ts`** — react-query keys and queries; invalidate by key after mutations.
- **`pages/`** — 15 pages, plus 7 tabs under `pages/dashboard/` (Overview, Bilan, Income, Categories, Flows, Projects, Triage).
- **`types/`** — TypeScript interfaces mirroring backend DTOs.

**Routing:** `App.tsx` — public routes (`/login`, `/register`, `/register-success`, `/confirm-email`, `/invitation/accept`) + `ProtectedRoute` wrapper for everything else under `Layout`. Feature pages are lazy-loaded.

## Configuration

Secrets live outside git: `appsettings.Development.json` locally, `appsettings.Production.json` on the Pi.
- `Jwt.Key` — required, the app refuses to start without it
- `GoCardless.SecretId` / `GoCardless.SecretKey` — Open Banking EU
- `Smtp.*` — used in Production only, Development logs emails instead
- `TradeRepublic.DefaultHolder` — holder name written on imported investment lines

Ports: backend `5000`, frontend `5173`. CORS allows `http://localhost:5173` only; in production the backend serves the frontend build from `wwwroot/` on the same origin.

## Working conventions

- Comments and commit messages are in French. Commit subjects follow `type(scope): phrase`.
- A model change is not done until `dotnet ef migrations has-pending-model-changes` says no and the migration has been applied to the local `finance.db`.
- A reporting change is not done until `BilanClassifier` tests still pass and the affected builder has a test for the new case.
- Deployment to the Raspberry Pi is documented in the Yen repo (`.claude/skills/deployer-finance-app-pi`). Never redeploy without a DB backup and a migration script applied with the service stopped.
