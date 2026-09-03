import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedVideo } from './feed.service';
import { VideoCardComponent } from './video-card.component';

describe('VideoCardComponent', () => {
  let fixture: ComponentFixture<VideoCardComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => undefined);
    await TestBed.configureTestingModule({
      imports: [VideoCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(VideoCardComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('selects the highest rendition and keeps social actions local', () => {
    fixture.componentRef.setInput('video', video());
    fixture.detectChanges();

    const player = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    expect(player.getAttribute('src')).toBe('https://storage.test/1080.mp4');

    findButton('Like').click();
    fixture.detectChanges();
    expect(findButton('Liked').getAttribute('aria-pressed')).toBe('true');

    findButton('Comment').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Comments are coming');
    http.expectNone(() => true);
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

  function findButton(label: string): HTMLButtonElement {
    return (
      Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
    ).find((button) => button.textContent?.includes(label))!;
  }

  function video(): FeedVideo {
    return {
      id: 'e2c1bb10-4340-452f-9fc6-a68cf4b12457',
      title: 'Demo video',
      description: 'A useful description',
      hashtags: ['dotnet'],
      uploadedAtUtc: new Date(Date.now() - 60_000).toISOString(),
      availableAtUtc: new Date().toISOString(),
      renditions: [
        {
          tier: '480p',
          width: 854,
          height: 480,
          videoCodec: 'h264',
          audioCodec: 'aac',
          contentType: 'video/mp4',
          sizeBytes: 10,
          playbackUrl: 'https://storage.test/480.mp4',
          playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        },
        {
          tier: '1080p',
          width: 1920,
          height: 1080,
          videoCodec: 'h264',
          audioCodec: 'aac',
          contentType: 'video/mp4',
          sizeBytes: 20,
          playbackUrl: 'https://storage.test/1080.mp4',
          playbackUrlExpiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        },
      ],
    };
  }
});
