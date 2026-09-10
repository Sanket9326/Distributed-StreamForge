import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, distinctUntilChanged, map, of, switchMap, tap, timer } from 'rxjs';
import { SearchService, VideoSuggestion } from './search.service';

@Component({
  selector: 'app-video-search',
  imports: [ReactiveFormsModule],
  templateUrl: './video-search.component.html',
  styleUrl: './video-search.component.scss',
})
export class VideoSearchComponent {
  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly suggestions = signal<VideoSuggestion[]>([]);
  protected readonly failed = signal(false);
  protected readonly focused = signal(false);
  protected readonly mobileOpen = signal(false);
  private readonly search = inject(SearchService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef<HTMLElement>);

  constructor() {
    this.searchControl.valueChanges
      .pipe(
        map((value) => value.trim()),
        distinctUntilChanged(),
        tap(() => {
          this.failed.set(false);
          this.suggestions.set([]);
        }),
        switchMap((query) => {
          if (query.length < 2) return of({ items: [] as VideoSuggestion[] });
          return timer(250).pipe(
            switchMap(() =>
              this.search.suggestions(query, 8).pipe(
                catchError(() => {
                  this.failed.set(true);
                  return of({ items: [] as VideoSuggestion[] });
                }),
              ),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        this.suggestions.set(response.items);
      });
  }

  protected showPanel(): boolean {
    return (
      (this.focused() || this.mobileOpen()) &&
      this.searchControl.value.trim().length >= 2 &&
      (this.failed() || this.suggestions().length > 0)
    );
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const first = this.suggestions()[0];
    if (first) this.open(first);
  }

  protected open(suggestion: VideoSuggestion): void {
    this.focused.set(false);
    this.mobileOpen.set(false);
    this.suggestions.set([]);
    this.searchControl.setValue('', { emitEvent: false });
    void this.router.navigate(['/watch', suggestion.videoId]);
  }

  protected toggleMobile(): void {
    this.mobileOpen.update((open) => !open);
    if (this.mobileOpen()) {
      setTimeout(() => this.searchInput?.nativeElement.focus());
    }
  }

  @HostListener('document:click', ['$event'])
  protected closeWhenClickingAway(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.focused.set(false);
      this.mobileOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    this.focused.set(false);
    this.mobileOpen.set(false);
  }
}
