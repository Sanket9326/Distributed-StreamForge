import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App shell', () => {
  beforeEach(() => localStorage.clear());

  it('renders Home and Upload navigation with Home as the default route', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);

    fixture.detectChanges();
    await router.navigateByUrl('/');
    fixture.detectChanges();

    const request = http.expectOne(
      (candidate) => candidate.url === '/api/feed/videos' && candidate.params.get('limit') === '1',
    );
    request.flush({ items: [], nextCursor: null });
    fixture.detectChanges();

    const links = Array.from(
      fixture.nativeElement.querySelectorAll('nav a'),
    ) as HTMLAnchorElement[];
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Home', 'Upload']);
    expect(fixture.nativeElement.textContent).toContain('No videos are ready yet');
    http.verify();
  });
});
