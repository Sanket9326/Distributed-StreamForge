import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';
import { BACKGROUND_HISTORY_REQUEST } from './background-request';

describe('Upload authentication responses', () => {
  let http: HttpTestingController;
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }),
  );
  afterEach(() => http.verify());
  it('clears an expired background history session without redirecting playback', () => {
    http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.user.set({ id: 'one', username: 'tester', email: 'test@example.test' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    TestBed.inject(HttpClient).get('/api/engagement/watch-history/video', {
      context: new HttpContext().set(BACKGROUND_HISTORY_REQUEST, true),
    }).subscribe({ error: () => undefined });
    http.expectOne('/api/engagement/watch-history/video').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.user()).toBeNull(); expect(navigate).not.toHaveBeenCalled();
  });
  it('does not clear a new account when an older background request expires', () => {
    http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.user.set({ id: 'one', username: 'tester', email: 'test@example.test' });
    TestBed.inject(HttpClient).get('/api/engagement/watch-history/video', {
      context: new HttpContext().set(BACKGROUND_HISTORY_REQUEST, true),
    }).subscribe({ error: () => undefined });
    auth.user.set({ id: 'two', username: 'new', email: 'new@example.test' });
    http.expectOne('/api/engagement/watch-history/video').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.user()?.id).toBe('two');
  });
  it.each([401, 503])('handles upload status %s without replaying media', (status) => {
    http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.user.set({ id: 'one', username: 'tester', email: 'test@example.test' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    TestBed.inject(HttpClient)
      .post('/api/uploads', new FormData())
      .subscribe({ error: () => undefined });
    http.expectOne('/api/uploads').flush({}, { status, statusText: 'Failure' });
    expect(navigate).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
    expect(auth.user()?.id ?? null).toBe(status === 401 ? null : 'one');
    http.expectNone('/api/uploads');
  });
});
