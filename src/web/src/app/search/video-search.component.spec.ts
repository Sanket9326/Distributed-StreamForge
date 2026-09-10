import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { VideoSearchComponent } from './video-search.component';

describe('VideoSearchComponent', () => {
  let fixture: ComponentFixture<VideoSearchComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    vi.useFakeTimers();
    await TestBed.configureTestingModule({
      imports: [VideoSearchComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(VideoSearchComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    vi.useRealTimers();
  });

  it('waits for two characters and 250 ms before searching', async () => {
    enter('e');
    await vi.advanceTimersByTimeAsync(300);
    http.expectNone('/api/search/videos/suggestions');

    enter('el');
    expect(fixture.nativeElement.querySelector('.suggestions')).toBeNull();
    await vi.advanceTimersByTimeAsync(249);
    http.expectNone('/api/search/videos/suggestions');
    await vi.advanceTimersByTimeAsync(1);

    const request = http.expectOne(
      (candidate) => candidate.params.get('q') === 'el' && candidate.params.get('limit') === '8',
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.suggestions')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Searching');
    request.flush({ items: [] });
  });

  it('cancels stale requests, renders suggestions, and navigates to the watch route', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    enter('el');
    await vi.advanceTimersByTimeAsync(250);
    const stale = http.expectOne((request) => request.params.get('q') === 'el');

    enter('elas');
    expect(stale.cancelled).toBe(true);
    await vi.advanceTimersByTimeAsync(250);
    const current = http.expectOne((request) => request.params.get('q') === 'elas');
    current.flush({
      items: [
        {
          videoId: 'e2c1bb10-4340-452f-9fc6-a68cf4b12457',
          title: 'Elasticsearch from events',
          description: 'Durable search indexing',
          hashtags: ['dotnet'],
        },
      ],
    });
    fixture.detectChanges();

    const suggestion = fixture.nativeElement.querySelector(
      '.suggestions button',
    ) as HTMLButtonElement;
    expect(suggestion.textContent).toContain('Elasticsearch from events');
    suggestion.click();

    expect(navigate).toHaveBeenCalledWith(['/watch', 'e2c1bb10-4340-452f-9fc6-a68cf4b12457']);
  });

  function enter(value: string): void {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus();
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }
});
