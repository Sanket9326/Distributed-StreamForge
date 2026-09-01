import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface FeedRendition {
  tier: string;
  width: number;
  height: number;
  videoCodec: string;
  audioCodec: string | null;
  contentType: string;
  sizeBytes: number;
  playbackUrl: string;
  playbackUrlExpiresAtUtc: string;
}

export interface FeedVideo {
  id: string;
  title: string;
  description: string | null;
  hashtags: string[];
  uploadedAtUtc: string;
  availableAtUtc: string;
  renditions: FeedRendition[];
}

export interface FeedPage {
  items: FeedVideo[];
  nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class FeedService {
  constructor(private readonly http: HttpClient) {}

  getPage(limit: number, cursor: string | null): Observable<FeedPage> {
    let params = new HttpParams().set('limit', limit);
    if (cursor) {
      params = params.set('cursor', cursor);
    }

    return this.http.get<FeedPage>('/api/feed/videos', { params });
  }

  refreshRenditions(videoId: string): Observable<FeedRendition[]> {
    return this.http.get<FeedRendition[]>(`/api/feed/videos/${videoId}/renditions`);
  }
}
