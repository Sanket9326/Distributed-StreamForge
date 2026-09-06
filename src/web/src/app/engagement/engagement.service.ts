import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';

export type ReactionValue = 'like' | 'dislike' | 'none';

export interface VideoSummary {
  videoId: string;
  likeCount: number;
  dislikeCount: number;
  viewCount: number;
  commentCount: number;
}

export interface ReactionUpdate {
  reaction: ReactionValue;
  likeCount: number | null;
  dislikeCount: number | null;
  countsPending: boolean;
}

export interface ViewAccepted {
  counted: boolean;
  viewCount: number | null;
  countsPending: boolean;
}

export interface VideoComment {
  id: string;
  videoId: string;
  authorId: string;
  body: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CommentPage {
  items: VideoComment[];
  totalCount: number;
  nextCursor: string | null;
}

export interface CommentMutation {
  comment: VideoComment;
  commentCount: number;
}

@Injectable({ providedIn: 'root' })
export class EngagementService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  getSummaries(ids: string[]): Promise<VideoSummary[]> {
    const unique = [...new Set(ids)].slice(0, 50);
    if (!unique.length) return Promise.resolve([]);
    let params = new HttpParams();
    for (const id of unique) params = params.append('ids', id);
    return firstValueFrom(
      this.http.get<VideoSummary[]>('/api/engagement/videos/summaries', { params }),
    );
  }

  getReaction(videoId: string): Promise<ReactionValue> {
    return firstValueFrom(
      this.http.get<{ reaction: ReactionValue }>(
        `/api/engagement/videos/${videoId}/reaction`,
      ),
    ).then((response) => response.reaction);
  }

  async setReaction(videoId: string, reaction: ReactionValue): Promise<ReactionUpdate> {
    await this.auth.refreshCsrf();
    return firstValueFrom(
      this.http.put<ReactionUpdate>(`/api/engagement/videos/${videoId}/reaction`, {
        reaction,
      }),
    );
  }

  async recordView(videoId: string, viewSessionId: string): Promise<ViewAccepted> {
    await this.auth.refreshCsrf();
    return firstValueFrom(
      this.http.post<ViewAccepted>(`/api/engagement/videos/${videoId}/views`, {
        viewSessionId,
      }),
    );
  }

  getComments(videoId: string, cursor: string | null, limit = 20): Promise<CommentPage> {
    let params = new HttpParams().set('limit', limit);
    if (cursor) params = params.set('cursor', cursor);
    return firstValueFrom(
      this.http.get<CommentPage>(`/api/engagement/videos/${videoId}/comments`, { params }),
    );
  }

  async createComment(videoId: string, body: string): Promise<CommentMutation> {
    await this.auth.refreshCsrf();
    return firstValueFrom(
      this.http.post<CommentMutation>(`/api/engagement/videos/${videoId}/comments`, { body }),
    );
  }

  async updateComment(commentId: string, body: string): Promise<CommentMutation> {
    await this.auth.refreshCsrf();
    return firstValueFrom(
      this.http.patch<CommentMutation>(`/api/engagement/comments/${commentId}`, { body }),
    );
  }

  async deleteComment(commentId: string): Promise<number> {
    await this.auth.refreshCsrf();
    const response = await firstValueFrom(
      this.http.delete<{ commentCount: number }>(`/api/engagement/comments/${commentId}`),
    );
    return response.commentCount;
  }
}
