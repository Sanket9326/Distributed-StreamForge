import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { SubscriptionService } from './subscription.service';

describe('SubscriptionService', () => {
  const user = '10000000-0000-0000-0000-000000000001';
  const creator = '20000000-0000-0000-0000-000000000002';
  const auth = {
    user: signal({ id: user, username: 'viewer', email: 'viewer@example.test' }),
    refreshCsrf: vi.fn(async () => {}),
  };
  let service: SubscriptionService;
  let http: HttpTestingController;
  const base = '/api/engagement/subscriptions';
  beforeEach(() => {
    auth.user.set({ id: user, username: 'viewer', email: 'viewer@example.test' });
    auth.refreshCsrf.mockReset().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    service = TestBed.inject(SubscriptionService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('updates immediately, blocks duplicate mutations and retains accepted state during a stale fallback', async () => {
    const pending = service.change(creator, true);
    expect(service.state(creator)).toMatchObject({ isActive: true, busy: true });
    await service.change(creator, false);
    await Promise.resolve();
    const mutation = http.expectOne(base + '/' + creator);
    expect(mutation.request.method).toBe('PUT');
    mutation.flush(
      {
        subscriberId: user,
        creatorId: creator,
        isActive: true,
        cachePending: true,
        sourcePartition: 1,
        sourceOffset: '9007199254740993',
      },
      { status: 202, statusText: 'Accepted' },
    );
    await pending;
    const status = service.loadStatus([creator]);
    http
      .expectOne((request) => request.url === base + '/status')
      .flush([
        {
          creatorId: creator,
          isActive: false,
          sourcePartition: 1,
          sourceOffset: '9007199254740992',
        },
      ]);
    await status;
    expect(service.state(creator)).toMatchObject({ isActive: true, busy: false });
  });

  it('restores the previous state on definite rejection', async () => {
    const pending = service.change(creator, true);
    await Promise.resolve();
    http.expectOne(base + '/' + creator).flush({}, { status: 404, statusText: 'Not Found' });
    await pending;
    expect(service.state(creator)).toMatchObject({ isActive: false, busy: false });
    expect(service.state(creator).message).toContain('rejected');
  });

  it('refetches after an uncertain response and keeps uncertainty visible', async () => {
    const pending = service.change(creator, true);
    await Promise.resolve();
    http.expectOne(base + '/' + creator).flush({}, { status: 503, statusText: 'Unavailable' });
    await Promise.resolve();
    await Promise.resolve();
    http
      .expectOne((request) => request.url === base + '/status')
      .flush([{ creatorId: creator, isActive: true, sourcePartition: 1, sourceOffset: '4' }]);
    await pending;
    expect(service.state(creator).message).toContain('Confirmation is unavailable');
    expect(service.state(creator).isActive).toBe(true);
  });

  it('removes only the incoming relationship', async () => {
    const load = service.list(true);
    http
      .expectOne((request) => request.url === base + '/subscribers')
      .flush({
        items: [
          {
            userId: creator,
            createdAtUtc: '2026-09-01T00:00:00Z',
            sourcePartition: 0,
            sourceOffset: '0',
          },
        ],
        nextCursor: null,
      });
    await load;
    const outgoing = service.loadStatus([creator]);
    http
      .expectOne((request) => request.url === base + '/status')
      .flush([{ creatorId: creator, isActive: true, sourcePartition: 1, sourceOffset: '7' }]);
    await outgoing;
    const remove = service.change(creator, false, true);
    expect(service.state(creator).isActive).toBe(true);
    expect(service.state(creator, true).isActive).toBe(false);
    await Promise.resolve();
    const request = http.expectOne(base + '/subscribers/' + creator);
    expect(request.request.method).toBe('DELETE');
    request.flush(
      {
        subscriberId: creator,
        creatorId: user,
        isActive: false,
        cachePending: false,
        sourcePartition: 0,
        sourceOffset: '1',
      },
      { status: 202, statusText: 'Accepted' },
    );
    await remove;
    expect(service.state(creator).isActive).toBe(true);
  });

  it('ignores a status response that was started before a mutation', async () => {
    const load = service.loadStatus([creator]);
    const stale = http.expectOne((request) => request.url === base + '/status');
    const change = service.change(creator, true);
    await Promise.resolve();
    http
      .expectOne(base + '/' + creator)
      .flush({ isActive: true, sourcePartition: 0, sourceOffset: '12' });
    await change;
    stale.flush([{ creatorId: creator, isActive: false, sourcePartition: 0, sourceOffset: '11' }]);
    await load;
    expect(service.state(creator).isActive).toBe(true);
  });

  it('clears state and ignores responses from the previous account', async () => {
    const pending = service.change(creator, true);
    await Promise.resolve();
    const oldRequest = http.expectOne(base + '/' + creator);
    auth.user.set({ id: creator, username: 'creator', email: 'creator@example.test' });
    expect(service.state(user).isActive).toBe(false);
    oldRequest.flush({ isActive: true, sourcePartition: 0, sourceOffset: '99' });
    await pending;
    expect(service.state(user).isActive).toBe(false);
  });
});
