import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Account fields exposed to the signed-in browser. */
export interface AuthUser {
  id: string;
  username: string;
  email: string;
}
/** Account state without the HttpOnly session secret. */
export interface AuthResponse {
  user: AuthUser;
  expiresAtUtc: string;
}
/** New-account input; optional profile fields are omitted when empty. */
export interface Registration {
  username: string;
  email: string;
  password: string;
  dob?: string;
  address?: string;
}

/** Owns in-memory account state; the browser alone manages the session cookie. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private initialization?: Promise<void>;
  readonly user = signal<AuthUser | null>(null);
  readonly status = signal<'loading' | 'guest' | 'authenticated' | 'unavailable'>('loading');
  readonly message = signal('');

  /** Restores state once without preventing anonymous browsing during outages. */
  initialize(): Promise<void> {
    return (this.initialization ??= this.restore());
  }

  /** Retries account restoration after a temporary dependency failure. */
  async restore(): Promise<void> {
    try {
      this.accept(await firstValueFrom(this.http.get<AuthResponse>('/api/auth/me')));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) this.clear();
      else {
        this.status.set('unavailable');
        this.message.set('Sign-in is temporarily unavailable. Please try again.');
      }
    }
  }

  /** Fetches the framework antiforgery token before a state-changing request. */
  async refreshCsrf(): Promise<void> {
    await firstValueFrom(this.http.get<void>('/api/auth/csrf'));
  }

  /** Signs in and refreshes the antiforgery token for the new user identity. */
  async login(email: string, password: string): Promise<void> {
    await this.authenticate('/api/auth/login', { email, password });
  }

  /** Creates an account and automatically signs in with the returned cookie. */
  async register(registration: Registration): Promise<void> {
    await this.authenticate('/api/auth/register', registration);
  }

  /** Revokes the current session before clearing local account state. */
  async logout(): Promise<void> {
    this.message.set('');
    try {
      await this.refreshCsrf();
      await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
      this.clear();
      await this.refreshCsrf();
    } catch (error) {
      this.message.set(authError(error));
      throw error;
    }
  }

  /** Clears transient state after confirmed expiry or logout. */
  clear(): void {
    this.user.set(null);
    this.status.set('guest');
    this.message.set('');
  }

  private accept(response: AuthResponse): void {
    this.user.set(response.user);
    this.status.set('authenticated');
    this.message.set('');
  }

  private async authenticate(url: string, body: unknown): Promise<void> {
    await this.refreshCsrf();
    this.accept(await firstValueFrom(this.http.post<AuthResponse>(url, body)));
    await this.refreshCsrf();
  }
}

/** Maps expected authentication errors to safe and useful form feedback. */
export function authError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  if (error.error?.code === 'account_created_session_unavailable')
    return 'Your account was created. Sign-in is temporarily unavailable; please use Log in when it recovers.';
  if (error.status === 401) return 'Email or password is incorrect.';
  if (error.status === 409) return 'That username or email is already registered.';
  if (error.status === 403) return 'Your form has expired. Please try again.';
  if (error.status === 429) return 'Too many attempts. Please wait and try again.';
  if (error.status === 400) return 'Check your details. Date of birth cannot be in the future.';
  if (error.status === 503 || error.status === 0)
    return 'Sign-in is temporarily unavailable. Please try again.';
  return 'Something went wrong. Please try again.';
}

/** Allows local navigation only and prevents returning to an authentication loop. */
export function safeReturnUrl(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || /[\\\u0000-\u0020]/.test(value))
    return '/';
  try {
    const url = new URL(value, 'https://streamforge.invalid');
    if (
      url.origin !== 'https://streamforge.invalid' ||
      /^\/(login|register)(\/|$)/.test(url.pathname)
    )
      return '/';
    return url.pathname + url.search + url.hash;
  } catch {
    return '/';
  }
}
