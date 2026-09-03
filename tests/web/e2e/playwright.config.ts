import { defineConfig, devices } from '../../../src/web/node_modules/@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: /hls-playback\.spec\.ts/,
  timeout: 10 * 60_000,
  fullyParallel: false,
  use: { baseURL: 'http://localhost:8080', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
