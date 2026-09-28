import { HttpXsrfTokenExtractor, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AuthService, AuthUser } from '../auth/auth.service';
import { BACKGROUND_HISTORY_REQUEST } from '../auth/background-request';
import { WatchHistoryItem, WatchHistoryService } from './watch-history.service';

const item = (offset: string, position = 120_000): WatchHistoryItem => ({ videoId: 'video', positionMs: position,
  durationMs: 600_000, isCompleted: false, createdAtUtc: '2026-09-15T10:00:00Z',
  updatedAtUtc: '2026-09-15T10:01:00Z', sourcePartition: 0, sourceOffset: offset });

describe('WatchHistoryService', () => {
  let history: WatchHistoryService;
  let http: HttpTestingController;
  let auth: { user: ReturnType<typeof signal<AuthUser | null>>; initialize: ReturnType<typeof vi.fn>;
    refreshCsrf: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    auth = { user: signal<AuthUser | null>({ id: 'alice', username: 'Alice', email: 'a@example.test' }),
      initialize: vi.fn().mockResolvedValue(undefined), refreshCsrf: vi.fn().mockResolvedValue(undefined), clear: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: auth }, { provide: HttpXsrfTokenExtractor, useValue: { getToken: () => 'csrf-token' } }] });
    history = TestBed.inject(WatchHistoryService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); vi.unstubAllGlobals(); vi.useRealTimers(); });
  it('skips guest requests and restarts completed videos', async () => {
    auth.user.set(null);
    expect(await history.resume('video')).toEqual({ position: 0, unavailable: false });
    http.expectNone('/api/engagement/watch-history/video');
    auth.user.set({ id: 'alice', username: 'Alice', email: 'a@example.test' });
    const pending = history.resume('video'); await Promise.resolve();
    const request = http.expectOne('/api/engagement/watch-history/video');
    expect(request.request.context.get(BACKGROUND_HISTORY_REQUEST)).toBe(true);
    request.flush({ ...item('1'), isCompleted: true });
    expect(await pending).toEqual({ position: 0, unavailable: false });
  });
  it('preserves accepted offsets above JavaScript integer precision against older reads', async () => {
    const account = history.accountKey()!;
    const save = history.save('video', item('9007199254740993'), account);
    const request = http.expectOne('/api/engagement/watch-history/video');
    expect(request.request.keepalive).toBe(true);
    request.flush({ progress: item('9007199254740993', 300_000), cachePending: false }); await save;
    const resume = history.resume('video'); await Promise.resolve();
    http.expectOne('/api/engagement/watch-history/video').flush(item('9007199254740992'));
    expect((await resume).position).toBe(300);
  });
  it('discards stale account callbacks', async () => {
    const resume = history.resume('video'); await Promise.resolve();
    auth.user.set({ id: 'bob', username: 'Bob', email: 'b@example.test' });
    http.expectOne('/api/engagement/watch-history/video').flush(item('4'));
    expect((await resume).position).toBe(0);
  });
  it('limits lookup waiting to two seconds', async () => {
    vi.useFakeTimers();
    const resume = history.resume('video'); await Promise.resolve();
    const request = http.expectOne('/api/engagement/watch-history/video');
    await vi.advanceTimersByTimeAsync(2001);
    expect(await resume).toEqual({ position: 0, unavailable: true });
    expect(request.cancelled).toBe(true);
  });
  it('uses authenticated keepalive fetch with the antiforgery header on exit', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 202,
      json: async () => ({ progress: item('8'), cachePending: false }) });
    vi.stubGlobal('fetch', fetchMock);
    expect(await history.save('video', item('8'), history.accountKey()!, true)).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith('/api/engagement/watch-history/video', expect.objectContaining({
      method: 'PUT', credentials: 'same-origin', keepalive: true,
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': 'csrf-token' },
    }));
  });
  it('bounds logout flushing without rejecting logout', async () => {
    vi.useFakeTimers();
    history.register(() => new Promise(() => undefined));
    const pending = history.flushBeforeLogout();
    await vi.advanceTimersByTimeAsync(2000);
    await expect(pending).resolves.toBeUndefined();
  });
});
