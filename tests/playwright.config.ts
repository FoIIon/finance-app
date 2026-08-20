import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  retries: 0,
  use: {
    baseURL: 'http://localhost:5173',
    // La sidebar est en position fixe et pleine hauteur : sous 900px de haut,
    // le bouton de deconnexion sort du viewport et devient incliquable.
    viewport: { width: 1280, height: 1000 },
    headless: true,
    screenshot: 'only-on-failure',
  },
});
