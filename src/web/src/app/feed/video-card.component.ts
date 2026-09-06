import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FeedRendition, FeedService, FeedVideo } from './feed.service';
import { PlayerLevel, VideoPlayerAdapter } from './video-player.adapter';
import {
  EngagementService,
  ReactionValue,
  VideoComment,
  VideoSummary,
} from '../engagement/engagement.service';
import { AuthService } from '../auth/auth.service';
import { Router } from '@angular/router';
import { ProfileService } from '../profiles/profile.service';

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
export class VideoCardComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('player') private player?: ElementRef<HTMLVideoElement>;
  readonly video = input.required<FeedVideo>();
  readonly appearance = input<'watch' | 'grid' | 'recommendation'>('watch');
  readonly autoplay = input(false);
  readonly summary = input<VideoSummary | null>(null);
  readonly creatorName = input('StreamForge Creator');
  readonly playStarted = output<HTMLVideoElement>();
  readonly watchRequested = output<FeedVideo>();
  readonly playbackQualityChanged = output<PlaybackQualityChanged>();
  protected readonly sourceUrl = signal('');
  protected readonly descriptionExpanded = signal(false);
  protected readonly reaction = signal<ReactionValue>('none');
  protected readonly reactionBusy = signal(false);
  protected readonly reactionAnimating = signal(false);
  protected readonly likeCount = signal(0);
  protected readonly viewCount = signal(0);
  protected readonly commentCount = signal(0);
  protected readonly socialError = signal('');
  protected readonly shareStatus = signal('');
  protected readonly comments = signal<VideoComment[]>([]);
  protected readonly commentAuthors = signal(new Map<string, string>());
  protected readonly commentsLoading = signal(false);
  protected readonly commentsLoadingMore = signal(false);
  protected readonly commentsCursor = signal<string | null>(null);
  protected readonly commentDraft = signal('');
  protected readonly commentBusy = signal(false);
  protected readonly editingCommentId = signal<string | null>(null);
  protected readonly editingBody = signal('');
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
  protected readonly auth = inject(AuthService);
  private readonly engagement = inject(EngagementService);
  private readonly profiles = inject(ProfileService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private observer?: IntersectionObserver;
  private adapter?: VideoPlayerAdapter;
  private hlsRetry = false;
  private mp4Retried = false;
  private autoplayAttempted = false;
  private playRequest = 0;
  private watchedMilliseconds = 0;
  private playingStartedAt: number | null = null;
  private viewSubmitted = false;
  private readonly viewSessionId = globalThis.crypto?.randomUUID?.() ??
    `${Date.now()}-${Math.random().toString(16).slice(2)}`;

  ngOnInit(): void {
    const summary = this.summary();
    this.likeCount.set(summary?.likeCount ?? 0);
    this.viewCount.set(summary?.viewCount ?? 0);
    this.commentCount.set(summary?.commentCount ?? 0);
    if (this.appearance() === 'watch') void this.initializeEngagement();
  }

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
    this.stopWatchClock();
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
    this.playingStartedAt ??= Date.now();
  }
  protected onPause(): void {
    this.stopWatchClock();
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
  protected onWaiting(): void {
    this.stopWatchClock();
  }

  protected onTimeUpdate(): void {
    if (this.viewSubmitted || this.playingStartedAt === null) return;
    if (this.watchedMilliseconds + Date.now() - this.playingStartedAt >= 10_000) {
      this.stopWatchClock();
      this.viewSubmitted = true;
      void this.submitQualifiedView();
    }
  }

  protected chooseReaction(value: 'like' | 'dislike'): void {
    if (this.reactionBusy()) return;
    void this.updateReaction(this.reaction() === value ? 'none' : value);
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
    return this.creatorName().trim().charAt(0).toUpperCase() || 'S';
  }

  protected compactCount(value: number): string {
    return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  }

  protected async shareVideo(): Promise<void> {
    const url = `${window.location.origin}/watch/${this.video().id}`;
    this.shareStatus.set('');
    try {
      if (navigator.share) await navigator.share({ title: this.video().title, url });
      else {
        await navigator.clipboard.writeText(url);
        this.shareStatus.set('Link copied');
      }
    } catch (error: unknown) {
      if (!(error instanceof DOMException && error.name === 'AbortError'))
        this.shareStatus.set('Could not share this link');
    }
  }

  protected onCommentInput(event: Event): void {
    this.commentDraft.set((event.target as HTMLTextAreaElement).value);
  }

  protected onEditInput(event: Event): void {
    this.editingBody.set((event.target as HTMLTextAreaElement).value);
  }

  protected addComment(): void {
    if (!this.auth.user()) { this.goToLogin(); return; }
    if (!this.commentDraft().trim() || this.commentBusy()) return;
    void this.createComment();
  }

  protected startEditing(comment: VideoComment): void {
    this.editingCommentId.set(comment.id);
    this.editingBody.set(comment.body);
  }

  protected cancelEditing(): void {
    this.editingCommentId.set(null);
    this.editingBody.set('');
  }

  protected saveComment(comment: VideoComment): void {
    if (!this.editingBody().trim() || this.commentBusy()) return;
    void this.updateComment(comment);
  }

  protected removeComment(comment: VideoComment): void {
    if (!window.confirm('Delete this comment permanently?')) return;
    void this.deleteComment(comment);
  }

  protected loadMoreComments(): void {
    if (this.commentsCursor() && !this.commentsLoadingMore()) void this.loadComments(true);
  }

  protected canManage(comment: VideoComment): boolean {
    return this.auth.user()?.id === comment.authorId;
  }

  protected commentAuthor(comment: VideoComment): string {
    return this.commentAuthors().get(comment.authorId) ?? 'StreamForge user';
  }

  protected commentInitial(comment: VideoComment): string {
    return this.commentAuthor(comment).charAt(0).toUpperCase() || 'S';
  }

  protected isEdited(comment: VideoComment): boolean {
    return comment.updatedAtUtc !== comment.createdAtUtc;
  }

  protected relativeTime(value: string): string {
    const seconds = Math.max(0, Math.floor((Date.now() - Date.parse(value)) / 1000));
    if (seconds < 60) return 'just now';
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
    return `${Math.floor(seconds / 86400)}d ago`;
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

  private async initializeEngagement(): Promise<void> {
    await this.auth.initialize();
    if (this.auth.user()) {
      try { this.reaction.set(await this.engagement.getReaction(this.video().id)); }
      catch { /* Counts and comments remain usable if personal state is temporarily unavailable. */ }
    }
    await this.loadComments(false);
  }

  private async updateReaction(next: ReactionValue): Promise<void> {
    if (!this.auth.user()) await this.auth.initialize();
    if (!this.auth.user()) { this.goToLogin(); return; }
    const previous = this.reaction();
    const previousCount = this.likeCount();
    this.reaction.set(next);
    if (previous === 'like' && next !== 'like') this.likeCount.update((count) => Math.max(0, count - 1));
    else if (previous !== 'like' && next === 'like') this.likeCount.update((count) => count + 1);
    this.reactionBusy.set(true);
    this.reactionAnimating.set(true);
    this.socialError.set('');
    setTimeout(() => this.reactionAnimating.set(false), 280);
    try {
      const response = await this.engagement.setReaction(this.video().id, next);
      this.reaction.set(response.reaction);
      if (response.likeCount !== null) this.likeCount.set(response.likeCount);
    } catch {
      this.reaction.set(previous);
      this.likeCount.set(previousCount);
      this.socialError.set('Your reaction was not saved. Please try again.');
    } finally {
      this.reactionBusy.set(false);
    }
  }

  private async submitQualifiedView(): Promise<void> {
    try {
      const response = await this.engagement.recordView(this.video().id, this.viewSessionId);
      if (response.viewCount !== null) this.viewCount.set(response.viewCount);
      else if (response.counted) this.viewCount.update((count) => count + 1);
    } catch {
      this.viewSubmitted = false;
      // Keep the last summary count visible. View reporting retries silently while
      // playback continues because a temporary analytics failure should not
      // replace otherwise valid engagement data with an error message.
    }
  }

  private stopWatchClock(): void {
    if (this.playingStartedAt === null) return;
    this.watchedMilliseconds += Date.now() - this.playingStartedAt;
    this.playingStartedAt = null;
  }

  private async loadComments(append: boolean): Promise<void> {
    if (append) this.commentsLoadingMore.set(true);
    else this.commentsLoading.set(true);
    try {
      const page = await this.engagement.getComments(
        this.video().id,
        append ? this.commentsCursor() : null,
      );
      this.comments.set(append ? [...this.comments(), ...page.items] : page.items);
      this.commentCount.set(page.totalCount);
      this.commentsCursor.set(page.nextCursor);
      await this.resolveCommentAuthors(page.items);
    } catch {
      this.socialError.set('Comments could not be loaded. Please try again.');
    } finally {
      this.commentsLoading.set(false);
      this.commentsLoadingMore.set(false);
    }
  }

  private async createComment(): Promise<void> {
    this.commentBusy.set(true);
    this.socialError.set('');
    try {
      const result = await this.engagement.createComment(this.video().id, this.commentDraft());
      this.comments.update((comments) => [result.comment, ...comments]);
      this.commentCount.set(result.commentCount);
      this.commentDraft.set('');
      await this.resolveCommentAuthors([result.comment]);
    } catch { this.socialError.set('Your comment was not added. Please try again.'); }
    finally { this.commentBusy.set(false); }
  }

  private async updateComment(comment: VideoComment): Promise<void> {
    this.commentBusy.set(true);
    this.socialError.set('');
    try {
      const result = await this.engagement.updateComment(comment.id, this.editingBody());
      this.comments.update((comments) => comments.map((item) => item.id === comment.id ? result.comment : item));
      this.cancelEditing();
    } catch { this.socialError.set('Your comment was not updated. Please try again.'); }
    finally { this.commentBusy.set(false); }
  }

  private async deleteComment(comment: VideoComment): Promise<void> {
    this.commentBusy.set(true);
    this.socialError.set('');
    try {
      const count = await this.engagement.deleteComment(comment.id);
      this.comments.update((comments) => comments.filter((item) => item.id !== comment.id));
      this.commentCount.set(count);
    } catch { this.socialError.set('Your comment was not deleted. Please try again.'); }
    finally { this.commentBusy.set(false); }
  }

  private async resolveCommentAuthors(comments: VideoComment[]): Promise<void> {
    try {
      const names = await this.profiles.resolve(comments.map((comment) => comment.authorId));
      this.commentAuthors.update((current) => new Map([...current, ...names]));
    } catch { /* Generic author labels remain available. */ }
  }

  private goToLogin(): void {
    void this.router.navigate(['/login'], { queryParams: { returnUrl: `/watch/${this.video().id}` } });
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
