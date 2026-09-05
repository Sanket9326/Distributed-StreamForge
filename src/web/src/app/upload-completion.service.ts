import { Injectable, effect, inject, signal } from '@angular/core';
import { AuthService } from './auth/auth.service';

interface PendingUpload {
  videoId: string;
  title: string;
  createdAtUtc: string;
}

export interface CompletionToast {
  videoId: string;
  title: string;
}

@Injectable({ providedIn: 'root' })
export class UploadCompletionService {
  private readonly auth = inject(AuthService);
  private storageKey: string | null = null;
  private static readonly maximumAgeMilliseconds = 7 * 24 * 60 * 60 * 1000;

  readonly toast = signal<CompletionToast | null>(null);
  private readonly eventSources = new Map<string, EventSource>();
  private pending: PendingUpload[] = [];

  constructor() {
    effect(() => {
      const userId = this.auth.user()?.id ?? null;
      const nextKey = userId ? `streamforge.pending-uploads.v2.${userId}` : null;
      if (nextKey === this.storageKey) return;
      for (const source of this.eventSources.values()) source.close();
      this.eventSources.clear();
      this.toast.set(null);
      this.storageKey = nextKey;
      this.pending = this.loadPending();
      for (const upload of this.pending) this.watch(upload);
    });
  }

  track(videoId: string, title: string): void {
    if (!this.auth.user() || !this.storageKey) return;
    const upload: PendingUpload = {
      videoId,
      title,
      createdAtUtc: new Date().toISOString(),
    };
    this.pending = [...this.pending.filter((item) => item.videoId !== videoId), upload];
    this.savePending();
    this.watch(upload);
  }

  dismissToast(): void {
    this.toast.set(null);
  }

  private watch(upload: PendingUpload): void {
    if (typeof EventSource === 'undefined' || this.eventSources.has(upload.videoId)) {
      return;
    }

    const source = new EventSource(`/api/feed/videos/${upload.videoId}/completion-events`);
    const ownerKey = this.storageKey;
    this.eventSources.set(upload.videoId, source);
    source.addEventListener('completed', (event) => {
      if (this.storageKey !== ownerKey || !this.auth.user()) return;
      try {
        const data = JSON.parse((event as MessageEvent<string>).data) as { videoId?: string };
        if (data.videoId?.toLowerCase() !== upload.videoId.toLowerCase()) return;
      } catch {
        return;
      }

      this.toast.set({ videoId: upload.videoId, title: upload.title });
      this.pending = this.pending.filter((item) => item.videoId !== upload.videoId);
      this.savePending();
      source.close();
      this.eventSources.delete(upload.videoId);
    });
  }

  private loadPending(): PendingUpload[] {
    if (typeof localStorage === 'undefined' || !this.storageKey) {
      return [];
    }

    try {
      const parsed = JSON.parse(localStorage.getItem(this.storageKey) ?? '[]');
      if (!Array.isArray(parsed)) {
        return [];
      }

      const cutoff = Date.now() - UploadCompletionService.maximumAgeMilliseconds;
      return parsed.filter(
        (item): item is PendingUpload =>
          typeof item?.videoId === 'string' &&
          typeof item?.title === 'string' &&
          typeof item?.createdAtUtc === 'string' &&
          Date.parse(item.createdAtUtc) >= cutoff,
      );
    } catch {
      return [];
    }
  }

  private savePending(): void {
    if (typeof localStorage !== 'undefined' && this.storageKey) {
      try {
        localStorage.setItem(this.storageKey, JSON.stringify(this.pending));
      } catch {
        /* Keep notifications in memory when browser storage is unavailable. */
      }
    }
  }
}
