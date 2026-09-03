import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnDestroy,
  ViewChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FeedRendition, FeedService, FeedVideo } from './feed.service';
import { PlayerLevel, VideoPlayerAdapter } from './video-player.adapter';

export interface PlaybackQualityChanged {
  videoId: string;
  mode: 'auto' | 'manual' | 'native-hls' | 'progressive';
  selectedHeight: number | null;
  activeHeight: number | null;
  bitrateBitsPerSecond: number | null;
}

@Component({
  selector: 'app-video-card',
  templateUrl: './video-card.component.html',
  styleUrl: './video-card.component.scss',
})
export class VideoCardComponent implements AfterViewInit, OnDestroy {
  @ViewChild('player') private player?: ElementRef<HTMLVideoElement>;
  readonly video = input.required<FeedVideo>();
  readonly appearance = input<'watch' | 'grid' | 'recommendation'>('watch');
  readonly autoplay = input(false);
  readonly playStarted = output<HTMLVideoElement>();
  readonly watchRequested = output<FeedVideo>();
  readonly playbackQualityChanged = output<PlaybackQualityChanged>();
  protected readonly sourceUrl = signal('');
  protected readonly liked = signal(false);
  protected readonly descriptionExpanded = signal(false);
  protected readonly commentsOpen = signal(false);
  protected readonly playbackError = signal('');
  protected readonly refreshing = signal(false);
  protected readonly qualityMenuOpen = signal(false);
  protected readonly levels = signal<PlayerLevel[]>([]);
  protected readonly prepared = signal(false);
  protected readonly mode = signal<'auto' | 'manual' | 'native-hls' | 'progressive'>('auto');
  protected readonly selectedHeight = signal<number | null>(null);
  protected readonly activeLevel = signal<PlayerLevel | null>(null);
  protected readonly startingPlayback = signal(false);
  protected readonly autoplayBlocked = signal(false);
  protected readonly startedMuted = signal(false);
  protected readonly playing = signal(false);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly feedService = inject(FeedService);
  private readonly destroyRef = inject(DestroyRef);
  private observer?: IntersectionObserver;
  private adapter?: VideoPlayerAdapter;
  private hlsRetry = false;
  private mp4Retried = false;
  private autoplayAttempted = false;
  private playRequest = 0;

  ngAfterViewInit(): void {
    if (this.appearance() !== 'watch') {
      const preview = this.highest(this.video().renditions);
      if (preview) {
        this.sourceUrl.set(preview.playbackUrl);
        this.prepared.set(true);
      }
      return;
    }
    if (this.autoplay()) {
      this.ensureSource();
      return;
    }
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
    this.adapter?.destroy();
  }
  @HostListener('document:click', ['$event']) closeOutside(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.qualityMenuOpen.set(false);
  }
  @HostListener('document:keydown.escape') closeEscape(): void {
    this.qualityMenuOpen.set(false);
  }
  protected onPlay(): void {
    this.adapter?.start();
    this.autoplayBlocked.set(false);
    this.startingPlayback.set(true);
    if (this.player) this.playStarted.emit(this.player.nativeElement);
  }
  protected onPlaying(): void {
    this.playing.set(true);
    this.startingPlayback.set(false);
    this.autoplayBlocked.set(false);
  }
  protected onPause(): void {
    this.playing.set(false);
    this.startingPlayback.set(false);
  }
  protected startFromOverlay(): void {
    const element = this.player?.nativeElement;
    if (!element) return;
    element.muted = false;
    this.startedMuted.set(false);
    this.requestPlayback(false);
  }
  protected enableSound(): void {
    const element = this.player?.nativeElement;
    if (!element) return;
    element.muted = false;
    this.startedMuted.set(false);
    this.requestPlayback(false);
  }
  protected onVolumeChange(): void {
    if (!this.player?.nativeElement.muted) this.startedMuted.set(false);
  }
  protected toggleLike(): void {
    this.liked.update((v) => !v);
  }
  protected toggleComments(): void {
    this.commentsOpen.update((v) => !v);
  }
  protected toggleDescription(): void {
    this.descriptionExpanded.update((v) => !v);
  }
  protected toggleQualityMenu(event: Event): void {
    event.stopPropagation();
    this.qualityMenuOpen.update((v) => !v);
  }
  protected openWatch(): void {
    this.watchRequested.emit(this.video());
  }
  protected creatorInitial(): string {
    return this.video().title.trim().charAt(0).toUpperCase() || 'S';
  }
  protected selectAuto(): void {
    const changed = this.mode() !== 'auto';
    this.mode.set('auto');
    this.selectedHeight.set(null);
    this.adapter?.select(-1);
    this.qualityMenuOpen.set(false);
    if (changed) this.emitQuality();
  }
  protected selectLevel(level: PlayerLevel): void {
    const changed = this.mode() !== 'manual' || this.selectedHeight() !== level.height;
    this.mode.set('manual');
    this.selectedHeight.set(level.height);
    this.adapter?.select(level.index);
    this.qualityMenuOpen.set(false);
    if (changed) this.emitQuality();
  }
  protected qualityLabel(): string {
    const active = this.activeLevel()?.height;
    const all = this.levels();
    if (this.mode() === 'progressive')
      return `MP4 · ${active ?? this.highest(this.video().renditions)?.height ?? '?'}p`;
    if (this.mode() === 'native-hls') return 'Auto';
    if (all.length === 1) return `Auto · ${all[0].height}p`;
    if (this.mode() === 'manual') return `${this.selectedHeight()}p`;
    return active ? `Auto · ${active}p` : 'Auto';
  }
  protected qualityDisabled(): boolean {
    return (
      this.mode() === 'native-hls' || this.mode() === 'progressive' || this.levels().length <= 1
    );
  }
  protected onPlaybackError(): void {
    if (this.mode() !== 'progressive') {
      this.recoverHls();
      return;
    }
    if (!this.mp4Retried) {
      this.mp4Retried = true;
      this.refreshRenditions();
      return;
    }
    this.playbackError.set('This video could not be loaded. Try again later.');
  }
  protected relativeUploadTime(): string {
    const seconds = Math.max(
      0,
      Math.floor((Date.now() - Date.parse(this.video().uploadedAtUtc)) / 1000),
    );
    if (seconds < 60) return 'just now';
    const minutes = Math.floor(seconds / 60);
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
    const manifest = this.video().hlsManifestUrl;
    if (manifest && this.player) {
      this.attachHls(manifest);
      return;
    }
    this.useProgressive();
  }
  private attachHls(url: string): void {
    if (!this.player) return;
    this.sourceUrl.set('');
    this.adapter = new VideoPlayerAdapter(this.player.nativeElement, {
      levels: (levels) => {
        this.levels.set(levels);
        this.prepared.set(true);
        queueMicrotask(() => this.beginAutoplay());
      },
      active: (level) => {
        this.activeLevel.set(level);
        this.emitQuality();
      },
      fatal: () => this.recoverHls(),
    });
    const kind = this.adapter.attach(url);
    if (kind === 'native') {
      this.mode.set('native-hls');
      this.sourceUrl.set(url);
      this.prepared.set(true);
      this.emitQuality();
    } else if (kind === 'unavailable') this.useProgressive();
    else {
      this.mode.set('auto');
      if (this.autoplay()) {
        this.startingPlayback.set(true);
        this.adapter.start();
      }
      this.emitQuality();
    }
  }
  private recoverHls(): void {
    if (!this.player) return;
    const element = this.player.nativeElement;
    const time = element.currentTime;
    const paused = element.paused;
    const manifest = this.video().hlsManifestUrl;
    if (!this.hlsRetry && manifest) {
      this.hlsRetry = true;
      this.adapter?.destroy();
      this.prepared.set(false);
      this.attachHls(manifest);
      if (!paused) this.adapter?.start();
      element.addEventListener(
        'loadedmetadata',
        () => {
          element.currentTime = time;
          if (!paused) void element.play();
        },
        { once: true },
      );
      return;
    }
    this.useProgressive(time, paused);
  }
  private useProgressive(time = 0, paused = true): void {
    this.adapter?.destroy();
    const rendition = this.bestProgressive();
    if (!rendition) {
      this.playbackError.set('No playable rendition is available.');
      return;
    }
    this.mode.set('progressive');
    this.activeLevel.set({ index: -1, height: rendition.height, bitrate: 0 });
    const expiresSoon = Date.parse(rendition.playbackUrlExpiresAtUtc) <= Date.now() + 300000;
    if (expiresSoon) {
      this.refreshRenditions(time, paused);
      return;
    }
    this.sourceUrl.set(rendition.playbackUrl);
    this.prepared.set(true);
    queueMicrotask(() => {
      const element = this.player?.nativeElement;
      if (element) {
        element.load();
        this.beginAutoplay();
        element.addEventListener(
          'loadedmetadata',
          () => {
            element.currentTime = time;
            if (!paused) void element.play();
          },
          { once: true },
        );
      }
    });
    this.emitQuality();
  }
  private bestProgressive(values = this.video().renditions): FeedRendition | undefined {
    const width = this.player?.nativeElement.clientWidth ?? 0;
    const height = this.player?.nativeElement.clientHeight ?? 0;
    const sorted = [...values].sort((a, b) => a.height - b.height || a.width - b.width);
    if (width <= 0 || height <= 0) return sorted.at(-1);
    return sorted.find((x) => x.width >= width && x.height >= height) ?? sorted.at(-1);
  }
  private refreshRenditions(time = 0, paused = true): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.feedService
      .refreshRenditions(this.video().id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (renditions) => {
          const selected = this.bestProgressive(renditions);
          if (selected) {
            this.playbackError.set('');
            this.sourceUrl.set(selected.playbackUrl);
            this.prepared.set(true);
            queueMicrotask(() => {
              const element = this.player?.nativeElement;
              if (element) {
                element.load();
                this.beginAutoplay();
                element.addEventListener(
                  'loadedmetadata',
                  () => {
                    element.currentTime = time;
                    if (!paused) void element.play();
                  },
                  { once: true },
                );
              }
            });
          } else this.playbackError.set('No playable rendition is available.');
          this.refreshing.set(false);
        },
        error: () => {
          this.playbackError.set('The playback link could not be refreshed.');
          this.refreshing.set(false);
        },
      });
  }
  private beginAutoplay(): void {
    if (!this.autoplay() || this.autoplayAttempted) return;
    this.autoplayAttempted = true;
    this.requestPlayback(true);
  }
  private requestPlayback(allowMutedFallback: boolean): void {
    const element = this.player?.nativeElement;
    if (!element) return;
    this.adapter?.start();
    this.autoplayBlocked.set(false);
    this.startingPlayback.set(true);
    const request = ++this.playRequest;
    let playResult: Promise<void>;
    try {
      playResult = element.play();
    } catch (error: unknown) {
      this.handlePlayFailure(element, error, allowMutedFallback, request);
      return;
    }
    void playResult.catch((error: unknown) => {
      this.handlePlayFailure(element, error, allowMutedFallback, request);
    });
  }
  private handlePlayFailure(
    element: HTMLVideoElement,
    error: unknown,
    allowMutedFallback: boolean,
    request: number,
  ): void {
    if (request !== this.playRequest) return;
    if (allowMutedFallback && !element.muted && this.isAutoplayPolicyError(error)) {
      element.muted = true;
      this.startedMuted.set(true);
      this.requestPlayback(false);
      return;
    }
    this.startingPlayback.set(false);
    this.autoplayBlocked.set(true);
  }
  private isAutoplayPolicyError(error: unknown): boolean {
    return (
      typeof error === 'object' &&
      error !== null &&
      'name' in error &&
      error.name === 'NotAllowedError'
    );
  }
  private highest(values: FeedRendition[]): FeedRendition | undefined {
    return [...values].sort((a, b) => b.height - a.height || b.width - a.width)[0];
  }
  private emitQuality(): void {
    const active = this.activeLevel();
    this.playbackQualityChanged.emit({
      videoId: this.video().id,
      mode: this.mode(),
      selectedHeight: this.selectedHeight(),
      activeHeight: active?.height ?? null,
      bitrateBitsPerSecond: this.mode() === 'progressive' ? null : (active?.bitrate ?? null),
    });
  }
}
