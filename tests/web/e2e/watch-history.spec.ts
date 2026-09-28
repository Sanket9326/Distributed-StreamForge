import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test, expect, Page } from '../../../src/web/node_modules/@playwright/test';

const userId = '10000000-0000-0000-0000-000000000001';
const videoId = '40000000-0000-0000-0000-000000000004';
const unavailableId = '40000000-0000-0000-0000-000000000005';
const base = '/api/engagement/watch-history';
const artifacts = join(__dirname, '../../../artifacts/watch-history-media');
const media = join(artifacts, 'synthetic.mp4');
const screenshot = (name: string) => join(__dirname, '../../../artifacts/screenshots', name);

test.beforeAll(() => {
  mkdirSync(artifacts, { recursive: true });
  const runFfmpeg = (args: string[]) => {
    try { execFileSync('ffmpeg', args, { cwd: artifacts }); }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      execFileSync('docker', ['run', '--rm', '--entrypoint', 'ffmpeg', '--mount',
        `type=bind,source=${artifacts},target=/media`, '--workdir', '/media',
        'streamforge-transcoding-service', ...args]);
    }
  };
  const args = ['-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i',
    'testsrc2=size=640x360:rate=24', '-t', '24', '-c:v', 'libx264', '-preset', 'ultrafast',
    '-pix_fmt', 'yuv420p', '-g', '48', '-movflags', '+faststart'];
  runFfmpeg([...args, 'synthetic.mp4']);
  for (const height of [180, 360]) {
    runFfmpeg(['-hide_banner', '-loglevel', 'error', '-y', '-i', 'synthetic.mp4',
      '-vf', `scale=-2:${height}`, '-c:v', 'libx264', '-preset', 'ultrafast', '-g', '48',
      '-hls_time', '4', '-hls_playlist_type', 'vod', '-hls_segment_type', 'fmp4',
      '-hls_fmp4_init_filename', `init-${height}.mp4`, '-hls_segment_filename', `${height}-%03d.m4s`, `${height}.m3u8`]);
  }
  writeFileSync(join(artifacts, 'master.m3u8'), '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=400000,RESOLUTION=320x180\n180.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=1400000,RESOLUTION=640x360\n360.m3u8\n');
});

async function setup(page: Page, guest = false, completed = false, brokenHls = false, hls = false) {
  let authenticated = !guest;
  let progress = { videoId, positionMs: completed ? 24_000 : 8_000, durationMs: 24_000, isCompleted: completed,
    createdAtUtc: '2026-09-15T10:00:00Z', updatedAtUtc: '2026-09-15T11:00:00Z', sourcePartition: 0, sourceOffset: '10' };
  const saves: typeof progress[] = [];
  const historyReads: string[] = [];
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/synthetic-history/*', async (route) => {
    const name = new URL(route.request().url()).pathname.split('/').at(-1)!;
    if (!/^(master|180|360)\.m3u8$|^init-(180|360)\.mp4$|^(180|360)-\d{3}\.m4s$/.test(name))
      return route.fulfill({ status: 404 });
    return route.fulfill({ body: readFileSync(join(artifacts, name)),
      contentType: name.endsWith('.m3u8') ? 'application/vnd.apple.mpegurl' : 'video/mp4' });
  });
  const bytes = readFileSync(media);
  await page.route('**/synthetic-history.mp4', async (route) => {
    const range = route.request().headers()['range'];
    const match = range?.match(/bytes=(\d+)-(\d*)/);
    if (match) {
      const start = Number(match[1]); const end = Math.min(Number(match[2] || bytes.length - 1), bytes.length - 1);
      return route.fulfill({ status: 206, contentType: 'video/mp4', body: bytes.subarray(start, end + 1),
        headers: { 'Accept-Ranges': 'bytes', 'Content-Range': `bytes ${start}-${end}/${bytes.length}` } });
    }
    return route.fulfill({ contentType: 'video/mp4', body: bytes, headers: { 'Accept-Ranges': 'bytes' } });
  });
  const video = () => ({ id: videoId, ownerId: null, title: 'A walk through the city',
    description: 'A synthetic clip for playback verification.', hashtags: [], uploadedAtUtc: '2026-09-14T10:00:00Z',
    availableAtUtc: '2026-09-14T10:00:00Z', hlsManifestUrl: brokenHls ? '/api/playback/broken.m3u8'
      : hls ? '/synthetic-history/master.m3u8' : null,
    renditions: [{ tier: '360p', width: 640, height: 360, videoCodec: 'h264', audioCodec: null,
      contentType: 'video/mp4', sizeBytes: bytes.length, playbackUrl: '/synthetic-history.mp4',
      playbackUrlExpiresAtUtc: new Date(Date.now() + 3600_000).toISOString() }] });
  await page.route('**/api/**', async (route) => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname;
    if (path === '/api/auth/me') return authenticated ? route.fulfill({ json: {
      user: { id: userId, username: 'sanket', email: 'sanket@example.test' }, expiresAtUtc: '2026-12-01T00:00:00Z',
    } }) : route.fulfill({ status: 401, json: {} });
    if (path === '/api/auth/csrf') return route.fulfill({ status: 204, headers: { 'Set-Cookie': 'XSRF-TOKEN=history-e2e; Path=/; SameSite=Strict' } });
    if (path === '/api/auth/logout') { authenticated = false; return route.fulfill({ status: 204 }); }
    if (path === '/api/playback/broken.m3u8') return route.fulfill({ status: 404 });
    if (path === base && request.method() === 'GET') {
      historyReads.push(path);
      return route.fulfill({ json: { items: [progress, { ...progress, videoId: unavailableId, positionMs: 3000 }], nextCursor: null } });
    }
    if (path === `${base}/${videoId}`) {
      if (request.method() === 'PUT') {
        expect(request.headers()['x-xsrf-token']).toBe('history-e2e');
        progress = { ...progress, ...request.postDataJSON(), sourceOffset: String(Number(progress.sourceOffset) + 1) };
        saves.push(progress);
        return route.fulfill({ status: 202, json: { progress, cachePending: false } });
      }
      historyReads.push(path); return route.fulfill({ json: progress });
    }
    if (path === '/api/feed/videos') return route.fulfill({ json: { items: [video()], nextCursor: null } });
    if (path === `/api/feed/videos/${unavailableId}`) return route.fulfill({ status: 404 });
    if (path === `/api/feed/videos/${videoId}`) return route.fulfill({ json: video() });
    if (path.endsWith('/renditions')) return route.fulfill({ json: video().renditions });
    if (path.endsWith('/summaries')) return route.fulfill({ json: [{ videoId, likeCount: 0, dislikeCount: 0, viewCount: 0, commentCount: 0 }] });
    if (path.endsWith('/comments')) return route.fulfill({ json: { items: [], nextCursor: null, totalCount: 0 } });
    if (path.endsWith('/reaction')) return route.fulfill({ json: { reaction: 'none' } });
    if (path.endsWith('/views')) return route.fulfill({ json: { counted: true, viewCount: 1 } });
    return route.fulfill({ json: [] });
  });
  return { saves, historyReads, errors };
}

test('shows history on desktop and mobile and resumes an actual paused video', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/watch-history');
  await expect(page.getByRole('heading', { name: 'Watch history', exact: true })).toBeVisible();
  await expect(page.getByText('Resume at 0:08')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Video unavailable' })).toBeVisible();
  await page.screenshot({ path: screenshot('watch-history-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('.mobile-nav').getByText('Watch history')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: screenshot('watch-history-mobile.png'), fullPage: true });
  await page.getByRole('button', { name: 'Watch A walk through the city', exact: true }).click();
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(2);
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThanOrEqual(8);
  await player.evaluate(async (v: HTMLVideoElement) => { await v.play(); v.currentTime = 12; });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.seeking)).toBe(false);
  await player.evaluate((v: HTMLVideoElement) => v.pause());
  await expect.poll(() => state.saves.length).toBeGreaterThan(0);
  expect(state.saves.at(-1)!.positionMs).toBeGreaterThanOrEqual(12_000);
  await page.reload();
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThanOrEqual(12);
  expect(state.errors).toEqual([]);
});

test('restarts completed videos and saves before logout', async ({ page }) => {
  const state = await setup(page, false, true);
  await page.goto(`/watch/${videoId}`);
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(2);
  expect(await player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeLessThan(4);
  await player.evaluate(async (v: HTMLVideoElement) => { await v.play(); v.currentTime = 15; });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.seeking)).toBe(false);
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Log in', exact: true })).toBeVisible();
  expect(state.saves.at(-1)!.positionMs).toBeGreaterThanOrEqual(15_000);
  expect(state.saves.at(-1)!.isCompleted).toBe(false);
  expect(state.errors).toEqual([]);
});

test('preserves resume when HLS falls back to MP4', async ({ page }) => {
  const state = await setup(page, false, false, true);
  await page.goto(`/watch/${videoId}`);
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState), { timeout: 30_000 }).toBeGreaterThanOrEqual(2);
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThanOrEqual(8);
  expect(state.errors).toEqual([]);
});

test('guest playback makes no history requests and history navigation requires login', async ({ page }) => {
  const state = await setup(page, true);
  await page.goto(`/watch/${videoId}`);
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(2);
  await player.evaluate(async (v: HTMLVideoElement) => { await v.play(); v.pause(); });
  await page.goto('/watch-history');
  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fwatch-history/);
  expect(state.historyReads).toEqual([]); expect(state.saves).toEqual([]); expect(state.errors).toEqual([]);
});

test('resumes real HLS, preserves position through quality changes, and records completion', async ({ page }) => {
  const state = await setup(page, false, false, false, true);
  await page.goto(`/watch/${videoId}`);
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThanOrEqual(8);
  await player.evaluate(async (v: HTMLVideoElement) => { await v.play(); v.currentTime = 12; });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.seeking)).toBe(false);
  await page.locator('.quality-pill').click();
  await page.getByRole('menuitemradio', { name: '360p', exact: true }).click();
  expect(await player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThanOrEqual(12);
  await player.evaluate((v: HTMLVideoElement) => { v.currentTime = v.duration - 0.5; });
  await expect.poll(() => state.saves.at(-1)?.isCompleted, { timeout: 15_000 }).toBe(true);
  await page.reload();
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(2);
  expect(await player.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeLessThan(4);
  expect(state.errors).toEqual([]);
});

test('saves a paused snapshot on navigation after an earlier save failed', async ({ page }) => {
  const state = await setup(page);
  let failed = false;
  await page.route(`**${base}/${videoId}`, async (route) => {
    if (!failed && route.request().method() === 'PUT') {
      failed = true;
      return route.fulfill({ status: 503, json: {} });
    }
    return route.fallback();
  });
  await page.goto(`/watch/${videoId}`);
  const player = page.getByLabel('Play A walk through the city', { exact: true });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(2);
  await player.evaluate(async (v: HTMLVideoElement) => { await v.play(); v.currentTime = 12; });
  await expect.poll(() => player.evaluate((v: HTMLVideoElement) => v.seeking)).toBe(false);
  await player.evaluate((v: HTMLVideoElement) => v.pause());
  await expect(page.getByText('Your latest watch progress could not be confirmed.')).toBeVisible();
  await page.getByRole('button', { name: 'Back to Home' }).click();
  await expect.poll(() => state.saves.length).toBeGreaterThan(0);
  expect(state.saves.at(-1)!.positionMs).toBeGreaterThanOrEqual(12_000);
  expect(state.errors).toEqual([]);
});

test('history recovers from a failed list request and shows its empty state', async ({ page }) => {
  await setup(page);
  let failed = false;
  await page.route(`**${base}?*`, async (route) => {
    if (!failed) { failed = true; return route.fulfill({ status: 503, json: {} }); }
    return route.fulfill({ json: { items: [], nextCursor: null } });
  });
  await page.goto('/watch-history');
  await expect(page.getByRole('alert')).toContainText('Watch history could not be loaded.');
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Your watch history starts here' })).toBeVisible();
});
