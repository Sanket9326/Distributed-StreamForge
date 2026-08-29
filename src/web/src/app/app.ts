import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, ViewChild, computed, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { UploadMetadata, UploadReceipt, UploadService } from './upload.service';

type UploadState = 'idle' | 'ready' | 'uploading' | 'success' | 'error';

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnDestroy {
  private static readonly maxFileSizeBytes = 1_073_741_824;
  private static readonly allowedExtensions = ['.mp4', '.mov', '.webm', '.mkv'];

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly hashtagInput = signal('');
  protected readonly state = signal<UploadState>('idle');
  protected readonly progress = signal(0);
  protected readonly receipt = signal<UploadReceipt | null>(null);
  protected readonly errorMessage = signal('');
  protected readonly isDragging = signal(false);
  protected readonly formattedSize = computed(() =>
    this.formatBytes(this.selectedFile()?.size ?? 0),
  );
  protected readonly metadataError = computed(() => this.validateMetadata().error);
  protected readonly canUpload = computed(
    () => this.state() === 'ready' && this.selectedFile() !== null && !this.metadataError(),
  );

  private uploadSubscription?: Subscription;

  constructor(private readonly uploadService: UploadService) {}

  ngOnDestroy(): void {
    this.uploadSubscription?.unsubscribe();
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.chooseFile(input.files?.item(0) ?? null);
  }

  protected onTitleInput(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
  }

  protected onDescriptionInput(event: Event): void {
    this.description.set((event.target as HTMLTextAreaElement).value);
  }

  protected onHashtagInput(event: Event): void {
    this.hashtagInput.set((event.target as HTMLInputElement).value);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (this.state() !== 'uploading') {
      this.isDragging.set(true);
    }
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);

    if (this.state() !== 'uploading') {
      this.chooseFile(event.dataTransfer?.files.item(0) ?? null);
    }
  }

  protected startUpload(): void {
    const file = this.selectedFile();
    const metadataValidation = this.validateMetadata();
    const fileValidationError = file ? this.validateFile(file) : null;
    if (!file || this.state() === 'uploading' || metadataValidation.error || fileValidationError) {
      return;
    }

    this.progress.set(0);
    this.receipt.set(null);
    this.errorMessage.set('');
    this.state.set('uploading');

    this.uploadSubscription = this.uploadService.upload(file, metadataValidation.metadata).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          const total = event.total ?? file.size;
          this.progress.set(
            total > 0 ? Math.min(100, Math.round((event.loaded / total) * 100)) : 0,
          );
        }

        if (event.type === HttpEventType.Response && event.body) {
          this.progress.set(100);
          this.receipt.set(event.body);
          this.state.set('success');
        }
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage.set(this.describeError(error));
        this.state.set('error');
      },
    });
  }

  protected cancelUpload(): void {
    this.uploadSubscription?.unsubscribe();
    this.uploadSubscription = undefined;
    this.progress.set(0);
    this.errorMessage.set('Upload cancelled. You can start it again when ready.');
    this.state.set('error');
  }

  protected retryUpload(): void {
    this.startUpload();
  }

  protected reset(): void {
    this.uploadSubscription?.unsubscribe();
    this.uploadSubscription = undefined;
    this.selectedFile.set(null);
    this.title.set('');
    this.description.set('');
    this.hashtagInput.set('');
    this.receipt.set(null);
    this.errorMessage.set('');
    this.progress.set(0);
    this.state.set('idle');

    if (this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  private chooseFile(file: File | null): void {
    this.receipt.set(null);
    this.progress.set(0);

    if (!file) {
      this.selectedFile.set(null);
      this.state.set('idle');
      return;
    }

    const validationError = this.validateFile(file);
    this.selectedFile.set(file);

    if (validationError) {
      this.errorMessage.set(validationError);
      this.state.set('error');
      return;
    }

    this.errorMessage.set('');
    this.state.set('ready');
  }

  private validateFile(file: File): string | null {
    const extension = this.getExtension(file.name);
    if (!App.allowedExtensions.includes(extension)) {
      return 'Choose an MP4, MOV, WebM, or MKV video.';
    }

    if (file.type && !file.type.startsWith('video/')) {
      return 'The selected file does not identify itself as a video.';
    }

    if (file.size === 0) {
      return 'The selected video is empty.';
    }

    if (file.size > App.maxFileSizeBytes) {
      return 'The selected video is larger than the 1 GB limit.';
    }

    return null;
  }

  private validateMetadata(): { metadata: UploadMetadata; error: string | null } {
    const title = this.title().trim();
    const description = this.description().trim();
    if (!title) {
      return {
        metadata: { title, description: description || null, hashtags: [] },
        error: 'Add a title before uploading.',
      };
    }

    if (title.length > 200) {
      return {
        metadata: { title, description: description || null, hashtags: [] },
        error: 'The title cannot exceed 200 characters.',
      };
    }

    if (description.length > 5_000) {
      return {
        metadata: { title, description, hashtags: [] },
        error: 'The description cannot exceed 5,000 characters.',
      };
    }

    const hashtags = Array.from(
      new Set(
        this.hashtagInput()
          .split(',')
          .map((hashtag) => hashtag.trim().replace(/^#/, '').trim().toLowerCase())
          .filter(Boolean),
      ),
    );
    if (hashtags.length > 10) {
      return {
        metadata: { title, description: description || null, hashtags },
        error: 'Add no more than 10 hashtags.',
      };
    }

    if (hashtags.some((hashtag) => hashtag.length > 50)) {
      return {
        metadata: { title, description: description || null, hashtags },
        error: 'Each hashtag must be 50 characters or fewer.',
      };
    }

    if (hashtags.some((hashtag) => !/^[\p{L}\p{N}_-]+$/u.test(hashtag))) {
      return {
        metadata: { title, description: description || null, hashtags },
        error: 'Hashtags may contain only letters, numbers, underscores, and hyphens.',
      };
    }

    return {
      metadata: { title, description: description || null, hashtags },
      error: null,
    };
  }

  private describeError(error: HttpErrorResponse): string {
    const problem = error.error as { detail?: string; correlationId?: string } | null;
    const message =
      problem?.detail ?? 'The upload could not be completed. Check the services and try again.';
    return problem?.correlationId ? `${message} Reference: ${problem.correlationId}` : message;
  }

  private getExtension(fileName: string): string {
    const dotIndex = fileName.lastIndexOf('.');
    return dotIndex >= 0 ? fileName.slice(dotIndex).toLowerCase() : '';
  }

  private formatBytes(bytes: number): string {
    if (bytes === 0) {
      return '0 bytes';
    }

    const units = ['bytes', 'KB', 'MB', 'GB'];
    const unitIndex = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const value = bytes / 1024 ** unitIndex;
    return `${value.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
  }
}
