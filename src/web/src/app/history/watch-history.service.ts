import { HttpClient, HttpContext, HttpParams, HttpXsrfTokenExtractor } from '@angular/common/http';
import { Injectable, effect, inject } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { BACKGROUND_HISTORY_REQUEST } from '../auth/background-request';

export interface WatchProgress { positionMs: number; durationMs: number; isCompleted: boolean; }
export interface WatchHistoryItem extends WatchProgress {
  videoId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  sourcePartition: number;
  sourceOffset: string;
}
export interface WatchHistoryPage { items: WatchHistoryItem[]; nextCursor: string | null; }
interface Mutation { progress: WatchHistoryItem; cachePending: boolean; }

@Injectable({ providedIn: 'root' })
export class WatchHistoryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly xsrf = inject(HttpXsrfTokenExtractor);
  private readonly base = '/api/engagement/watch-history';
  private account: object | null = null;
  private epoch = 0;
  private readonly accepted = new Map<string, WatchHistoryItem>();
  private readonly flushers = new Set<() => Promise<unknown>>();

  constructor() { effect(() => { this.auth.user(); this.accountKey(); }); }

  accountKey(): string | null {
    const user = this.auth.user();
    if (user !== this.account) {
      this.account = user;
      this.epoch++;
      this.accepted.clear();
    }
    return user ? `${user.id}:${this.epoch}` : null;
  }

  register(flush: () => Promise<unknown>): () => void {
    this.flushers.add(flush);
    return () => this.flushers.delete(flush);
  }

  async flushBeforeLogout(): Promise<void> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        Promise.allSettled([...this.flushers].map((flush) => flush())),
        new Promise<void>((resolve) => { timer = setTimeout(resolve, 2000); }),
      ]);
    } finally { clearTimeout(timer); }
  }

  async resume(videoId: string): Promise<{ position: number; unavailable: boolean }> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const lookup = async () => {
      await this.auth.initialize();
      const account = this.accountKey();
      if (!account) return { position: 0, unavailable: false };
      // Exit handlers cannot wait for token acquisition.
      void this.auth.refreshCsrf().catch(() => undefined);
      const item = await firstValueFrom(this.http.get<WatchHistoryItem | null>(`${this.base}/${videoId}`, {
        context: new HttpContext().set(BACKGROUND_HISTORY_REQUEST, true),
      }).pipe(timeout(2000)));
      if (account !== this.accountKey()) return { position: 0, unavailable: false };
      const current = item ? this.accept(item) : this.accepted.get(videoId);
      return { position: current && !current.isCompleted ? current.positionMs / 1000 : 0, unavailable: false };
    };
    try {
      return await Promise.race([lookup(), new Promise<{ position: number; unavailable: boolean }>((resolve) => {
        timer = setTimeout(() => resolve({ position: 0, unavailable: true }), 2000);
      })]);
    } catch { return { position: 0, unavailable: true }; }
    finally { clearTimeout(timer); }
  }

  async list(cursor: string | null = null): Promise<WatchHistoryPage> {
    const account = this.accountKey();
    if (!account) return { items: [], nextCursor: null };
    let params = new HttpParams().set('limit', 20);
    if (cursor) params = params.set('cursor', cursor);
    const page = await firstValueFrom(this.http.get<WatchHistoryPage>(this.base, { params }));
    if (account !== this.accountKey()) return { items: [], nextCursor: null };
    return { ...page, items: page.items.map((item) => this.accept(item)) };
  }

  async save(videoId: string, progress: WatchProgress, account: string, keepalive = false): Promise<boolean> {
    if (account !== this.accountKey()) return false;
    try {
      let result: Mutation;
      if (keepalive) {
        const token = this.xsrf.getToken();
        if (!token) return false;
        const response = await fetch(`${this.base}/${videoId}`, {
          method: 'PUT', credentials: 'same-origin', keepalive: true,
          headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
          body: JSON.stringify(progress),
        });
        if (response.status === 401 && account === this.accountKey()) this.auth.clear();
        if (!response.ok) return false;
        result = await response.json() as Mutation;
      } else {
        result = await firstValueFrom(this.http.put<Mutation>(`${this.base}/${videoId}`, progress, {
          keepalive: true,
          context: new HttpContext().set(BACKGROUND_HISTORY_REQUEST, true),
        }));
      }
      if (account !== this.accountKey()) return false;
      this.accept(result.progress);
      return true;
    } catch { return false; }
  }

  private accept(item: WatchHistoryItem): WatchHistoryItem {
    const current = this.accepted.get(item.videoId);
    if (current && (current.sourcePartition !== item.sourcePartition ||
        BigInt(current.sourceOffset) > BigInt(item.sourceOffset))) return current;
    this.accepted.set(item.videoId, item);
    return item;
  }
}
