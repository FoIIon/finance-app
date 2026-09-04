# SeedDemo

Remplit une base SQLite de développement avec un ménage de démonstration : deux utilisateurs, un dashboard commun, trois mois glissants de transactions plausibles, trois récurrentes. Rejouable à volonté, et physiquement incapable de toucher la base de production.

```bash
dotnet run --project tools/SeedDemo -- --db backend/FinanceApp.API/finance.db
```

Connexion ensuite avec `seb@demo.invalid` ou `audrey@demo.invalid`, mot de passe `Demo-1234!`. Les emails sont déjà confirmés.

## Ce que ça fait

1. Passe les verrous (`SeedGuard`) avant d'ouvrir quoi que ce soit. Refus, code de sortie 2 et rien d'autre, si : `--db` manque, le chemin contient `finance-app/data` ou commence par `/home/`, `ASPNETCORE_ENVIRONMENT` ou `DOTNET_ENVIRONMENT` vaut `Production`, ou la machine s'appelle `raspberrypi5`.
2. Applique les migrations de l'API sur la base cible (`Database.Migrate()`), donc le schéma suit toujours le code, pas une copie SQL qui vieillit.
3. Purge les données de démo précédentes (utilisateurs en `@demo.invalid`, transactions en `demo-<n>`), dans l'ordre imposé par les clés étrangères, puis recrée le tout. Deux passes le même jour donnent exactement le même contenu (graine fixe). Les données qui ne sont pas de démo ne sont pas touchées.

## Contenu

`seb` a un dashboard Personnel, un compte principal (`IsPrimary`), un compte Perso (`IsPersonalScope`) et le dashboard « Commun démo » dont `audrey` est membre. `audrey` a son dashboard Personnel et un compte principal. Environ 150 transactions réparties sur les 10 catégories par défaut : deux salaires par mois en fin de mois, loyer et énergie marqués `IsFixed`, quatre remboursements `IsRefund`, une réparation `IsExceptional`, courses, restaurants, pharmacie, allocations familiales, une facture freelance sur le compte Perso. Trois `RecurringTransaction` sur le dashboard commun, le salaire avec `ProvisionAtMonthStart`.

Aucune donnée réelle : contreparties génériques ou enseignes publiques, IBAN de test à clé valide.

## Tests

`backend/FinanceApp.Tests/SeedGuardTests.cs` : chaque verrou, un chemin de dev qui passe, et le seed exécuté deux fois sur une base SQLite en mémoire avec le vrai schéma.
