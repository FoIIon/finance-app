# Finance App

Gestionnaire de finances personnelles — projet d'apprentissage C# / React / TypeScript.

![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-8.0-512BD4?logo=dotnet)
![React](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript)
![Tailwind CSS](https://img.shields.io/badge/Tailwind%20CSS-4-06B6D4?logo=tailwindcss)

## Fonctionnalités

- **Authentification** — Inscription, connexion, JWT avec refresh automatique
- **Transactions** — CRUD complet avec filtres (date, catégorie, type revenu/dépense)
- **Catégories** — 10 catégories par défaut + création/édition/suppression de catégories personnalisées
- **Tableau de bord** — Solde, revenus, dépenses, camembert par catégorie, courbe d'évolution sur 6 mois
- **Sécurité** — Rate limiting, validation des entrées, protection IDOR, headers de sécurité

## Stack technique

| Couche | Technologie |
|--------|-------------|
| Backend | C# / ASP.NET Core 8 Web API |
| ORM | Entity Framework Core |
| BDD | SQLite |
| Auth | JWT (BCrypt) |
| Frontend | React 18 + TypeScript + Vite |
| Style | Tailwind CSS 4 |
| Graphiques | Recharts |
| Tests E2E | Playwright (9 tests) |

## Structure

```
finance-app/
├── backend/
│   └── FinanceApp.API/
│       ├── Controllers/     # Auth, Transaction, Category
│       ├── Models/          # User, Transaction, Category
│       ├── DTOs/            # Validation avec DataAnnotations
│       ├── Data/            # AppDbContext + migrations + seed
│       ├── Services/        # TokenService (JWT)
│       └── Program.cs
├── frontend/
│   └── src/
│       ├── api/             # Client Axios typé
│       ├── components/      # Layout, ProtectedRoute
│       ├── context/         # AuthContext
│       ├── hooks/           # useAuth, useTransactions
│       ├── pages/           # Dashboard, Transactions, Categories, Login, Register
│       ├── types/           # Interfaces TypeScript
│       └── utils/           # formatCurrency
└── tests/
    └── e2e/                 # Playwright E2E tests
```

## Lancement

### Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (v18+)

La base est un fichier SQLite (`finance.db`), créé automatiquement par les migrations. Rien d'autre à installer.

### Backend

```bash
cd backend/FinanceApp.API
dotnet ef database update    # Créer la BDD + seed des catégories
dotnet run                   # Démarre sur http://localhost:5000
```

### Frontend

```bash
cd frontend
npm install
npm run dev                  # Démarre sur http://localhost:5173
```

### Tests E2E

```bash
cd tests
npm install
npx playwright install chromium
npx playwright test          # Nécessite backend + frontend lancés
```

## Déploiement (Raspberry Pi)

L'app tourne en prod sur le Raspberry Pi 5 de la maison : `http://raspberrypi5:5001`.
En prod, le backend sert aussi le frontend (build Vite copié dans `wwwroot/`), même origine, URL d'API relative.

### Build sur le PC

```bash
# Frontend
cd frontend && npm run build

# Backend self-contained ARM64
cd backend/FinanceApp.API
dotnet publish -c Release -r linux-arm64 --self-contained true -o <dossier-publish>

# Copier le frontend dans le publish
cp -r frontend/dist <dossier-publish>/wwwroot
```

### Sur le Pi

- App : `/home/admin/finance-app/app/` (remplacée à chaque redéploiement)
- BDD : `/home/admin/finance-app/data/finance.db` (jamais touchée par un redéploiement)
- Secrets : `appsettings.Production.json` posé sur le Pi, hors git (JWT, GoCardless)
- Service : systemd `finance-app` (`ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://0.0.0.0:5001`, restart auto)

```bash
# Redéployer : remplacer le contenu de app/ puis
sudo systemctl restart finance-app
```

## API

| Méthode | Route | Description |
|---------|-------|-------------|
| POST | `/api/auth/register` | Inscription |
| POST | `/api/auth/login` | Connexion → JWT |
| GET | `/api/transactions` | Liste (filtres: date, catégorie, type) |
| POST | `/api/transactions` | Créer une transaction |
| PUT | `/api/transactions/{id}` | Modifier |
| DELETE | `/api/transactions/{id}` | Supprimer |
| GET | `/api/transactions/summary` | Résumé + graphiques |
| GET | `/api/categories` | Catégories (défaut + custom) |
| POST | `/api/categories` | Créer une catégorie |
| PUT | `/api/categories/{id}` | Modifier une catégorie |
| DELETE | `/api/categories/{id}` | Supprimer une catégorie |

## Licence

Projet personnel d'apprentissage.
