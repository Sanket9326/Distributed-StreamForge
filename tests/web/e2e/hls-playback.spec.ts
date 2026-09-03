import { execFileSync } from 'node:child_process';
import { mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { test, expect } from '../../../src/web/node_modules/@playwright/test';

test.describe('full Compose adaptive HLS playback', () => {
  test.skip(process.env['STREAMFORGE_E2E'] !== '1', 'Set STREAMFORGE_E2E=1 after starting the Compose topology.');
  const artifacts = join(__dirname, '.generated');
  const media = join(artifacts, 'synthetic.mp4');

  test.beforeAll(() => {
    mkdirSync(artifacts, { recursive: true });
    execFileSync('ffmpeg', ['-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc2=size=1920x1080:rate=30','-f','lavfi','-i','sine=frequency=1000:sample_rate=48000','-t','24','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac',media]);
  });
  test.afterAll(() => rmSync(artifacts, { recursive: true, force: true }));

  test('uploads, adapts, switches manually, refreshes signatures, and falls back to MP4', async ({ page, context }) => {
    await page.goto('/upload');
    await page.getByLabel('Title').fill(`HLS E2E ${Date.now()}`);
    await page.locator('#video-file').setInputFiles(media);
    await page.getByRole('button', { name: /Upload video/ }).click();
    await expect(page.getByText('Video stored and queued')).toBeVisible();
    await expect(page.getByText('Your video is ready')).toBeVisible({ timeout: 8 * 60_000 });

    const requested: string[] = [];
    page.on('request', request => { if (request.url().includes('.m4s')) requested.push(request.url()); });
    const cdp = await context.newCDPSession(page);
    await cdp.send('Network.enable');
    await cdp.send('Network.emulateNetworkConditions', { offline:false, latency:200, downloadThroughput:100_000, uploadThroughput:50_000 });
    await page.goto('/');
    const player = page.locator('video').first();
    await player.evaluate((video: HTMLVideoElement) => video.play());
    await expect.poll(() => requested.some(url => url.includes('/360p/'))).toBeTruthy();
    await cdp.send('Network.emulateNetworkConditions', { offline:false, latency:10, downloadThroughput:10_000_000, uploadThroughput:1_000_000 });
    await expect.poll(() => requested.some(url => /\/(720p|1080p)\//.test(url)), { timeout: 60_000 }).toBeTruthy();

    await page.getByRole('button', { name: /Auto/ }).click();
    await page.getByRole('menuitemradio', { name: '720p' }).click();
    const before = requested.length;
    await expect.poll(() => requested.slice(before).some(url => url.includes('/720p/'))).toBeTruthy();

    let manifestReloads = 0;
    await page.route('**/api/playback/**', async route => { manifestReloads++; await route.continue(); });
    await page.route('**/*.m4s*', route => route.abort());
    const timestamp = await player.evaluate((video: HTMLVideoElement) => video.currentTime);
    await expect.poll(() => manifestReloads).toBeGreaterThanOrEqual(1);
    await expect.poll(() => player.evaluate((video: HTMLVideoElement) => video.currentTime)).toBeGreaterThanOrEqual(Math.max(0, timestamp - 1));
    await expect(page.getByRole('button', { name: /MP4/ })).toBeVisible({ timeout: 30_000 });
  });
});
