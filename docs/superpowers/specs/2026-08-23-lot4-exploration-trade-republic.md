# Lot 4 Trade Republic — journal d'exploration (2026-08-23)

Statut : **exploration en cours, code non commité.** Le login v2 fonctionne. La forme JSON du portefeuille n'est pas encore capturée (session expirée avant la sonde). Reprendre avec un login frais.

## Ce qui a changé chez Trade Republic depuis la spec (2026-07-28)

La spec supposait le flux v1 (code SMS à 4 chiffres) encore vivant. Il est mort.

- `POST /api/v1/auth/web/login` → **405** (TR a posé AWS WAF devant, et déprécié le v1).
- Le flux actuel est **v2 par approbation push** : plus de code, l'utilisateur approuve dans l'app mobile.
- `POST /api/v1/auth/web/refresh` → **405** aussi. L'ancien refresh est mort.

## Le login v2, résolu

Séquence qui marche (implémentée dans `TradeRepublicClient`) :

1. `POST /api/v2/auth/web/login` avec `{phoneNumber, pin}` et les headers v2 obligatoires (sinon `MISSING_REQUIRED_HEADER`) :
   - `x-tr-platform: web`
   - `x-tr-app-version` (15.7.0, configurable via `TradeRepublic:WebAppVersion`)
   - `x-tr-device-info` : base64 d'un JSON contenant un `stableDeviceId` (64 hex), configurable via `TradeRepublic:DeviceId`
   - plus les headers navigateur (Origin, Referer, Sec-Fetch-*)
   - Réponse : `{"processId":"..."}`
2. L'utilisateur approuve la notification dans l'app TR.
3. `GET /api/v2/auth/web/login/processes/{processId}` en boucle → à l'approbation, TR pose `tr_session`, `tr_refresh`, `tr_device` dans Set-Cookie. (Implémenté : `PollLoginApprovalV2Async`.)

**Bonne nouvelle** : depuis l'IP de Sébastien (Belgique, résidentielle), TR n'a pas déclenché le défi WAF. On a obtenu 200 sans `x-aws-waf-token`. Si un jour le WAF se réveille, il faudra un token obtenu via navigateur headless (Playwright), ce qu'on veut éviter.

## La forme du portefeuille : ce qu'on sait

Tout passe par le **WebSocket** (`wss://api.traderepublic.com/`). Les chemins REST portefeuille testés renvoient tous 404 nginx.

Protocole WS existant (déjà dans le client) : `connect 31 {...}`, puis `sub {id} {json}`, réponses préfixées `{id} A {json}` (succès) ou `{id} E {json}` (erreur).

**Topics : la réponse d'erreur discrimine leur validité.**

| Topic testé | Réponse | Lecture |
|---|---|---|
| `portfolioStatus` | AUTHENTICATION_ERROR | **valide**, à authentifier |
| `availableCash` | AUTHENTICATION_ERROR | **valide** |
| `cash` | AUTHENTICATION_ERROR | **valide** |
| `compactPortfolioByType` | AUTHENTICATION_ERROR | **valide** |
| `timelineTransactions` | AUTHENTICATION_ERROR | **valide** |
| `watchlists` | AUTHENTICATION_ERROR | **valide** |
| `compactPortfolio` | BAD_SUBSCRIPTION_TYPE | mort, renommé |
| `portfolio` | BAD_SUBSCRIPTION_TYPE | mort |
| `portfolioAggregateHistoryLight` | BAD_SUBSCRIPTION_TYPE | mauvais nom |

**Authentification des topics** : chaque souscription doit porter le session token → `{"type":"portfolioStatus","token":"<tr_session>"}`. Sans lui : « No auth token ». Avec un token expiré : « Unauthorized ». (Le harnais injecte le token, vérifié : l'erreur passe de l'un à l'autre.)

## Ce qui bloque la capture du JSON

Le session token TR vit **~5 minutes**. Entre le login et la sonde, avec les rebuilds, il a expiré → « Unauthorized » partout. Il faut sonder **dans la foulée du login**, et surtout implémenter un **refresh v2 fonctionnel**, l'ancien étant mort (405).

Piste refresh (pytr, tr-api) : keepalive par `GET /api/v1/auth/web/session` toutes les ~290 s, qui fait tourner les cookies. À vérifier en v2. Alternative : passer le session token dans le message `connect` du WebSocket.

Autre piège rencontré : le **sync de démarrage** (`SyncTradeRepublicAsync`) tente l'ancien refresh (405) et bascule la connexion en statut **Error**, ce qui la retire du champ des sondes. Contourné en tolérant Error dans le harnais. À corriger proprement au lot 4 (le sync doit utiliser le nouveau mécanisme).

## Prochaine session (login frais obligatoire)

1. Relancer le backend en dev, relogin TR (approbation push, ~30 s).
2. **Immédiatement** sonder `portfolioStatus`, `compactPortfolioByType`, `availableCash`, `cash` avec le token frais → capturer le JSON réel.
3. Écrire le parsing sur la forme observée (positions par ISIN, quantités, valeur, liquidités).
4. Implémenter le refresh v2 (keepalive `/api/v1/auth/web/session` ou token dans `connect`), remplacer l'appel mort dans `SyncTradeRepublicAsync`.
5. Réconciliation par ISIN vers `Investment.ExternalId`, déduplication des mouvements par `ExternalId` (comme prévu à la spec).

## Fichiers touchés (non commités)

- `Services/TradeRepublicClient.cs` : login v2 (headers, poll approbation), sondes REST/WS d'exploration.
- `Controllers/TradeRepublicExplorationController.cs` : **nouveau**, harnais `/api/banking/traderepublic/probe`, réservé Development, injecte le token dans les souscriptions.
- `Controllers/BankingController.cs` : verify branché sur le poll v2.
- `DTOs/BankingDtos.cs` : `Code` rendu optionnel (le v2 n'en a plus).
- `frontend/src/pages/Bank.tsx` : étape 2FA remplacée par « approuvez dans l'app, puis Continuer ».

Le harnais d'exploration (`TradeRepublicExplorationController`, `ProbeRestAsync`, `ProbeSubscriptionAsync`) est du code jetable, à retirer ou garder sous garde Development une fois le parsing écrit.
