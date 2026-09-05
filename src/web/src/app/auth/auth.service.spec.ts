import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService, safeReturnUrl } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('restores anonymous state without redirecting or storing a secret', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const pending = auth.initialize();
    http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await pending;
    expect(auth.status()).toBe('guest');
    expect(auth.user()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('distinguishes unavailable authentication from a logged-out user', async () => {
    const pending = auth.initialize();
    http.expectOne('/api/auth/me').flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(auth.status()).toBe('unavailable');
    expect(auth.message()).toContain('temporarily unavailable');
  });

  it('fetches antiforgery before login and refreshes it after the identity changes', async () => {
    const pending = auth.login('user@example.test', 'a password with spaces');
    http.expectOne('/api/auth/csrf').flush(null);
    await new Promise((resolve) => setTimeout(resolve, 0));
    const login = http.expectOne('/api/auth/login');
    expect(login.request.body.password).toBe('a password with spaces');
    login.flush({
      user: { id: 'one', username: 'user', email: 'user@example.test' },
      expiresAtUtc: '2026-09-06T00:00:00Z',
    });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/auth/csrf').flush(null);
    await pending;
    expect(auth.user()?.id).toBe('one');
  });

  it('keeps account state when logout revocation fails', async () => {
    auth.user.set({ id: 'one', username: 'user', email: 'user@example.test' });
    const pending = auth.logout();
    const rejected = expect(pending).rejects.toBeDefined();
    http.expectOne('/api/auth/csrf').flush(null);
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/auth/logout').flush({}, { status: 503, statusText: 'Unavailable' });
    await rejected;
    expect(auth.user()?.id).toBe('one');
  });

  it.each([
    'https://evil.test',
    '//evil.test',
    '/\\evil.test',
    '/login',
    '/register?returnUrl=/upload',
  ])('rejects unsafe return URL %s', (value) => {
    expect(safeReturnUrl(value)).toBe('/');
  });
  it('retains a local upload return URL', () =>
    expect(safeReturnUrl('/upload?source=home')).toBe('/upload?source=home'));
});
