import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { AuthService } from './auth.service';
import { authGuard } from './auth.guard';

describe('Upload auth guard', () => {
  function setup(status: string, user: unknown) {
    const auth = {
      initialize: vi.fn().mockResolvedValue(undefined),
      restore: vi.fn().mockResolvedValue(undefined),
      refreshCsrf: vi.fn().mockResolvedValue(undefined),
      status: () => status,
      user: () => user,
      message: { set: vi.fn() },
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    return auth;
  }
  async function guard() {
    return await TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/upload' } as RouterStateSnapshot),
    );
  }
  it('preserves the upload return path for guests', async () => {
    setup('guest', null);
    const result = await guard();
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(
      '/login?returnUrl=%2Fupload',
    );
  });
  it('refreshes csrf before allowing an authenticated upload', async () => {
    const auth = setup('authenticated', { id: 'one' });
    expect(await guard()).toBe(true);
    expect(auth.refreshCsrf).toHaveBeenCalledOnce();
  });
  it('does not redirect an outage to login', async () => {
    const auth = setup('unavailable', null);
    expect(await guard()).toBe(false);
    expect(auth.restore).toHaveBeenCalledOnce();
  });
});
