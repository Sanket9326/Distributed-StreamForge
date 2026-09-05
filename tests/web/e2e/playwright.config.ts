import {
  defineConfig,
  devices,
} from "../../../src/web/node_modules/@playwright/test";

export default defineConfig({
  testDir: ".",
  testMatch: /.*\.spec\.ts/,
  timeout: 10 * 60_000,
  fullyParallel: false,
  use: {
    baseURL: process.env["STREAMFORGE_BASE_URL"] ?? "https://localhost:8443",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
