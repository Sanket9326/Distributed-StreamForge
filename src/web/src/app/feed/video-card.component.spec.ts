import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService, AuthUser } from '../auth/auth.service';
import {
  CommentPage,
  EngagementService,
  VideoComment,
  VideoSummary,
} from '../engagement/engagement.service';
import { ProfileService } from '../profiles/profile.service';
import { FeedVideo } from './feed.service';
import { VideoCardComponent } from './video-card.component';

describe('VideoCardComponent', () => {
  let fixture: ComponentFixture<VideoCardComponent>;
  let http: HttpTestingController;
  let auth: AuthStub;
  let engagement: EngagementStub;

  beforeEach(async () => {
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => undefined);
    auth = new AuthStub();
    engagement = new EngagementStub();
    await TestBed.configureTestingModule({
      imports: [VideoCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: EngagementService, useValue: engagement },
        { provide: ProfileService, useValue: { resolve: vi.fn().mockResolvedValue(new Map()) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(VideoCardComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('selects the highest rendition and rolls back a failed optimistic like', async () => {
    auth.user.set(user());
    engagement.setReaction.mockRejectedValueOnce(new Error('offline'));
    fixture.componentRef.setInput('video', video());
    fixture.componentRef.setInput('summary', summary({ likeCount: 4 }));
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalled());
    fixture.detectChanges();

    const player = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    expect(player.getAttribute('src')).toBe('https://storage.test/1080.mp4');

    action('Like video').click();
    fixture.detectChanges();
    expect(action('Like video').getAttribute('aria-pressed')).toBe('true');
    expect(action('Like video').textContent).toContain('5');

    await vi.waitFor(() => expect(fixture.nativeElement.textContent).toContain('not saved'));
    fixture.detectChanges();
    expect(action('Like video').getAttribute('aria-pressed')).toBe('false');
    expect(action('Like video').textContent).toContain('4');
  });

  it('sends guests to login with the watch return URL', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.componentRef.setInput('video', video());
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalled());

    action('Like video').click();
    await vi.waitFor(() =>
      expect(navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: `/watch/${video().id}` },
      }),
    );
  });

  it('submits one qualified view after ten cumulative seconds of playback', async () => {
    let now = 1_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    fixture.componentRef.setInput('video', video());
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalled());
    const player = fixture.nativeElement.querySelector('video') as HTMLVideoElement;

    player.dispatchEvent(new Event('playing'));
    now += 6_000;
    player.dispatchEvent(new Event('pause'));
    now += 20_000;
    player.dispatchEvent(new Event('playing'));
    now += 4_001;
    player.dispatchEvent(new Event('timeupdate'));

    await vi.waitFor(() => expect(engagement.recordView).toHaveBeenCalledOnce());
    expect(engagement.recordView.mock.calls[0][0]).toBe(video().id);
    player.dispatchEvent(new Event('timeupdate'));
    expect(engagement.recordView).toHaveBeenCalledOnce();
  });

  it('keeps the last known view count without showing an error when view reporting fails', async () => {
    let now = 1_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    engagement.recordView.mockRejectedValueOnce(new Error('offline'));
    fixture.componentRef.setInput('video', video());
    fixture.componentRef.setInput('summary', summary({ viewCount: 42 }));
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalled());
    const player = fixture.nativeElement.querySelector('video') as HTMLVideoElement;

    player.dispatchEvent(new Event('playing'));
    now += 10_001;
    player.dispatchEvent(new Event('timeupdate'));

    await vi.waitFor(() => expect(engagement.recordView).toHaveBeenCalledOnce());
    fixture.detectChanges();
    const description = fixture.nativeElement.querySelector('.description-card') as HTMLElement;
    expect(description.textContent).toContain('42 views');
    expect(fixture.nativeElement.textContent).not.toContain('view count will retry');
  });

  it('shares the stable watch URL instead of the signed media URL', async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'share', { configurable: true, value: share });
    fixture.componentRef.setInput('video', video());
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalled());

    findButton('Share').click();

    await vi.waitFor(() => expect(share).toHaveBeenCalled());
    expect(share.mock.calls[0][0].url).toBe(`${window.location.origin}/watch/${video().id}`);
    expect(share.mock.calls[0][0].url).not.toContain('storage.test');
  });

  it('loads another cursor page and appends its comments', async () => {
    const first = comment('First page');
    const second = comment('Older comment');
    engagement.getComments
      .mockResolvedValueOnce({ items: [first], totalCount: 2, nextCursor: 'opaque' })
      .mockResolvedValueOnce({ items: [second], totalCount: 2, nextCursor: null });
    fixture.componentRef.setInput('video', video());
    fixture.detectChanges();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalledOnce());
    fixture.detectChanges();

    findButton('Load more comments').click();
    await vi.waitFor(() => expect(engagement.getComments).toHaveBeenCalledTimes(2));
    fixture.detectChanges();

    expect(engagement.getComments).toHaveBeenLastCalledWith(video().id, 'opaque');
    expect(fixture.nativeElement.textContent).toContain('First page');
    expect(fixture.nativeElement.textContent).toContain('Older comment');
  });

  it('refreshes a rendition URL that is near expiry', () => {
    const expiring = video();
    expiring.renditions[1].playbackUrlExpiresAtUtc = new Date(Date.now() + 30_000).toISOString();
    fixture.componentRef.setInput('video', expiring);
    fixture.detectChanges();

    const request = http.expectOne(`/api/feed/videos/${expiring.id}/renditions`);
    request.flush([
      {
        ...expiring.renditions[1],
        playbackUrl: 'https://storage.test/refreshed.mp4',
        playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('video').getAttribute('src')).toBe(
      'https://storage.test/refreshed.mp4',
    );
  });

  it('starts playback automatically when opened from the feed', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
    fixture.componentRef.setInput('video', video());
    fixture.componentRef.setInput('autoplay', true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(play).toHaveBeenCalledOnce();
  });

  it('falls back to muted playback when the browser blocks audible autoplay', async () => {
    const play = vi
      .spyOn(HTMLMediaElement.prototype, 'play')
      .mockRejectedValueOnce(new DOMException('Autoplay blocked', 'NotAllowedError'))
      .mockResolvedValueOnce(undefined);
    fixture.componentRef.setInput('video', video());
    fixture.componentRef.setInput('autoplay', true);
    fixture.detectChanges();
    await fixture.whenStable();

    const player = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    await vi.waitFor(() => expect(play).toHaveBeenCalledTimes(2));
    expect(player.muted).toBe(true);

    player.dispatchEvent(new Event('playing'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tap for sound');
  });

  function action(label: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`button[aria-label="${label}"]`)!;
  }

  function findButton(label: string): HTMLButtonElement {
    return (
      Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
    ).find((button) => button.textContent?.includes(label))!;
  }
});

class AuthStub {
  readonly user = signal<AuthUser | null>(null);
  readonly initialize = vi.fn().mockResolvedValue(undefined);
}

class EngagementStub {
  readonly getReaction = vi.fn().mockResolvedValue('none');
  readonly setReaction = vi.fn().mockResolvedValue({
    reaction: 'like', likeCount: 5, dislikeCount: 0, countsPending: false,
  });
  readonly recordView = vi.fn().mockResolvedValue({ counted: true, viewCount: 1, countsPending: false });
  readonly getComments = vi.fn<(_videoId: string, _cursor: string | null) => Promise<CommentPage>>()
    .mockResolvedValue({ items: [], totalCount: 0, nextCursor: null });
}

function user(): AuthUser {
  return { id: 'b1cfdf8b-82b1-4734-b742-ae0a4941a09b', username: 'sanket', email: 's@example.test' };
}

function summary(changes: Partial<VideoSummary> = {}): VideoSummary {
  return { videoId: video().id, likeCount: 0, dislikeCount: 0, viewCount: 0, commentCount: 0, ...changes };
}

function comment(body: string): VideoComment {
  return {
    id: crypto.randomUUID(), videoId: video().id, authorId: user().id, body,
    createdAtUtc: new Date().toISOString(), updatedAtUtc: new Date().toISOString(),
  };
}

function video(): FeedVideo {
  return {
    id: 'e2c1bb10-4340-452f-9fc6-a68cf4b12457',
    ownerId: null,
    title: 'Demo video',
    description: 'A useful description',
    hashtags: ['dotnet'],
    uploadedAtUtc: new Date(Date.now() - 60_000).toISOString(),
    availableAtUtc: new Date().toISOString(),
    renditions: [
      {
        tier: '480p', width: 854, height: 480, videoCodec: 'h264', audioCodec: 'aac',
        contentType: 'video/mp4', sizeBytes: 10, playbackUrl: 'https://storage.test/480.mp4',
        playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
      },
      {
        tier: '1080p', width: 1920, height: 1080, videoCodec: 'h264', audioCodec: 'aac',
        contentType: 'video/mp4', sizeBytes: 20, playbackUrl: 'https://storage.test/1080.mp4',
        playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
      },
    ],
  };
}
