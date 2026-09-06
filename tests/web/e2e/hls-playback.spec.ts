import { execFileSync } from "node:child_process";
import { mkdirSync, rmSync } from "node:fs";
import { join } from "node:path";
import { test, expect } from "../../../src/web/node_modules/@playwright/test";

test.describe("full Compose adaptive HLS playback", () => {
  test.skip(
    process.env["STREAMFORGE_E2E"] !== "1",
    "Set STREAMFORGE_E2E=1 after starting the Compose topology.",
  );
  const artifacts = join(__dirname, ".generated");
  const media = join(artifacts, "synthetic.mp4");

  test.beforeAll(() => {
    mkdirSync(artifacts, { recursive: true });
    const args = [
      "-hide_banner",
      "-loglevel",
      "error",
      "-y",
      "-f",
      "lavfi",
      "-i",
      "testsrc2=size=1920x1080:rate=30",
      "-f",
      "lavfi",
      "-i",
      "sine=frequency=1000:sample_rate=48000",
      "-t",
      "24",
      "-c:v",
      "libx264",
      "-pix_fmt",
      "yuv420p",
      "-c:a",
      "aac",
    ];
    try {
      execFileSync("ffmpeg", [...args, media]);
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
      execFileSync("docker", [
        "run",
        "--rm",
        "--entrypoint",
        "ffmpeg",
        "--mount",
        `type=bind,source=${artifacts},target=/media`,
        "streamforge-transcoding-service",
        ...args,
        "/media/synthetic.mp4",
      ]);
    }
  });
  test.afterAll(() => rmSync(artifacts, { recursive: true, force: true }));

  test("uploads, adapts, switches quality, and falls back when HLS is unavailable", async ({
    page,
    context,
  }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto("/register?returnUrl=%2Fupload");
    const name = `hls${Date.now()}`;
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
    await page.goto("/upload");
    const title = `HLS E2E ${Date.now()}`;
    await page.getByLabel("Title").fill(title);
    await page.locator("#video-file").setInputFiles(media);
    await page.getByRole("button", { name: /Upload video/ }).click();
    await expect(page.getByText("Video stored and queued")).toBeVisible();
    await expect(page.getByText("Your video is ready")).toBeVisible({
      timeout: 8 * 60_000,
    });

    const requested: string[] = [];
    page.on("request", (request) => {
      if (request.url().includes(".m4s")) requested.push(request.url());
    });
    const cdp = await context.newCDPSession(page);
    await cdp.send("Network.enable");
    // startLevel=0 guarantees the first 360p segment. Keep page and preview loads
    // unthrottled so they do not distort the watch player's bandwidth estimate.
    await page.goto("/");
    await page.getByRole("button", { name: `Watch ${title}`, exact: true }).click();
    const player = page.getByLabel(`Play ${title}`, { exact: true });
    const qualifiedView = page.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        /\/api\/engagement\/videos\/.+\/views$/.test(new URL(response.url()).pathname),
      { timeout: 30_000 },
    );
    await player.evaluate((video: HTMLVideoElement) => video.play());
    await qualifiedView;
    await expect
      .poll(() => requested.some((url) => url.includes("/360p/")), { timeout: 30_000 })
      .toBeTruthy();
    expect(requested.every((url) => url.startsWith("https://"))).toBe(true);
    await cdp.send("Network.emulateNetworkConditions", {
      offline: false,
      latency: 10,
      downloadThroughput: 10_000_000,
      uploadThroughput: 1_000_000,
    });
    await expect
      .poll(() => requested.some((url) => /\/(720p|1080p)\//.test(url)), {
        timeout: 60_000,
      })
      .toBeTruthy();

    await page.getByRole("button", { name: /Auto/ }).click();
    const before = requested.length;
    await page.getByRole("menuitemradio", { name: "720p" }).click();
    await expect
      .poll(() => requested.slice(before).some((url) => url.includes("/720p/")), { timeout: 30_000 })
      .toBeTruthy();

    const like = page.getByRole("button", { name: "Like video", exact: true });
    await like.click();
    await expect(like).toHaveAttribute("aria-pressed", "true");
    await page.reload();
    await expect(page.getByRole("button", { name: "Like video", exact: true })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    const comment = `Engagement E2E ${Date.now()}`;
    await page.getByPlaceholder("Add a comment…").fill(comment);
    await page.getByRole("button", { name: "Comment", exact: true }).click();
    await expect(page.getByText(comment, { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    const edited = `${comment} edited`;
    await page.locator(".comment textarea").fill(edited);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByText(edited, { exact: true })).toBeVisible();
    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Delete", exact: true }).click();
    await expect(page.getByText(edited, { exact: true })).toHaveCount(0);

    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.evaluate(() =>
      Object.defineProperty(navigator, "share", { configurable: true, value: undefined }),
    );
    await page.getByRole("button", { name: "Share", exact: true }).click();
    await expect(page.getByText("Link copied", { exact: true })).toBeVisible();
    const sharedUrl = await page.evaluate(() => navigator.clipboard.readText());
    expect(new URL(sharedUrl).pathname).toMatch(/^\/watch\/[0-9a-f-]+$/);
    expect(sharedUrl).not.toContain("streamforge-renditions");
    const sharedPage = await context.newPage();
    await sharedPage.goto(sharedUrl);
    await expect(sharedPage.getByRole("heading", { name: title, exact: true })).toBeVisible();
    await sharedPage.close();

    let manifestReloads = 0;
    await page.route("**/api/playback/**", async (route) => {
      if (new URL(route.request().url()).pathname.endsWith("master.m3u8")) manifestReloads++;
      await route.fulfill({ status: 503, body: "Playback temporarily unavailable" });
    });
    // Start a fresh player to exercise the application's retry/fallback behavior,
    // independently of hls.js's longer per-segment retry and level-switch policies.
    await page.getByRole("button", { name: /Back to Home/ }).click();
    await page.getByRole("button", { name: `Watch ${title}`, exact: true }).click();
    await expect.poll(() => manifestReloads, { timeout: 60_000 }).toBeGreaterThanOrEqual(2);
    await expect(page.getByRole("button", { name: /MP4/ })).toBeVisible({
      timeout: 30_000,
    });
  });
});
