import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SearchService } from './search.service';

describe('SearchService', () => {
  it('requests video suggestions with the requested query and limit', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const service = TestBed.inject(SearchService);
    const http = TestBed.inject(HttpTestingController);

    service.suggestions('elastic', 8).subscribe((response) => expect(response.items).toEqual([]));

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/search/videos/suggestions' &&
        candidate.params.get('q') === 'elastic' &&
        candidate.params.get('limit') === '8',
    );
    request.flush({ items: [] });
    http.verify();
  });
});
