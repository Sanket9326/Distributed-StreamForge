import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService, safeReturnUrl } from './auth.service';

/** Redirects failed protected uploads once, without replaying a media request. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        /^\/api\/uploads(?:\/|\?|$)/.test(request.url) &&
        error instanceof HttpErrorResponse &&
        error.status === 401
      ) {
        auth.clear();
        if (!router.url.startsWith('/login'))
          void router.navigate(['/login'], {
            queryParams: { returnUrl: safeReturnUrl(router.url) },
          });
      }
      return throwError(() => error);
    }),
  );
};
