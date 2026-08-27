import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface UploadReceipt {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAtUtc: string;
  correlationId: string;
}

@Injectable({ providedIn: 'root' })
export class UploadService {
  private static readonly fallbackContentTypes: Record<string, string> = {
    '.mkv': 'video/x-matroska',
    '.mov': 'video/quicktime',
    '.mp4': 'video/mp4',
    '.webm': 'video/webm',
  };

  constructor(private readonly http: HttpClient) {}

  upload(file: File): Observable<HttpEvent<UploadReceipt>> {
    const formData = new FormData();
    formData.append('file', this.withVideoContentType(file), file.name);

    return this.http.post<UploadReceipt>('/api/uploads', formData, {
      observe: 'events',
      reportProgress: true,
    });
  }

  private withVideoContentType(file: File): File {
    if (file.type.startsWith('video/')) {
      return file;
    }

    const dotIndex = file.name.lastIndexOf('.');
    const extension = dotIndex >= 0 ? file.name.slice(dotIndex).toLowerCase() : '';
    return new File([file], file.name, {
      lastModified: file.lastModified,
      type: UploadService.fallbackContentTypes[extension] ?? 'application/octet-stream',
    });
  }
}
