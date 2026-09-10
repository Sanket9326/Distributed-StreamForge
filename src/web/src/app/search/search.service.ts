import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface VideoSuggestion {
  videoId: string;
  title: string;
  description: string | null;
  hashtags: string[];
}

export interface VideoSuggestionsResponse {
  items: VideoSuggestion[];
}

@Injectable({ providedIn: 'root' })
export class SearchService {
  constructor(private readonly http: HttpClient) {}

  suggestions(query: string, limit = 8): Observable<VideoSuggestionsResponse> {
    const params = new HttpParams().set('q', query).set('limit', limit);
    return this.http.get<VideoSuggestionsResponse>('/api/search/videos/suggestions', { params });
  }
}
