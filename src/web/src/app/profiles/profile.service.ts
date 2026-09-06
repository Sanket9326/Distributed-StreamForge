import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface PublicProfile {
  id: string;
  username: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly cache = new Map<string, string>();

  async resolve(ids: Array<string | null | undefined>): Promise<Map<string, string>> {
    const requested = [...new Set(ids.filter((id): id is string => !!id))];
    const missing = requested.filter((id) => !this.cache.has(id)).slice(0, 50);
    if (missing.length) {
      let params = new HttpParams();
      for (const id of missing) params = params.append('ids', id);
      const profiles = await firstValueFrom(
        this.http.get<PublicProfile[]>('/api/users', { params }),
      );
      for (const profile of profiles) this.cache.set(profile.id, profile.username);
    }
    return new Map(requested.flatMap((id) => (this.cache.has(id) ? [[id, this.cache.get(id)!]] : [])));
  }
}
