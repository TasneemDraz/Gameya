import { defineConfig, devices } from '@playwright/test';

/**
 * Smoke e2e. Expects the stack to be running already:
 *   docker compose up -d        (API on :8080; /api/dev/otp is mapped in Development and E2E)
 *   npm start                   (ng serve on :4200, proxying /api to :8080)
 * Run with: npm run e2e   (set PLAYWRIGHT_BROWSERS_PATH first if browsers live off the default path).
 */
export default defineConfig({
  testDir: './e2e',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    {
      name: 'mobile',
      use: { ...devices['Desktop Chrome'], viewport: { width: 360, height: 740 } },
    },
  ],
});
