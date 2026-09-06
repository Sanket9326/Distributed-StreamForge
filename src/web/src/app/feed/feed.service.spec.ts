import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FeedService } from './feed.service';

describe('FeedService', () => {
  let service: FeedService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FeedService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the requested page size and opaque cursor', () => {
    service.getPage(10, 'next-cursor').subscribe();

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/feed/videos' &&
        candidate.params.get('limit') === '10' &&
        candidate.params.get('cursor') === 'next-cursor',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], nextCursor: null });
  });

  it('loads a single video for a direct watch link', () => {
    const id = 'e2c1bb10-4340-452f-9fc6-a68cf4b12457';
    service.getVideo(id).subscribe();

    const request = http.expectOne(`/api/feed/videos/${id}`);
    expect(request.request.method).toBe('GET');
    request.flush({ id, ownerId: null, title: 'Direct watch', renditions: [] });
  });
});
