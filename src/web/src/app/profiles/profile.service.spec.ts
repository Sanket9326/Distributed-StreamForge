import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProfileService } from './profile.service';

describe('ProfileService', () => {
  it('batches unique IDs and reuses cached usernames', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(ProfileService);
    const http = TestBed.inject(HttpTestingController);
    const id = 'e2c1bb10-4340-452f-9fc6-a68cf4b12457';

    const first = service.resolve([id, id, null]);
    const request = http.expectOne(
      (candidate) => candidate.url === '/api/users' && candidate.params.getAll('ids')?.join() === id,
    );
    request.flush([{ id, username: 'sanket' }]);
    expect((await first).get(id)).toBe('sanket');

    expect((await service.resolve([id])).get(id)).toBe('sanket');
    http.expectNone('/api/users');
    http.verify();
  });
});
