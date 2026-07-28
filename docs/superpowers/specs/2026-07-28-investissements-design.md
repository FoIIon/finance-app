# Design du suivi des investissements

Date : 2026-07-28
Statut : validé, prêt pour planification

## Objectif

Ajouter à finance-app un suivi de portefeuille avec valorisation et performance, couvrant les trois familles d'actifs réellement détenues :

- **Titres cotés** chez Trade Republic (actions, ETF)
- **Assurance-vie** branche 21/23, sans cours accessible
- **Métal physique** (or, argent)

Le besoin est la performance, pas seulement le solde : montant investi, valeur actuelle, plus-value, et rendement quand il est calculable honnêtement.

## Décisions de cadrage

| Sujet | Décision |
|---|---|
| Granularité | Historique de mouvements là où l'API le donne (Trade Republic), position saisie pour la branche 21/23 et le métal |
| Valorisation | Automatique pour TR et le métal, manuelle pour l'assurance-vie |
| Place au Bilan | Écran séparé. Le Bilan, le Solde total et le burn-down ne sont pas modifiés |
| Rattachement | `Dashboard`, comme `SavingsGoal` et `ProjectEnvelope` |
| Titulaire | Champ texte `Holder` sur chaque ligne, pour combiner les patrimoines de Sébastien et d'Audrey |

### Pourquoi `Holder` en texte et non une FK vers `User`

Audrey n'a pas nécessairement de compte dans l'app. Une clé étrangère vers `User` empêcherait de saisir ses lignes. Un champ texte libre permet le groupement et le total combiné sans dépendre de l'existence d'un compte, et sans migration le jour où un troisième titulaire apparaît. L'UI propose en autocomplétion les valeurs déjà présentes sur le dashboard, ce qui limite les fautes de frappe sans figer la liste.

### Pourquoi un écran séparé

Les calculs du Bilan et du burn-down ont été fiabilisés en juillet 2026 après une série de corrections (provisions faussant la courbe du solde, réconciliation fragile, retraits d'épargne comptés en revenu). Y injecter une source de valeur neuve et non éprouvée rouvrirait ce risque. Le branchement au patrimoine consolidé reste possible plus tard : le modèle est conçu pour, il n'est simplement pas activé.

## Hors périmètre

- Modification du Bilan, du Solde total, du burn-down ou des courbes existantes
- Fiscalité (précompte mobilier, taxe sur les comptes-titres, plus-values imposables)
- Passage d'ordres ou toute action d'écriture vers un courtier
- Crypto-actifs
- Autres courtiers que Trade Republic

## Modèle de données

Trois entités, toutes rattachées au `Dashboard`.

### `Investment`

Une ligne détenue.

| Champ | Type | Note |
|---|---|---|
| `Id` | int | |
| `DashboardId` | int | FK, index |
| `Name` | string | ex. « iShares Core MSCI World », « Or 1 oz Maple Leaf » |
| `Holder` | string | Texte libre. L'UI propose les valeurs déjà utilisées sur le dashboard |
| `Kind` | enum | `Security`, `Metal`, `InsuranceContract` |
| `Isin` | string? | Titres cotés |
| `MetalCode` | string? | `XAU`, `XAG` |
| `Quantity` | decimal | Précision explicite requise, voir plus bas. Vaut 1 pour un contrat |
| `Unit` | enum | `Share`, `Gram`, `Ounce`, `Contract` |
| `CostBasis` | decimal | Total réellement versé, en euros |
| `FirstPurchaseDate` | DateTime? | Conditionne l'affichage du CAGR |
| `Source` | enum | `Manual`, `TradeRepublic` |
| `ExternalId` | string? | Identifiant côté TR, pour la réconciliation |
| `IsArchived` | bool | |
| `CreatedAt` | DateTime | |

`Kind` détermine le mode de valorisation. `Source` détermine qui écrit la donnée. Les deux sont volontairement séparés : une ligne saisie à la main aujourd'hui peut être reprise par TR demain sans changer de nature.

### `InvestmentValuation`

Une valeur datée. On n'écrase jamais, on empile.

| Champ | Type | Note |
|---|---|---|
| `Id` | int | |
| `InvestmentId` | int | FK, index |
| `AsOf` | DateTime | Date de la valeur, pas date de saisie |
| `UnitPrice` | decimal? | Cours unitaire quand connu |
| `MarketValue` | decimal | Valeur totale de la ligne, en euros |
| `Source` | enum | `Manual`, `TradeRepublic`, `SpotApi` |
| `CreatedAt` | DateTime | |

Contrainte unique sur `(InvestmentId, AsOf)`.

L'empilement est ce qui produit la courbe du patrimoine sans code supplémentaire, et ce qui évite qu'une correction de valeur réécrive rétroactivement l'historique. `AsOf` porte la date du relevé ou du cours, jamais celle du moment où l'utilisateur tape.

### `InvestmentMovement`

Achats, ventes, dividendes, frais. Alimentée par TR, saisissable à la main.

| Champ | Type | Note |
|---|---|---|
| `Id` | int | |
| `InvestmentId` | int | FK, index |
| `Type` | enum | `Buy`, `Sell`, `Dividend`, `Fee` |
| `Date` | DateTime | |
| `Quantity` | decimal | |
| `UnitPrice` | decimal | |
| `Amount` | decimal | Montant total signé, en euros |
| `ExternalId` | string? | Unique, déduplication à l'import (même pattern que `Transaction.ExternalId`) |
| `Source` | enum | `Manual`, `TradeRepublic` |

### Précision décimale

EF Core sur SQLite ne porte pas de précision native sur `decimal`. Les quantités fractionnaires de Trade Republic descendent à six décimales. La précision doit être configurée explicitement dans `OnModelCreating` pour `Quantity`, `UnitPrice`, `CostBasis` et `MarketValue`. Sans cela, la perte est silencieuse et ne se voit qu'à l'écart cumulé.

## Calculs de performance

Isolés dans un service dédié, sans accès direct au `DbContext` pour rester testables unitairement.

- **PRU** = `CostBasis / Quantity`. Non affiché pour `Kind = InsuranceContract`, où la quantité vaut 1 par convention et où le PRU se confondrait avec le montant versé sans rien apprendre
- **Valeur actuelle** = `MarketValue` de la `InvestmentValuation` la plus récente
- **Plus-value latente** = valeur actuelle moins `CostBasis`, exprimée en euros et en pourcentage
- **Quantité** = valeur saisie pour les lignes `Manual`, somme signée des mouvements pour les lignes `TradeRepublic`

### Rendement, et ce qu'on refuse d'afficher

Un rendement annualisé honnête exige des mouvements datés.

- Ligne avec historique de mouvements : TRI (money-weighted, méthode XIRR)
- Ligne sans historique mais avec `FirstPurchaseDate` : CAGR, explicitement étiqueté comme approximatif
- Ligne sans historique et sans date d'entrée : **aucun rendement affiché**, avec une invitation à renseigner la date

Une case vide est préférable à un chiffre reposant sur une hypothèse invisible. Ce point n'est pas négociable dans l'implémentation.

## Sources de valorisation

### Assurance-vie, manuelle

Saisie de la valeur du relevé, avec sa date de relevé. Crée une `InvestmentValuation` de source `Manual`. Aucune dépendance externe.

### Métal, cours spot

Deux conversions sont nécessaires quand la source cote en dollars l'once et que la détention se compte en grammes : conversion d'unité et conversion de devise. Une erreur y reste plausible à l'œil, donc invisible.

Exigences :

- La conversion (unité et devise) est une fonction pure isolée, couverte par des tests unitaires
- Préférer une source cotant directement en XAU/EUR, ce qui supprime la conversion de devise
- Un appel par jour suffit et tient dans les quotas gratuits usuels
- Le choix de la source se fait au moment de construire le lot 3, sur vérification des conditions réelles du service. Les informations disponibles à la rédaction de cette spec peuvent être périmées
- Échec d'appel : aucune `InvestmentValuation` n'est écrite. La dernière valeur connue reste affichée avec sa date

### Trade Republic, portefeuille

Le transport existe déjà : `TradeRepublicClient` gère le login SMS, les tokens chiffrés via DataProtection, et un mécanisme de souscription WebSocket générique (`SendSubscriptionAsync` / `ReadResponseAsync`) utilisé aujourd'hui pour les transactions carte.

L'API Trade Republic n'est ni publique ni documentée. Le nom exact du type de souscription au portefeuille et la forme du JSON retourné ne peuvent pas être affirmés à la rédaction de cette spec.

Le lot 4 commence donc par une phase d'exploration en session interactive, avec la session TR de Sébastien ouverte, pour observer la réponse réelle avant d'écrire le parsing. Le code de parsing n'est écrit qu'après.

Réconciliation par ISIN vers `Investment.ExternalId`, déduplication des mouvements par `ExternalId` unique.

### Fraîcheur

Chaque ligne affiche la date de sa dernière valorisation. Au-delà du seuil, la valeur s'affiche en grisé avec sa date apparente.

- Source `Manual` : seuil 30 jours
- Sources automatiques : seuil 48 heures

Une donnée périmée doit se voir périmée.

## Écran

Page `Investments`, suivant le pattern existant (`pages/`, `api/`, `hooks/`, `types/`).

- Tableau des lignes : nom, titulaire, quantité, PRU, valeur actuelle, plus-value en euros et en pourcentage, badge de fraîcheur
- Groupement basculable par titulaire ou par type d'actif, avec total consolidé en tête
- Courbe d'évolution du patrimoine (Recharts, déjà utilisé dans l'app), alimentée par les `InvestmentValuation`
- Formulaire de saisie et de mise à jour de valeur pour les lignes manuelles

Aucun écran existant n'est modifié.

## Autorisation

Même pattern que le reste de l'app : extraction du `UserId` depuis les claims JWT, puis validation de l'appartenance au dashboard avant toute lecture ou écriture. Les trois entités sont atteintes exclusivement via leur `DashboardId`, ce qui ferme la voie IDOR par construction.

## Tests

L'app ne dispose aujourd'hui que de tests Playwright E2E, sans test unitaire backend.

Cette fonctionnalité introduit un projet de tests unitaires backend, limité au service de calcul des investissements. Périmètre :

- PRU, plus-value, agrégation par titulaire et par type
- Conversion once vers gramme et dollar vers euro
- TRI sur un historique de mouvements connu
- Règles d'affichage du rendement, y compris les cas où rien ne doit s'afficher
- Calcul de fraîcheur aux bornes des seuils

Le reste du code existant n'est pas touché par cette introduction.

Un test E2E Playwright couvre le parcours de bout en bout : création d'une ligne manuelle, saisie d'une valorisation, affichage de la plus-value.

## Découpage en lots

Chaque lot laisse l'application utilisable et se reprend dans une session neuve. Contrainte de budget Claude Pro : un seul lot par séance.

**Lot 1. Modèle et saisie.** Entités, migration, précision décimale, service de calcul, CRUD backend, formulaire de saisie. À la fin : les lignes des trois familles peuvent être saisies à la main.

**Lot 2. Écran et performance.** Tableau, groupement, totaux consolidés, courbe, badges de fraîcheur. À la fin : le patrimoine est visible.

**Lot 3. Cours spot métal.** Choix et vérification de la source, conversion isolée et testée, job quotidien accroché au `BankSyncService` existant.

**Lot 4. Portefeuille Trade Republic.** Exploration en direct, souscription, parsing, réconciliation par ISIN, déduplication des mouvements.

## Risques

| Risque | Portée | Traitement |
|---|---|---|
| L'API Trade Republic casse sans préavis | Lot 4 | Badge de fraîcheur, échec silencieux qui n'écrase aucune valeur, saisie manuelle toujours possible en repli |
| Le nom de la souscription portefeuille est inconnu à ce jour | Lot 4 | Exploration en direct avant écriture du parsing |
| Erreur de conversion unité ou devise, plausible donc invisible | Lot 3 | Fonction pure isolée, tests unitaires, préférence pour une source XAU/EUR |
| Perte de précision décimale sur SQLite | Lot 1 | Configuration explicite dans `OnModelCreating` |
| Conditions des API de cours gratuites périmées | Lot 3 | Vérification au moment de construire le lot, pas à la rédaction de la spec |
