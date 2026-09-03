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
  protected readonly selectedVideo = signal<FeedVideo | null>(null);
  protected readonly recommendations = computed(() => {
    const selected = this.selectedVideo();
    return selected ? this.videos().filter((video) => video.id !== selected.id) : [];
  });
  protected readonly categories = [
    'All',
    'Recently uploaded',
    'Technology',
    'Learning',
    '4K',
    'Live',
  ];

  private readonly feedService = inject(FeedService);
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
    this.selectedVideo.set(video);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected closeWatch(): void {
    this.activePlayer?.pause();
    this.activePlayer = undefined;
    this.selectedVideo.set(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
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
          this.videos.set(append ? [...this.videos(), ...page.items] : page.items);
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
}
