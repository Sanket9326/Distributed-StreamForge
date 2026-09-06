import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { UploadCompletionService } from './upload-completion.service';
import { AuthService } from './auth/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly completions = inject(UploadCompletionService);
  protected readonly auth = inject(AuthService);
  protected readonly sidebarCollapsed = signal(localStorage.getItem('streamforge-sidebar-collapsed') === '1');
  private readonly router = inject(Router);

  constructor() {
    void this.auth.initialize();
  }

  protected async logout(): Promise<void> {
    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/');
    } catch {
      /* AuthService exposes a retryable error without pretending logout succeeded. */
    }
  }

  protected toggleSidebar(): void {
    this.sidebarCollapsed.update((value) => !value);
    localStorage.setItem('streamforge-sidebar-collapsed', this.sidebarCollapsed() ? '1' : '0');
  }
}
