import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HomeFeedPage } from './home-feed.page';
import { provideRouter, Router } from '@angular/router';

describe('HomeFeedPage', () => {
  let fixture: ComponentFixture<HomeFeedPage>;
  let http: HttpTestingController;
  let originalObserver: typeof IntersectionObserver | undefined;
  let observers: MockIntersectionObserver[];

  beforeEach(async () => {
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => undefined);
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
    originalObserver = globalThis.IntersectionObserver;
    observers = [];
    class TestObserver extends MockIntersectionObserver {
      constructor(callback: IntersectionObserverCallback) {
        super(callback);
        observers.push(this);
      }
    }
    globalThis.IntersectionObserver = TestObserver as unknown as typeof IntersectionObserver;
    await TestBed.configureTestingModule({
      imports: [HomeFeedPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(HomeFeedPage);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    if (originalObserver) globalThis.IntersectionObserver = originalObserver;
    else
      delete (globalThis as { IntersectionObserver?: typeof IntersectionObserver })
        .IntersectionObserver;
    vi.restoreAllMocks();
  });

  it('loads the first grid page and waits for user scroll before requesting more', () => {
    const initial = http.expectOne(
      (request) => request.url === '/api/feed/videos' && request.params.get('limit') === '10',
    );
    initial.flush({ items: [video()], nextCursor: 'opaque-cursor' });
    http.expectOne('/api/engagement/videos/summaries?ids=e2c1bb10-4340-452f-9fc6-a68cf4b12457')
      .flush([{ videoId: video().id, likeCount: 0, dislikeCount: 0, viewCount: 0, commentCount: 0 }]);
    fixture.detectChanges();

    const sentinelObserver = observers.find((observer) =>
      observer.target?.classList.contains('pagination-sentinel'),
    )!;
    sentinelObserver.trigger(true);
    http.expectNone(
      (request) => request.url === '/api/feed/videos' && request.params.get('limit') === '10',
    );

    window.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();
    const next = http.expectOne(
      (request) =>
        request.url === '/api/feed/videos' &&
        request.params.get('limit') === '10' &&
        request.params.get('cursor') === 'opaque-cursor',
    );
    next.flush({ items: [], nextCursor: null });
    http.expectOne('/api/engagement/videos/summaries?ids=e2c1bb10-4340-452f-9fc6-a68cf4b12457')
      .flush([{ videoId: video().id, likeCount: 0, dislikeCount: 0, viewCount: 0, commentCount: 0 }]);
  });

  it('navigates a browse card to its stable watch route', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const initial = http.expectOne(
      (request) => request.url === '/api/feed/videos' && request.params.get('limit') === '10',
    );
    initial.flush({ items: [video()], nextCursor: null });
    http.expectOne('/api/engagement/videos/summaries?ids=e2c1bb10-4340-452f-9fc6-a68cf4b12457')
      .flush([{ videoId: video().id, likeCount: 0, dislikeCount: 0, viewCount: 0, commentCount: 0 }]);
    fixture.detectChanges();

    const browseLink = fixture.nativeElement.querySelector('.browse-link') as HTMLButtonElement;
    browseLink.click();
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/watch', video().id]);
  });

  function video() {
    return {
      id: 'e2c1bb10-4340-452f-9fc6-a68cf4b12457',
      ownerId: null,
      title: 'First video',
      description: null,
      hashtags: [],
      uploadedAtUtc: new Date().toISOString(),
      availableAtUtc: new Date().toISOString(),
      renditions: [
        {
          tier: '1080p',
          width: 1920,
          height: 1080,
          videoCodec: 'h264',
          audioCodec: 'aac',
          contentType: 'video/mp4',
          sizeBytes: 100,
          playbackUrl: 'https://storage.test/video.mp4',
          playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        },
      ],
    };
  }
});

class MockIntersectionObserver {
  readonly root = null;
  readonly rootMargin = '';
  readonly thresholds = [0];
  target?: Element;

  constructor(private readonly callback: IntersectionObserverCallback) {}

  observe(target: Element): void {
    this.target = target;
  }
  unobserve(): void {}
  disconnect(): void {}
  takeRecords(): IntersectionObserverEntry[] {
    return [];
  }

  trigger(isIntersecting: boolean): void {
    this.callback(
      [{ isIntersecting, target: this.target! } as IntersectionObserverEntry],
      this as never,
    );
  }
}
