import { Component, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { FeedService, FeedVideo } from '../feed/feed.service';
import { VideoCardComponent } from '../feed/video-card.component';
import { ProfileService } from '../profiles/profile.service';
import { WatchHistoryItem, WatchHistoryService } from './watch-history.service';

interface HistoryEntry { progress: WatchHistoryItem; video: FeedVideo | null; }

@Component({
  selector: 'app-watch-history-page',
  imports: [VideoCardComponent, RouterLink],
  templateUrl: './watch-history.page.html',
  styleUrl: './watch-history.page.scss',
})
export class WatchHistoryPage {
  private readonly history = inject(WatchHistoryService);
  private readonly feed = inject(FeedService);
  private readonly profiles = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly entries = signal<HistoryEntry[]>([]);
  protected readonly names = signal(new Map<string, string>());
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly cursor = signal<string | null>(null);
  private generation = 0;

  constructor() {
    effect(() => {
      const user = this.auth.user();
      this.generation++;
      this.entries.set([]); this.names.set(new Map()); this.cursor.set(null); this.error.set('');
      if (user) void this.load(false);
      else this.loading.set(false);
    });
  }

  protected async load(append: boolean): Promise<void> {
    const generation = this.generation;
    this.loading.set(true); this.error.set('');
    try {
      const page = await this.history.list(append ? this.cursor() : null);
      let next = 0;
      const entries: HistoryEntry[] = new Array(page.items.length);
      await Promise.all(Array.from({ length: Math.min(4, page.items.length) }, async () => {
        while (next < page.items.length && generation === this.generation) {
          const index = next++;
          let video: FeedVideo | null = null;
          try { video = await firstValueFrom(this.feed.getVideo(page.items[index].videoId)); }
          catch { /* Preserve unavailable history entries. */ }
          entries[index] = { video, progress: page.items[index] };
        }
      }));
      if (generation !== this.generation) return;
      const merged = new Map((append ? this.entries() : []).map((x) => [x.progress.videoId, x]));
      for (const entry of entries) merged.set(entry.progress.videoId, entry);
      this.entries.set([...merged.values()]); this.cursor.set(page.nextCursor);
      try {
        const names = await this.profiles.resolve(entries.map((entry) => entry.video?.ownerId ?? null));
        if (generation === this.generation) this.names.update((old) => new Map([...old, ...names]));
      } catch { /* Generic creator labels remain available. */ }
    } catch { if (generation === this.generation) this.error.set('Watch history could not be loaded. Please try again.'); }
    finally { if (generation === this.generation) this.loading.set(false); }
  }
  protected open(video: FeedVideo): void { void this.router.navigate(['/watch', video.id]); }
  protected creator(video: FeedVideo): string { return this.names().get(video.ownerId ?? '') ?? 'StreamForge Creator'; }
  protected time(ms: number): string {
    const seconds = Math.floor(ms / 1000);
    return seconds >= 3600 ? `${Math.floor(seconds / 3600)}:${String(Math.floor(seconds / 60) % 60).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`
      : `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  }
  protected date(value: string): string { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)); }
}
