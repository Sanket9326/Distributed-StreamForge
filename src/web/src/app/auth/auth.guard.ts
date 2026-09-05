import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService, safeReturnUrl } from './auth.service';

/** Protects upload navigation while leaving public browsing independent of authentication. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.initialize();
  if (auth.status() === 'unavailable') await auth.restore();
  if (auth.status() === 'unavailable') return false;
  if (auth.user()) {
    try {
      await auth.refreshCsrf();
      return true;
    } catch {
      auth.message.set('Uploads are temporarily unavailable. Please try again.');
      return false;
    }
  }
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: safeReturnUrl(state.url) } });
};
