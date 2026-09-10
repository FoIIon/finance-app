import { test, expect, type Page } from '@playwright/test';
import { DatabaseSync } from 'node:sqlite';
import { join } from 'node:path';

// L'inscription n'ouvre pas de session : le compte reste bloque tant que
// l'email n'est pas confirme. En test il n'y a pas de boite mail, on lit
// donc le jeton directement dans la base de developpement.
function confirmationTokenFor(email: string): string {
  const dbPath = join(__dirname, '..', '..', 'backend', 'FinanceApp.API', 'finance.db');
  const db = new DatabaseSync(dbPath, { readOnly: true });
  try {
    const row = db.prepare('SELECT EmailConfirmationToken AS t FROM Users WHERE Email = ?').get(email) as { t: string } | undefined;
    if (!row?.t) throw new Error(`Aucun jeton de confirmation pour ${email}`);
    return row.t;
  } finally {
    db.close();
  }
}

// Identifiants partagés entre les tests
const testEmail = `test-${Date.now()}@test.com`;
const testPassword = 'Test123456';
const testDescription = `Achat test ${Date.now()}`;
const updatedDescription = `Achat modifie ${Date.now()}`;
// Partagé entre les tests 10 et 11 : la ligne créée puis valorisée sert au parcours détail
const investmentName = `ETF World ${Date.now()}`;

test.describe.serial('FinanceApp E2E', () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    page = await browser.newPage();
  });

  test.afterAll(async () => {
    await page.close();
  });

  test('Test 1 : Page de login accessible', async () => {
    await page.goto('/login');
    await page.waitForURL('**/login');

    // Vérifier que le formulaire email + password est visible
    const emailInput = page.getByPlaceholder('votre@email.com');
    await expect(emailInput).toBeVisible();

    const passwordInput = page.getByPlaceholder('••••••');
    await expect(passwordInput).toBeVisible();

    // Vérifier que le bouton de connexion existe
    await expect(page.getByRole('button', { name: 'Se connecter' })).toBeVisible();

    // Vérifier que le lien "Créer un compte" existe
    await expect(page.getByRole('link', { name: 'Créer un compte' })).toBeVisible();
  });

  test("Test 2 : Inscription d'un nouvel utilisateur", async () => {
    await page.goto('/register');
    await page.waitForURL('**/register');

    // Remplir le formulaire
    await page.getByPlaceholder('votre@email.com').fill(testEmail);

    // Il y a deux champs mot de passe avec le meme placeholder
    const passwordFields = page.getByPlaceholder('••••••••');
    await passwordFields.nth(0).fill(testPassword);
    await passwordFields.nth(1).fill(testPassword);

    // Soumettre
    await page.getByRole('button', { name: 'Créer un compte' }).click();

    // L'inscription ne connecte pas : elle renvoie sur l'ecran d'attente de confirmation
    await page.waitForURL('**/register-success**');

    // Confirmer l'email avec le jeton emis par le backend, puis se connecter.
    // Attendre la confirmation effective avant de quitter l'ecran : partir sur /login
    // des le chargement annulait la requete en vol et le compte restait non confirme.
    // Le lien « Se connecter » n'existe que dans la branche succes, l'attendre vaut
    // donc assertion. Le double appel de StrictMode ne fausse plus rien : la page se
    // garde d'une seconde tentative et le serveur repond 200 sur un compte deja confirme.
    await page.goto(`/confirm-email?token=${confirmationTokenFor(testEmail)}`);
    await expect(page.getByRole('link', { name: 'Se connecter' })).toBeVisible({ timeout: 10000 });
    await page.getByRole('link', { name: 'Se connecter' }).click();
    await page.waitForURL('**/login');
    await page.getByPlaceholder('votre@email.com').fill(testEmail);
    await page.getByPlaceholder('••••••').fill(testPassword);
    await page.getByRole('button', { name: 'Se connecter' }).click();

    await page.waitForURL('**/');
    expect(page.url()).not.toContain('/login');
    expect(page.url()).not.toContain('/register');

    // Vérifier que la sidebar/navigation est visible
    await expect(page.getByRole('link', { name: /Tableau de bord/ })).toBeVisible({ timeout: 10000 });
    await expect(page.getByRole('link', { name: /Transactions/ })).toBeVisible();
    await expect(page.getByRole('link', { name: /Catégories/ })).toBeVisible();
  });

  test('Test 3 : Déconnexion puis reconnexion', async () => {
    // Cliquer sur le bouton déconnexion
    // Le layout rend deux sidebars (desktop et mobile) : cibler le role evite
    // l'ambiguite, la copie mobile etant masquee et hors de l'arbre d'accessibilite
    await page.getByRole('button', { name: 'Déconnexion' }).click();

    // Vérifier la redirection vers /login
    await page.waitForURL('**/login');
    await expect(page.getByPlaceholder('votre@email.com')).toBeVisible();

    // Se reconnecter avec les memes identifiants
    await page.getByPlaceholder('votre@email.com').fill(testEmail);
    await page.getByPlaceholder('••••••').fill(testPassword);
    await page.getByRole('button', { name: 'Se connecter' }).click();

    // Vérifier qu'on est de retour sur le dashboard
    // La racine ouvre l'Agenda depuis le lot 2 (09/09/2026) : le tableau de bord se rejoint explicitement.
    await page.waitForURL('**/agenda');
    await page.goto('/dashboard/overview');
    await page.waitForURL('**/dashboard/overview');
    await expect(page.getByRole('heading', { name: 'Dernières transactions' })).toBeVisible({ timeout: 10000 });
  });

  test('Test 4 : Créer une transaction', async () => {
    await page.goto('/transactions');
    await page.waitForURL('**/transactions');

    // Attendre que la page soit chargée
    await expect(page.getByText('Transactions').first()).toBeVisible({ timeout: 10000 });

    // Cliquer sur "Ajouter"
    await page.getByRole('button', { name: '+ Ajouter' }).click();

    // Vérifier que le modal s'ouvre
    await expect(page.getByText('Nouvelle transaction')).toBeVisible();

    // Remplir le formulaire
    // Type : Dépense est déjà sélectionné par défaut

    // Montant
    const amountInput = page.locator('input[type="number"]');
    await amountInput.fill('42.50');

    // Description
    const descriptionInput = page.locator('form input[type="text"]');
    await descriptionInput.fill(testDescription);

    // Date : garder la date par défaut (aujourd'hui)

    // Catégorie : sélectionner la première catégorie disponible (pas "Sélectionner...")
    const categorySelect = page.locator('form select').last();
    await categorySelect.waitFor({ state: 'visible' });
    // Attendre que les options soient chargées (plus que juste "Sélectionner...")
    await page.waitForFunction(() => {
      const selects = document.querySelectorAll('form select');
      const catSelect = selects[selects.length - 1];
      return catSelect && catSelect.querySelectorAll('option').length > 1;
    });
    // Sélectionner la deuxième option (la première vraie catégorie)
    const options = await categorySelect.locator('option:not([disabled])').all();
    if (options.length > 0) {
      const value = await options[0].getAttribute('value');
      if (value) await categorySelect.selectOption(value);
    }

    // Soumettre
    await page.getByRole('button', { name: 'Ajouter', exact: true }).click();

    // Vérifier que le modal se ferme
    await expect(page.getByText('Nouvelle transaction')).not.toBeVisible({ timeout: 5000 });

    // Vérifier que la transaction apparait dans le tableau
    await expect(page.locator('table').getByText(testDescription)).toBeVisible({ timeout: 10000 });
  });

  test('Test 5 : Modifier une transaction', async () => {
    // On est déjà sur /transactions
    await expect(page.locator('table').getByText(testDescription)).toBeVisible();

    // L'edition se fait en ligne, pas dans une modale : le crayon transforme la
    // ligne en formulaire, que l'on valide par la coche.
    const row = page.locator('tr', { hasText: testDescription });
    await row.getByRole('button', { name: 'Édition rapide' }).click();

    // La ligne en cours d'edition ne porte plus l'ancien libelle, il est passe
    // dans un champ : on la retrouve par la presence de ce champ.
    const editingRow = page.locator('tbody tr').filter({ has: page.locator('input[type="text"]') });
    const descriptionInput = editingRow.locator('input[type="text"]');
    await expect(descriptionInput).toBeVisible();
    await descriptionInput.fill(updatedDescription);

    await editingRow.getByRole('button', { name: '✓' }).click();

    // Le champ d'edition disparait une fois la ligne enregistree
    await expect(descriptionInput).not.toBeVisible({ timeout: 5000 });

    // Vérifier que la description est mise à jour
    await expect(page.locator('table').getByText(updatedDescription)).toBeVisible({ timeout: 10000 });
  });

  test('Test 6 : Vérifier le dashboard', async () => {
    await page.goto('/');
    // La racine ouvre l'Agenda depuis le lot 2 (09/09/2026) : le tableau de bord se rejoint explicitement.
    await page.waitForURL('**/agenda');
    await page.goto('/dashboard/overview');
    await page.waitForURL('**/dashboard/overview');

    // Vérifier que les cartes résumé sont affichées
    await expect(page.getByText('Solde global')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Revenus ce mois-ci')).toBeVisible();
    await expect(page.getByText('Dépenses ce mois-ci')).toBeVisible();

    // Vérifier que la section "Dernières transactions" existe
    await expect(page.getByRole('heading', { name: 'Dernières transactions' })).toBeVisible();
  });

  test('Test 7 : Page catégories', async () => {
    await page.goto('/categories');
    await page.waitForURL('**/categories');

    // Vérifier que les catégories par défaut sont listées
    await expect(page.getByText('Alimentation')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Transport')).toBeVisible();

    // Cliquer sur "Ajouter"
    await page.getByRole('button', { name: '+ Ajouter' }).click();

    // Vérifier que le formulaire inline apparait
    await expect(page.getByPlaceholder('Nom de la catégorie')).toBeVisible();

    // Créer une catégorie custom
    await page.getByPlaceholder('Nom de la catégorie').fill('Test QA');
    await page.getByPlaceholder('🎯').fill('🧪');

    // Soumettre
    await page.getByRole('button', { name: 'Créer' }).click();

    // Vérifier qu'elle apparait dans la liste
    await expect(page.getByText('Test QA')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Personnalisée')).toBeVisible();

    // Supprimer la catégorie custom
    // Trouver la carte de la catégorie "Test QA" et cliquer sur le bouton supprimer
    const categoryCard = page.locator('div', { hasText: 'Test QA' }).filter({ hasText: 'Personnalisée' });
    await categoryCard.locator('button', { hasText: '🗑️' }).click();

    // Confirmer la suppression (bouton "Oui")
    await categoryCard.locator('button', { hasText: 'Oui' }).click();

    // Vérifier qu'elle a disparu
    await expect(page.getByText('Test QA')).not.toBeVisible({ timeout: 5000 });
  });

  test('Test 8 : Supprimer une transaction', async () => {
    await page.goto('/transactions');
    await page.waitForURL('**/transactions');

    // Vérifier que la transaction modifiée existe
    await expect(page.locator('table').getByText(updatedDescription)).toBeVisible({ timeout: 10000 });

    // Cliquer sur supprimer
    const row = page.locator('tr', { hasText: updatedDescription });
    await row.locator('button', { hasText: '🗑️' }).click();

    // Confirmer la suppression
    await row.locator('button', { hasText: 'Confirmer' }).click();

    // Vérifier que la transaction a disparu
    await expect(page.locator('table').getByText(updatedDescription)).not.toBeVisible({ timeout: 5000 });
  });

  test('Test 9 : Routes protégées', async () => {
    // Supprimer le token du localStorage
    await page.evaluate(() => {
      localStorage.removeItem('token');
      localStorage.removeItem('email');
    });

    // Naviguer vers la page d'accueil
    await page.goto('/');

    // Vérifier la redirection vers /login
    await page.waitForURL('**/login', { timeout: 10000 });
    await expect(page.getByPlaceholder('votre@email.com')).toBeVisible();
  });

  test('Test 10 : Investissement créé, valorisé, plus-value affichée', async () => {
    // Le test précédent a vidé le token pour vérifier la protection des routes,
    // on est donc revenu sur /login. On se reconnecte avant de continuer la série.
    await page.getByPlaceholder('votre@email.com').fill(testEmail);
    await page.getByPlaceholder('••••••').fill(testPassword);
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await page.waitForURL('**/');

    await page.goto('/investments');
    await page.waitForURL('**/investments');

    // Le formulaire de saisie manuelle est désormais replié : c'est l'action la plus rare
    // de l'écran depuis que l'import Trade Republic existe.
    await page.getByRole('button', { name: 'Ajouter une ligne à la main' }).click();

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
    // Notation française depuis le 25/08/2026 : la virgule décimale, comme le reste de la page.
    await expect(row).toContainText('25,0');
  });

  test('Test 11 : Résumé du portefeuille et détail de ligne', async () => {
    // On est toujours sur /investments après le test 10. Le gros chiffre du résumé
    // doit refléter la valorisation saisie : un montant non nul.
    const total = page.getByLabel('Valeur totale du portefeuille');
    await expect(total).toBeVisible();
    await expect(total).not.toHaveText(/^0[,.]00/);

    // Le clic sur le nom de la ligne ouvre le panneau de détail
    await page.getByRole('button', { name: investmentName }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    // Le panneau ne liste plus les valorisations une à une depuis le 25/08/2026 : il porte
    // l'identité de la ligne, son montant investi, la courbe et le choix de la période.
    // \s couvre l'espace fine insécable que Intl place comme séparateur de milliers.
    await expect(dialog).toContainText(investmentName);
    // La valorisation saisie au test 10, pas seulement le montant investi : sans elle
    // l'assertion passerait aussi sur un panneau vide de toute donnée.
    await expect(dialog).toContainText(/1\s250,00/);
    await expect(dialog).toContainText(/1\s000,00/);
    await expect(dialog.getByRole('group', { name: 'Période' })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'YTD' })).toBeVisible();

    // Une seule valorisation ne fait pas une courbe, et le panneau le dit au lieu
    // d'afficher un trait plat trompeur.
    await expect(dialog).toContainText('Pas assez de valorisations');

    // Fermeture du détail
    await dialog.getByRole('button', { name: 'Fermer' }).click();
    await expect(dialog).not.toBeVisible();
  });

  test('Test 12 : Agenda, route par défaut, navigation et menu mobile', async () => {
    // La racine mène désormais à l'agenda, l'écran qu'Audrey ouvre le matin.
    await page.goto('/');
    await page.waitForURL('**/agenda**');

    // Le jour courant est rendu par le serveur (isToday) : en vue semaine il est toujours là.
    // Aucune donnée de calendrier ni d'échéance n'est requise, le bloc existe même vide.
    await expect(page.getByRole('heading', { name: /^Aujourd'hui/ })).toBeVisible({ timeout: 10000 });

    // Hors période courante seulement : en vue semaine sur aujourd'hui, pas de bouton « Aujourd'hui ».
    const todayButton = page.getByRole('button', { name: "Aujourd'hui", exact: true });
    await expect(todayButton).toHaveCount(0);

    // La vue vit dans l'URL, pour le retour arrière et le partage d'un lien.
    await page.getByRole('button', { name: 'Mois', exact: true }).click();
    await page.waitForURL(/view=month/);

    // Le mois suivant ne contient pas aujourd'hui : le bouton apparaît, et le ramène.
    await page.getByRole('button', { name: 'Période suivante' }).click();
    await page.waitForURL(/anchor=\d{4}-\d{2}-\d{2}/);
    await expect(todayButton).toBeVisible();
    await todayButton.click();
    await expect(page).not.toHaveURL(/anchor=/);
    await expect(todayButton).toHaveCount(0);

    // Sur un iPhone (390 × 664), la sidebar défile : « Déconnexion » reste atteignable au bas du menu.
    await page.setViewportSize({ width: 390, height: 664 });
    await page.getByRole('button', { name: 'Ouvrir le menu' }).click();
    const logout = page.getByRole('button', { name: 'Déconnexion' });
    await logout.scrollIntoViewIfNeeded();
    await expect(logout).toBeInViewport();

    // Aucun défilement horizontal à 390 px.
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);

    await page.getByRole('button', { name: 'Fermer le menu' }).click();
    await page.setViewportSize({ width: 1280, height: 1000 });
  });

  test('Test 13 : Échéance créée, document déposé, doublon refusé, paiement, modification, suppression du document', async () => {
    // Lot 1 (10/09/2026) : les trois gestes d'Audrey, depuis l'Agenda puis depuis /documents.
    await page.goto('/agenda');
    await page.waitForURL('**/agenda**');
    await expect(page.getByRole('heading', { name: /^Aujourd'hui/ })).toBeVisible({ timeout: 10000 });

    // Création : libellé, date limite à J+3, montant saisi à la française. La virgule part en point.
    const due = new Date();
    due.setDate(due.getDate() + 3);
    const dueIso = `${due.getFullYear()}-${String(due.getMonth() + 1).padStart(2, '0')}-${String(due.getDate()).padStart(2, '0')}`;

    await page.getByRole('button', { name: '+ Échéance' }).click();
    const form = page.getByRole('dialog', { name: 'Nouvelle échéance' });
    await expect(form).toBeVisible();
    await form.getByLabel('Libellé').fill('Test E2E facture');
    await form.getByLabel('Date limite').fill(dueIso);
    await form.getByLabel(/Montant/).fill('12,50');
    await form.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(form).not.toBeVisible({ timeout: 5000 });

    // À J+3 l'échéance est dans la semaine affichée ou dans « Et ensuite » : dans les deux cas une ligne
    // touchable, avec le montant en notation française. Le statut vient du serveur, on ne le vérifie pas ici.
    const row = page.getByRole('button', { name: /Test E2E facture/ });
    await expect(row).toBeVisible({ timeout: 10000 });
    await expect(row).toContainText(/12,50\s€/);

    // Dépôt d'un PDF généré (aucune donnée réelle) depuis /documents, sans échéance.
    await page.goto('/documents');
    await page.waitForURL('**/documents**');
    await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible({ timeout: 10000 });
    await page.getByRole('button', { name: 'Déposer', exact: true }).click();
    const upload = page.getByRole('dialog', { name: 'Déposer un document' });
    await expect(upload).toBeVisible();
    await upload.locator('input[type="file"]').setInputFiles(join(__dirname, 'fixtures', 'facture-test.pdf'));
    await expect(upload).toContainText('facture-test.pdf');
    await upload.getByRole('button', { name: 'Envoyer' }).click();
    await expect(upload).not.toBeVisible({ timeout: 10000 });

    // La carte est là, sous la pastille de l'année en cours (année fiscale par défaut du dépôt).
    const card = page.locator('li', { hasText: 'facture-test.pdf' });
    await expect(card).toBeVisible({ timeout: 10000 });
    await expect(card).toContainText('Facture');
    await expect(page.getByRole('button', { name: String(new Date().getFullYear()), pressed: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Créer une échéance à partir de ce document' })).toBeVisible();

    // Second dépôt du même fichier : le serveur répond 409 (même SHA-256 dans le dashboard), son message
    // s'affiche tel quel avec le geste vers l'existant, et aucune seconde carte n'apparaît.
    await page.getByRole('button', { name: 'Déposer', exact: true }).click();
    const again = page.getByRole('dialog', { name: 'Déposer un document' });
    await expect(again).toBeVisible();
    await again.locator('input[type="file"]').setInputFiles(join(__dirname, 'fixtures', 'facture-test.pdf'));
    await again.getByRole('button', { name: 'Envoyer' }).click();
    await expect(again).toContainText('déjà rangé', { timeout: 10000 });
    await expect(again.getByRole('button', { name: 'Ouvrir le document existant' })).toBeVisible();
    await again.getByRole('button', { name: 'Fermer' }).click();
    await expect(again).not.toBeVisible();
    await expect(page.locator('li', { hasText: 'facture-test.pdf' })).toHaveCount(1);

    // Paiement depuis la feuille Échéance, ouverte depuis l'agenda. « Payée » est écrit par le serveur.
    await page.goto('/agenda');
    await page.waitForURL('**/agenda**');
    await page.getByRole('button', { name: /Test E2E facture/ }).click();
    const sheet = page.getByRole('dialog', { name: 'Test E2E facture' });
    await expect(sheet).toBeVisible();
    await expect(sheet.getByRole('heading', { name: 'Documents' })).toBeVisible();
    await sheet.getByRole('button', { name: "Je l'ai payée" }).click();
    await expect(sheet).toContainText(/Payée/, { timeout: 10000 });

    // « Modifier » : la feuille de saisie prend la place de la feuille Échéance, pré-remplie, puis la rend
    // avec le nouveau libellé. L'agenda le reprend après invalidation.
    await sheet.getByRole('button', { name: 'Modifier' }).click();
    const edit = page.getByRole('dialog', { name: "Modifier l'échéance" });
    await expect(edit).toBeVisible();
    await expect(edit.getByLabel('Libellé')).toHaveValue('Test E2E facture');
    await edit.getByLabel('Libellé').fill('Test E2E facture modifiée');
    await edit.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(edit).not.toBeVisible({ timeout: 5000 });
    const renamed = page.getByRole('dialog', { name: 'Test E2E facture modifiée' });
    await expect(renamed).toBeVisible({ timeout: 10000 });
    await renamed.getByRole('button', { name: 'Fermer' }).click();
    await expect(renamed).not.toBeVisible();
    await expect(page.getByRole('button', { name: /Test E2E facture modifiée/ })).toBeVisible({ timeout: 10000 });

    // Suppression du document en deux gestes : le bouton, puis « Oui ».
    await page.goto('/documents');
    await page.waitForURL('**/documents**');
    const cardAgain = page.locator('li', { hasText: 'facture-test.pdf' });
    await expect(cardAgain).toBeVisible({ timeout: 10000 });
    await cardAgain.getByRole('button', { name: 'Supprimer' }).click();
    await cardAgain.getByRole('button', { name: 'Oui' }).click();
    await expect(cardAgain).not.toBeVisible({ timeout: 5000 });

    // Aucun défilement horizontal à 390 px sur /documents non plus.
    await page.setViewportSize({ width: 390, height: 664 });
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
    await page.setViewportSize({ width: 1280, height: 1000 });
  });
});
