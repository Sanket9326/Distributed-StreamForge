import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { EngagementService } from '../engagement/engagement.service';
import { ProfileService } from '../profiles/profile.service';
import { WatchPage } from './watch.page';

describe('WatchPage', () => {
  it('loads a deep-linked video and resolves its creator name', async () => {
    const videoId = 'e2c1bb10-4340-452f-9fc6-a68cf4b12457';
    const ownerId = 'b1cfdf8b-82b1-4734-b742-ae0a4941a09b';
    const engagement = {
      getSummaries: vi.fn().mockResolvedValue([
        { videoId, likeCount: 3, dislikeCount: 1, viewCount: 40, commentCount: 2 },
      ]),
    };
    const profiles = {
      resolve: vi.fn().mockResolvedValue(new Map([[ownerId, 'sanket']])),
    };
    await TestBed.configureTestingModule({
      imports: [WatchPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ videoId })) } },
        { provide: EngagementService, useValue: engagement },
        { provide: ProfileService, useValue: profiles },
      ],
    })
      .overrideComponent(WatchPage, {
        set: {
          template: `@if (video(); as item) { <h1>{{ item.title }}</h1><p>{{ creatorFor(item) }}</p> }`,
        },
      })
      .compileComponents();
    const fixture = TestBed.createComponent(WatchPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne(`/api/feed/videos/${videoId}`).flush(video(videoId, ownerId));
    http.expectOne((request) => request.url === '/api/feed/videos').flush({ items: [], nextCursor: null });
    await vi.waitFor(() => expect(engagement.getSummaries).toHaveBeenCalledWith([videoId]));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Direct watch');
    expect(fixture.nativeElement.textContent).toContain('sanket');
    expect(profiles.resolve).toHaveBeenCalledWith([ownerId]);
    http.verify();
  });
});

function video(id: string, ownerId: string) {
  return {
    id,
    ownerId,
    title: 'Direct watch',
    description: null,
    hashtags: [],
    uploadedAtUtc: new Date().toISOString(),
    availableAtUtc: new Date().toISOString(),
    renditions: [],
  };
}
