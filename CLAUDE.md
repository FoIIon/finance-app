# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (ASP.NET Core 8)
```bash
cd backend/FinanceApp.API

dotnet build
dotnet run                        # API on http://localhost:5000, Swagger at /swagger
dotnet ef database update         # Create/update SQLite DB (finance.db) + seed default categories
dotnet ef migrations add <Name>   # Add a new migration after model changes
```

### Frontend (React 19 + Vite)
```bash
cd frontend

npm install
npm run dev      # Dev server on http://localhost:5173
npm run build
npm run lint
```

### E2E Tests (Playwright) — requires backend + frontend running
```bash
cd tests

npx playwright install chromium   # First time only
npm run test
npm run test:headed
```

## Architecture

**Layered backend:** `Controllers → Services → AppDbContext (EF Core → SQLite)`

**Frontend state:** Context API (AuthContext, DashboardContext) + custom hooks → axios API client → backend

### Backend

- **`Controllers/`** — 8 controllers: Auth, Transaction, Category, Account, Dashboard, Invitation, Banking, CategoryRule
- **`Services/`** — Business logic extracted from controllers; injected via DI. Includes `BankSyncService` (hosted background service, syncs banks every 6h), `GoCardlessClient`, `TradeRepublicClient`
- **`Models/`** — 15 EF Core entities. Key relationships: `User → Account → Transaction`, `Dashboard → DashboardMember/DashboardAccount` (composite keys)
- **`DTOs/`** — DataAnnotations validation (`[Required]`, `[Range]`, `[MaxLength]`). Pattern: `CreateXDto` in, `XDto` out (enriched with names/labels)
- **`Data/AppDbContext.cs`** — 10 DbSets, all config in `OnModelCreating` (indexes, FKs, seed data for 10 default categories)

**Authorization pattern:** Every protected controller extracts `UserId` from JWT claims, then validates ownership through dashboard membership before querying data (IDOR prevention).

**Auth flow:** Email confirmation required after registration. JWT (1hr) + refresh token (BCrypt passwords). Rate limiting on auth endpoints (5 req/min).

**Database:** SQLite file `backend/FinanceApp.API/finance.db`. Single migration (`InitialCreate`). `Transaction.ExternalId` has unique constraint for deduplication during bank imports.

### Frontend

- **`api/`** — Axios client with Bearer token injection (request interceptor) and 401→redirect (response interceptor). Separate file per resource.
- **`context/`** — `AuthContext` (token persisted in localStorage), `DashboardContext` (current dashboard selection)
- **`hooks/`** — `useTransactions`, `useDashboards`, `useBanking` — fetch + mutation logic per feature
- **`pages/`** — 11 pages. Dashboard shows charts (Recharts: pie by category, 6-month line). Transactions page has filters (date, category, type, search).
- **`types/`** — TypeScript interfaces mirroring backend DTOs

**Routing:** `App.tsx` — public routes (`/login`, `/register`, `/confirm-email`, `/invitation/accept`) + `ProtectedRoute` wrapper for everything else under `Layout`.

## Configuration

Secrets to fill in `backend/FinanceApp.API/appsettings.json`:
- `GoCardless.SecretId` / `GoCardless.SecretKey` — Open Banking EU
- `Smtp.Username` / `Smtp.Password` — Mailtrap sandbox (email confirmation, invitations)

Ports: backend `5000`, frontend `5173` (hardcoded in `api/client.ts` baseURL).
