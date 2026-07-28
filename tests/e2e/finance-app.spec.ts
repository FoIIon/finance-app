import { test, expect, type Page } from '@playwright/test';

// Identifiants partagés entre les tests
const testEmail = `test-${Date.now()}@test.com`;
const testPassword = 'Test123456';
const testDescription = `Achat test ${Date.now()}`;
const updatedDescription = `Achat modifie ${Date.now()}`;

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

    // Vérifier la redirection vers le dashboard
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
    await page.getByText('Déconnexion').click();

    // Vérifier la redirection vers /login
    await page.waitForURL('**/login');
    await expect(page.getByPlaceholder('votre@email.com')).toBeVisible();

    // Se reconnecter avec les memes identifiants
    await page.getByPlaceholder('votre@email.com').fill(testEmail);
    await page.getByPlaceholder('••••••').fill(testPassword);
    await page.getByRole('button', { name: 'Se connecter' }).click();

    // Vérifier qu'on est de retour sur le dashboard
    await page.waitForURL('**/');
    await expect(page.getByRole('heading', { name: 'Tableau de bord' })).toBeVisible({ timeout: 10000 });
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
    await expect(page.getByText(testDescription)).toBeVisible({ timeout: 10000 });
  });

  test('Test 5 : Modifier une transaction', async () => {
    // On est déjà sur /transactions
    await expect(page.getByText(testDescription)).toBeVisible();

    // Cliquer sur le bouton modifier de la transaction
    const row = page.locator('tr', { hasText: testDescription });
    await row.locator('button', { hasText: '✏️' }).click();

    // Vérifier que le modal d'édition s'ouvre
    await expect(page.getByText('Modifier la transaction')).toBeVisible();

    // Changer la description
    const descriptionInput = page.locator('form input[type="text"]');
    await descriptionInput.clear();
    await descriptionInput.fill(updatedDescription);

    // Soumettre (le bouton submit du formulaire, pas le bouton aria-label="Modifier")
    await page.locator('form button[type="submit"]').click();

    // Vérifier que le modal se ferme
    await expect(page.getByText('Modifier la transaction')).not.toBeVisible({ timeout: 5000 });

    // Vérifier que la description est mise à jour
    await expect(page.getByText(updatedDescription)).toBeVisible({ timeout: 10000 });
  });

  test('Test 6 : Vérifier le dashboard', async () => {
    await page.goto('/');
    await page.waitForURL(/\/$/);

    // Vérifier que les cartes résumé sont affichées
    await expect(page.getByText('Solde total')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Revenus')).toBeVisible();
    await expect(page.getByText('Dépenses', { exact: true })).toBeVisible();

    // Vérifier que la section "Dernières transactions" existe
    await expect(page.getByText('Dernières transactions')).toBeVisible();
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
    await expect(page.getByText(updatedDescription)).toBeVisible({ timeout: 10000 });

    // Cliquer sur supprimer
    const row = page.locator('tr', { hasText: updatedDescription });
    await row.locator('button', { hasText: '🗑️' }).click();

    // Confirmer la suppression
    await row.locator('button', { hasText: 'Confirmer' }).click();

    // Vérifier que la transaction a disparu
    await expect(page.getByText(updatedDescription)).not.toBeVisible({ timeout: 5000 });
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
    await expect(row).toContainText('25.0');
  });
});
