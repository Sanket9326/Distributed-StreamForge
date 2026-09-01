import { Injectable, signal } from '@angular/core';

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
  private static readonly storageKey = 'streamforge.pending-uploads.v1';
  private static readonly maximumAgeMilliseconds = 7 * 24 * 60 * 60 * 1000;

  readonly toast = signal<CompletionToast | null>(null);
  private readonly eventSources = new Map<string, EventSource>();
  private pending = this.loadPending();

  constructor() {
    for (const upload of this.pending) {
      this.watch(upload);
    }
  }

  track(videoId: string, title: string): void {
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
    source.addEventListener('completed', (event) => {
      const data = JSON.parse((event as MessageEvent<string>).data) as { videoId: string };
      if (data.videoId.toLowerCase() !== upload.videoId.toLowerCase()) {
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
    if (typeof localStorage === 'undefined') {
      return [];
    }

    try {
      const parsed = JSON.parse(localStorage.getItem(UploadCompletionService.storageKey) ?? '[]');
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
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(UploadCompletionService.storageKey, JSON.stringify(this.pending));
    }
  }
}
