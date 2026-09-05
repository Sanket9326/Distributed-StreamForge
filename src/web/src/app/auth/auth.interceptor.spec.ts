import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('Upload authentication responses', () => {
  let http: HttpTestingController;
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }),
  );
  afterEach(() => http.verify());
  it.each([401, 503])('handles upload status %s without replaying media', (status) => {
    http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.user.set({ id: 'one', username: 'tester', email: 'test@example.test' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    TestBed.inject(HttpClient)
      .post('/api/uploads', new FormData())
      .subscribe({ error: () => undefined });
    http.expectOne('/api/uploads').flush({}, { status, statusText: 'Failure' });
    expect(navigate).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
    expect(auth.user()?.id ?? null).toBe(status === 401 ? null : 'one');
    http.expectNone('/api/uploads');
  });
});
