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
});
