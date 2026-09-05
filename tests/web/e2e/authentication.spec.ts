import { test, expect } from "../../../src/web/node_modules/@playwright/test";
import { join } from "node:path";

test.describe("HTTPS browser sessions", () => {
  test.skip(
    process.env["STREAMFORGE_E2E"] !== "1",
    "Set STREAMFORGE_E2E=1 with the HTTPS topology running.",
  );

  test("registers, restores a persistent session, logs out, and guards uploads", async ({
    page,
    context,
    browser,
  }) => {
    const name = `auth${Date.now()}`;
    await page.goto("/upload");
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fupload/);
    await expect(
      page.getByRole("button", { name: "Forgot password?" }),
    ).toBeDisabled();
    await page.screenshot({
      path: join(__dirname, "../../../artifacts/screenshots/login.png"),
      fullPage: true,
    });
    await page
      .getByRole("link", { name: "Create an account", exact: true })
      .click();
    await page.screenshot({
      path: join(__dirname, "../../../artifacts/screenshots/register.png"),
      fullPage: true,
    });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({
      path: join(
        __dirname,
        "../../../artifacts/screenshots/register-mobile.png",
      ),
      fullPage: true,
    });
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    ).toBe(true);
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.getByLabel("Username", { exact: true }).fill(name);
    await page
      .getByLabel("Email", { exact: true })
      .fill(`${name}@example.test`);
    await page
      .getByLabel("Password", { exact: true })
      .fill("browser test long password");
    await page
      .getByLabel("Confirm password", { exact: true })
      .fill("browser test long password");
    await page
      .getByRole("button", { name: "Create account", exact: true })
      .click();
    await expect(page).toHaveURL(/\/upload$/, { timeout: 30_000 });
    const session = (await context.cookies()).find(
      (cookie) => cookie.name === "__Host-streamforge-session",
    )!;
    expect(session.secure).toBe(true);
    expect(session.httpOnly).toBe(true);
    expect(session.sameSite).toBe("Strict");
    expect(session.expires).toBeGreaterThan(Date.now() / 1000 + 86000);
    expect(await page.evaluate(() => document.cookie)).not.toContain(
      "__Host-streamforge-session",
    );
    const restarted = await browser.newContext({
      storageState: await context.storageState(),
    });
    const restored = await restarted.newPage();
    await restored.goto(new URL("/upload", page.url()).href);
    await expect(
      restored.getByRole("heading", { name: "Share a video" }),
    ).toBeVisible();
    await restarted.close();
    await page.getByRole("button", { name: "Log out", exact: true }).click();
    await expect(page).toHaveURL(/\/$/);
    await page.goto("/upload");
    await expect(page).toHaveURL(/\/login/);
  });

  test("an invalidated server session redirects an upload without replaying it", async ({
    page,
    context,
  }) => {
    // A correctly formatted but nonexistent secret exercises server-side invalidation.
    await page.goto("/");
    await context.addCookies([
      {
        name: "__Host-streamforge-session",
        value: "a".repeat(43),
        url: new URL("/", page.url()).href,
        secure: true,
        httpOnly: true,
        sameSite: "Strict",
      },
    ]);
    await page.goto("/upload");
    await expect(page).toHaveURL(/\/login/);
    expect(
      (await context.cookies()).find(
        (cookie) => cookie.name === "__Host-streamforge-session",
      ),
    ).toBeUndefined();
    await page.goto("/");
    await expect(
      page.getByRole("link", { name: "StreamForge home" }),
    ).toBeVisible();
  });
});
