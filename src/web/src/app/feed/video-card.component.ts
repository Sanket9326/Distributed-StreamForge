import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  ViewChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FeedRendition, FeedService, FeedVideo } from './feed.service';

@Component({
  selector: 'app-video-card',
  templateUrl: './video-card.component.html',
  styleUrl: './video-card.component.scss',
})
export class VideoCardComponent implements AfterViewInit, OnDestroy {
  @ViewChild('player') private player?: ElementRef<HTMLVideoElement>;

  readonly video = input.required<FeedVideo>();
  readonly playStarted = output<HTMLVideoElement>();

  protected readonly sourceUrl = signal('');
  protected readonly liked = signal(false);
  protected readonly descriptionExpanded = signal(false);
  protected readonly commentsOpen = signal(false);
  protected readonly playbackError = signal('');
  protected readonly refreshing = signal(false);

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly feedService = inject(FeedService);
  private readonly destroyRef = inject(DestroyRef);
  private observer?: IntersectionObserver;
  private retriedPlayback = false;

  ngAfterViewInit(): void {
    if (typeof IntersectionObserver === 'undefined') {
      this.ensureSource();
      return;
    }

    this.observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          this.ensureSource();
          this.observer?.disconnect();
        }
      },
      { rootMargin: '400px 0px' },
    );
    this.observer.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  protected onPlay(): void {
    if (this.player) {
      this.playStarted.emit(this.player.nativeElement);
    }
  }

  protected toggleLike(): void {
    this.liked.update((value) => !value);
  }

  protected toggleComments(): void {
    this.commentsOpen.update((value) => !value);
  }

  protected toggleDescription(): void {
    this.descriptionExpanded.update((value) => !value);
  }

  protected onPlaybackError(): void {
    if (!this.retriedPlayback) {
      this.retriedPlayback = true;
      this.refreshRenditions();
      return;
    }

    this.playbackError.set('This video could not be loaded. Try again later.');
  }

  protected relativeUploadTime(): string {
    const elapsedSeconds = Math.max(
      0,
      Math.floor((Date.now() - Date.parse(this.video().uploadedAtUtc)) / 1000),
    );
    if (elapsedSeconds < 60) return 'just now';
    const minutes = Math.floor(elapsedSeconds / 60);
    if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;
    const days = Math.floor(hours / 24);
    if (days < 30) return `${days} day${days === 1 ? '' : 's'} ago`;
    return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(
      new Date(this.video().uploadedAtUtc),
    );
  }

  protected hasLongDescription(): boolean {
    return (this.video().description?.length ?? 0) > 180;
  }

  private ensureSource(): void {
    const highest = this.highest(this.video().renditions);
    if (!highest) {
      this.playbackError.set('No playable rendition is available.');
      return;
    }

    const expiresSoon = Date.parse(highest.playbackUrlExpiresAtUtc) <= Date.now() + 5 * 60 * 1000;
    if (expiresSoon) {
      this.refreshRenditions();
    } else {
      this.sourceUrl.set(highest.playbackUrl);
    }
  }

  private refreshRenditions(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.feedService
      .refreshRenditions(this.video().id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (renditions) => {
          const highest = this.highest(renditions);
          if (!highest) {
            this.playbackError.set('No playable rendition is available.');
          } else {
            this.playbackError.set('');
            this.sourceUrl.set(highest.playbackUrl);
            queueMicrotask(() => this.player?.nativeElement.load());
          }
          this.refreshing.set(false);
        },
        error: () => {
          this.playbackError.set('The playback link could not be refreshed.');
          this.refreshing.set(false);
        },
      });
  }

  private highest(renditions: FeedRendition[]): FeedRendition | undefined {
    return [...renditions].sort(
      (left, right) => right.height - left.height || right.width - left.width,
    )[0];
  }
}
