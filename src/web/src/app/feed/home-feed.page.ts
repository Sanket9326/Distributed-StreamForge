import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { FeedService, FeedVideo } from './feed.service';
import { VideoCardComponent } from './video-card.component';
import { Router } from '@angular/router';
import { EngagementService, VideoSummary } from '../engagement/engagement.service';
import { ProfileService } from '../profiles/profile.service';

@Component({
  selector: 'app-home-feed-page',
  imports: [VideoCardComponent],
  templateUrl: './home-feed.page.html',
  styleUrl: './home-feed.page.scss',
})
export class HomeFeedPage implements OnInit, OnDestroy {
  @ViewChild('paginationSentinel')
  private set paginationSentinel(element: ElementRef<HTMLElement> | undefined) {
    this.sentinel = element;
    this.observeSentinel();
  }

  protected readonly videos = signal<FeedVideo[]>([]);
  protected readonly initialLoading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly hasMore = computed(() => this.nextCursor() !== null);
  protected readonly summaries = signal(new Map<string, VideoSummary>());
  protected readonly creatorNames = signal(new Map<string, string>());
  protected readonly categories = [
    'All',
    'Recently uploaded',
    'Technology',
    'Learning',
    '4K',
    'Live',
  ];

  private readonly feedService = inject(FeedService);
  private readonly engagement = inject(EngagementService);
  private readonly profiles = inject(ProfileService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private sentinel?: ElementRef<HTMLElement>;
  private intersectionObserver?: IntersectionObserver;
  private userHasScrolled = false;
  private sentinelIsVisible = false;
  private activePlayer?: HTMLVideoElement;

  ngOnInit(): void {
    this.loadPage(10, false);
  }

  private observeSentinel(): void {
    if (!this.sentinel || typeof IntersectionObserver === 'undefined') {
      return;
    }

    this.intersectionObserver?.disconnect();
    this.intersectionObserver = new IntersectionObserver(
      ([entry]) => {
        this.sentinelIsVisible = entry.isIntersecting;
        this.tryLoadMore();
      },
      { rootMargin: '500px 0px' },
    );
    this.intersectionObserver.observe(this.sentinel.nativeElement);
  }

  ngOnDestroy(): void {
    this.intersectionObserver?.disconnect();
    this.activePlayer?.pause();
  }

  @HostListener('window:scroll')
  protected onWindowScroll(): void {
    this.userHasScrolled = true;
    this.tryLoadMore();
  }

  protected retry(): void {
    if (this.videos().length === 0) {
      this.initialLoading.set(true);
      this.loadPage(10, false);
      return;
    }

    this.loadPage(10, true);
  }

  protected onPlayStarted(player: HTMLVideoElement): void {
    if (this.activePlayer && this.activePlayer !== player) {
      this.activePlayer.pause();
    }
    this.activePlayer = player;
  }

  protected openWatch(video: FeedVideo): void {
    this.activePlayer?.pause();
    this.activePlayer = undefined;
    void this.router.navigate(['/watch', video.id]);
  }

  private tryLoadMore(): void {
    if (
      this.userHasScrolled &&
      this.sentinelIsVisible &&
      this.nextCursor() &&
      !this.loadingMore() &&
      !this.initialLoading()
    ) {
      this.loadPage(10, true);
    }
  }

  private loadPage(limit: number, append: boolean): void {
    if (append) {
      if (!this.nextCursor() || this.loadingMore()) {
        return;
      }
      this.loadingMore.set(true);
    }
    this.errorMessage.set('');

    this.feedService
      .getPage(limit, append ? this.nextCursor() : null)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          const videos = append ? [...this.videos(), ...page.items] : page.items;
          this.videos.set(videos);
          void this.enrich(videos);
          this.nextCursor.set(page.nextCursor);
          this.initialLoading.set(false);
          this.loadingMore.set(false);
        },
        error: (error: HttpErrorResponse) => {
          const problem = error.error as { detail?: string } | null;
          this.errorMessage.set(
            problem?.detail ?? 'The feed could not be loaded. Check the services and try again.',
          );
          this.initialLoading.set(false);
          this.loadingMore.set(false);
        },
      });
  }

  protected summaryFor(videoId: string): VideoSummary | null {
    return this.summaries().get(videoId) ?? null;
  }

  protected creatorFor(video: FeedVideo): string {
    return (video.ownerId && this.creatorNames().get(video.ownerId)) || 'StreamForge Creator';
  }

  private async enrich(videos: FeedVideo[]): Promise<void> {
    try {
      const [summaries, names] = await Promise.all([
        this.engagement.getSummaries(videos.map((video) => video.id)),
        this.profiles.resolve(videos.map((video) => video.ownerId)),
      ]);
      this.summaries.set(new Map(summaries.map((summary) => [summary.videoId, summary])));
      this.creatorNames.set(names);
    } catch {
      // Feed browsing remains available while optional engagement/profile data recovers.
    }
  }
}
