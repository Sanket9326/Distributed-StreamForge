import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal, untracked } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { ProfileService } from '../profiles/profile.service';
import { SubscriptionItem, SubscriptionService } from './subscription.service';

@Component({
  selector: 'app-subscriptions-page',
  imports: [DatePipe],
  templateUrl: './subscriptions.page.html',
  styleUrl: './subscriptions.page.scss',
})
export class SubscriptionsPage {
  protected readonly subscriptions = inject(SubscriptionService);
  private readonly auth = inject(AuthService);
  private readonly profiles = inject(ProfileService);
  protected readonly incoming = signal(false);
  protected readonly items = signal<SubscriptionItem[]>([]);
  protected readonly names = signal(new Map<string, string>());
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly feedback = signal('');
  private generation = 0;

  constructor() {
    effect(() => {
      const user = this.auth.user();
      this.generation++;
      this.items.set([]);
      this.names.set(new Map());
      this.cursor.set(null);
      this.feedback.set('');
      if (user) untracked(() => void this.load());
    });
  }

  protected select(incoming: boolean): void {
    if (this.incoming() === incoming) return;
    this.incoming.set(incoming);
    this.items.set([]);
    this.cursor.set(null);
    this.feedback.set('');
    void this.load();
  }

  protected async load(append = false): Promise<void> {
    const generation = ++this.generation;
    const incoming = this.incoming();
    this.loading.set(true);
    this.error.set('');
    try {
      const page = await this.subscriptions.list(incoming, append ? this.cursor() : null);
      if (generation !== this.generation) return;
      this.items.set(
        append
          ? [
              ...this.items(),
              ...page.items.filter(
                (item) => !this.items().some((existing) => existing.userId === item.userId),
              ),
            ]
          : page.items,
      );
      this.cursor.set(page.nextCursor);
      const ids = page.items.map((item) => item.userId);
      // Names and reverse status have independent availability.
      await Promise.all([
        this.profiles
          .resolve(ids)
          .then((names) => {
            if (generation === this.generation)
              this.names.update((current) => new Map([...current, ...names]));
          })
          .catch(() => {}),
        incoming
          ? this.subscriptions.loadStatus(ids).catch(() => {
              if (generation === this.generation)
                this.error.set('Subscribe back status is unavailable. Please refresh.');
            })
          : Promise.resolve(),
      ]);
    } catch {
      if (generation === this.generation)
        this.error.set('Subscriptions could not be loaded. Please refresh.');
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }

  protected visible(): SubscriptionItem[] {
    return this.items().filter((item) => {
      const state = this.subscriptions.state(item.userId, this.incoming());
      return state.isActive || state.busy || !!state.message;
    });
  }

  protected async change(id: string, active: boolean, incoming: boolean): Promise<void> {
    const generation = this.generation;
    await this.subscriptions.change(id, active, incoming);
    if (generation !== this.generation) return;
    const state = this.subscriptions.state(id, incoming);
    this.feedback.set(
      state.message ||
        (state.isActive !== active
          ? 'Subscription updated by another request.'
          : active
            ? 'Subscribed.'
            : incoming
              ? 'Subscriber removed.'
              : 'Unsubscribed.'),
    );
  }
}
