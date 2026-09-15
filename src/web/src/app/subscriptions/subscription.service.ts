import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';

export interface SubscriptionItem {
  userId: string;
  createdAtUtc: string;
  sourcePartition: number;
  sourceOffset: string;
}
export interface SubscriptionPage {
  items: SubscriptionItem[];
  nextCursor: string | null;
}
export interface SubscriptionState {
  isActive: boolean;
  busy: boolean;
  message: string;
  sourcePartition?: number | null;
  sourceOffset?: string | null;
  revision: number;
}
interface SubscriptionStatus {
  creatorId: string;
  isActive: boolean;
  sourcePartition: number | null;
  sourceOffset: string | null;
}
interface SubscriptionMutation extends SubscriptionStatus {
  subscriberId: string;
  cachePending: boolean;
}
const empty: SubscriptionState = { isActive: false, busy: false, message: '', revision: 0 };

/** Shared, account-scoped state for watch buttons and both private lists. */
@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly states = signal(new Map<string, SubscriptionState>());
  private account: string | null = null;
  private epoch = 0;
  private readonly base = '/api/engagement/subscriptions';

  constructor() {
    effect(() => {
      this.auth.user();
      this.syncAccount();
    });
  }

  private syncAccount(): string | null {
    const id = this.auth.user()?.id ?? null;
    if (id !== this.account) {
      this.account = id;
      this.epoch++;
      this.states.set(new Map());
    }
    return id;
  }

  state(counterpart: string, incoming = false): SubscriptionState {
    this.syncAccount();
    return this.states().get(this.key(counterpart, incoming)) ?? empty;
  }

  private key(counterpart: string, incoming: boolean): string {
    return incoming ? `${counterpart}:${this.account}` : `${this.account}:${counterpart}`;
  }

  private put(counterpart: string, incoming: boolean, value: SubscriptionState): void {
    this.states.update((states) => new Map(states).set(this.key(counterpart, incoming), value));
  }

  private accept(
    counterpart: string,
    incoming: boolean,
    value: Partial<SubscriptionState>,
    revision: number,
  ): void {
    const current = this.state(counterpart, incoming);
    if (current.busy || current.revision !== revision) return;
    // An accepted offset must survive a temporarily older PostgreSQL fallback.
    if (
      current.sourceOffset != null &&
      (value.sourceOffset == null ||
        current.sourcePartition !== value.sourcePartition ||
        BigInt(current.sourceOffset) > BigInt(value.sourceOffset))
    )
      return;
    this.put(counterpart, incoming, { ...current, ...value });
  }

  async loadStatus(ids: string[]): Promise<void> {
    const account = this.syncAccount();
    const epoch = this.epoch;
    if (!account || !ids.length) return;
    const unique = [...new Set(ids)].slice(0, 50);
    const revisions = new Map(unique.map((id) => [id, this.state(id).revision]));
    let params = new HttpParams();
    unique.forEach((id) => {
      params = params.append('creatorIds', id);
    });
    const statuses = await firstValueFrom(
      this.http.get<SubscriptionStatus[]>(this.base + '/status', { params }),
    );
    if (account !== this.syncAccount() || epoch !== this.epoch) return;
    for (const status of statuses)
      this.accept(status.creatorId, false, status, revisions.get(status.creatorId) ?? -1);
  }

  async list(incoming: boolean, cursor: string | null = null): Promise<SubscriptionPage> {
    const account = this.syncAccount();
    const epoch = this.epoch;
    const revisions = new Map([...this.states()].map(([key, state]) => [key, state.revision]));
    let params = new HttpParams().set('limit', 20);
    if (cursor) params = params.set('cursor', cursor);
    const page = await firstValueFrom(
      this.http.get<SubscriptionPage>(this.base + (incoming ? '/subscribers' : ''), { params }),
    );
    if (account !== this.syncAccount() || epoch !== this.epoch)
      return { items: [], nextCursor: null };
    for (const item of page.items)
      this.accept(
        item.userId,
        incoming,
        { ...item, isActive: true },
        revisions.get(this.key(item.userId, incoming)) ?? 0,
      );
    return page;
  }

  /** Disables only this directed relationship until acknowledgement or rejection. */
  async change(counterpart: string, active: boolean, incoming = false): Promise<void> {
    const account = this.syncAccount();
    const epoch = this.epoch;
    if (!account || account === counterpart || this.state(counterpart, incoming).busy) return;
    const previous = this.state(counterpart, incoming);
    const revision = previous.revision + 1;
    this.put(counterpart, incoming, {
      ...previous,
      isActive: active,
      busy: true,
      message: '',
      revision,
    });
    let submitted = false;
    try {
      await this.auth.refreshCsrf();
      if (account !== this.syncAccount() || epoch !== this.epoch) return;
      submitted = true;
      const url = this.base + (incoming ? '/subscribers/' : '/') + encodeURIComponent(counterpart);
      const result = await firstValueFrom(
        active
          ? this.http.put<SubscriptionMutation>(url, {})
          : this.http.delete<SubscriptionMutation>(url),
      );
      if (account !== this.syncAccount() || epoch !== this.epoch) return;
      this.put(counterpart, incoming, {
        isActive: result.isActive,
        busy: false,
        message: '',
        revision,
        sourcePartition: result.sourcePartition,
        sourceOffset: result.sourceOffset,
      });
    } catch (error) {
      if (account !== this.syncAccount() || epoch !== this.epoch) return;
      const definite =
        !submitted ||
        (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500);
      this.put(counterpart, incoming, {
        ...(definite ? previous : this.state(counterpart, incoming)),
        busy: false,
        revision,
        message: definite
          ? 'The change was rejected. Please try again.'
          : 'Confirmation is unavailable. Refresh to check your subscription.',
      });
      if (!definite) {
        // This read is advisory: a delayed event can still arrive after a durable fallback.
        try {
          if (incoming) await this.list(true);
          else await this.loadStatus([counterpart]);
        } catch {
          /* Keep accessible uncertainty feedback until the user can refresh. */
        }
      }
    }
  }
}
