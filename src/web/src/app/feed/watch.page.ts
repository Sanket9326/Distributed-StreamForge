import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { EngagementService, VideoSummary } from '../engagement/engagement.service';
import { ProfileService } from '../profiles/profile.service';
import { FeedService, FeedVideo } from './feed.service';
import { VideoCardComponent } from './video-card.component';

@Component({
  selector: 'app-watch-page',
  imports: [VideoCardComponent],
  templateUrl: './watch.page.html',
  styleUrl: './watch.page.scss',
})
export class WatchPage {
  protected readonly video = signal<FeedVideo | null>(null);
  protected readonly recommendations = signal<FeedVideo[]>([]);
  protected readonly summaries = signal(new Map<string, VideoSummary>());
  protected readonly creatorNames = signal(new Map<string, string>());
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly feed = inject(FeedService);
  private readonly engagement = inject(EngagementService);
  private readonly profiles = inject(ProfileService);
  private readonly destroyRef = inject(DestroyRef);
  private activePlayer?: HTMLVideoElement;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('videoId');
      if (id) void this.load(id);
    });
  }

  protected back(): void {
    void this.router.navigateByUrl('/');
  }

  protected openWatch(video: FeedVideo): void {
    this.activePlayer?.pause();
    void this.router.navigate(['/watch', video.id]);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected onPlayStarted(player: HTMLVideoElement): void {
    if (this.activePlayer && this.activePlayer !== player) this.activePlayer.pause();
    this.activePlayer = player;
  }

  protected summaryFor(videoId: string): VideoSummary | null {
    return this.summaries().get(videoId) ?? null;
  }

  protected creatorFor(video: FeedVideo): string {
    return (video.ownerId && this.creatorNames().get(video.ownerId)) || 'StreamForge Creator';
  }

  private async load(videoId: string): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    this.video.set(null);
    try {
      const [video, page] = await Promise.all([
        firstValueFrom(this.feed.getVideo(videoId)),
        firstValueFrom(this.feed.getPage(10, null)),
      ]);
      const recommendations = page.items.filter((candidate) => candidate.id !== video.id);
      const all = [video, ...recommendations];
      this.video.set(video);
      this.recommendations.set(recommendations);
      const [summaries, names] = await Promise.all([
        this.engagement.getSummaries(all.map((candidate) => candidate.id)),
        this.profiles.resolve(all.map((candidate) => candidate.ownerId)),
      ]);
      this.summaries.set(new Map(summaries.map((summary) => [summary.videoId, summary])));
      this.creatorNames.set(names);
    } catch {
      this.error.set('This video could not be loaded. It may no longer be available.');
    } finally {
      this.loading.set(false);
    }
  }
}
