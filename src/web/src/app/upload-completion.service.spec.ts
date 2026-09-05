import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService, AuthUser } from './auth/auth.service';
import { UploadCompletionService } from './upload-completion.service';

describe('User upload notifications', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });
  it('isolates pending uploads and closes streams when the account changes', () => {
    const closed = vi.fn();
    vi.stubGlobal(
      'EventSource',
      class {
        addEventListener() {}
        close() {
          closed();
        }
      },
    );
    const user = signal<AuthUser | null>({ id: 'one', username: 'one', email: 'one@example.test' });
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: { user } }] });
    const service = TestBed.inject(UploadCompletionService);
    TestBed.tick();
    service.track('video-one', 'First video');
    expect(localStorage.getItem('streamforge.pending-uploads.v2.one')).toContain('video-one');
    user.set(null);
    TestBed.tick();
    expect(closed).toHaveBeenCalledOnce();
    expect(service.toast()).toBeNull();
    user.set({ id: 'two', username: 'two', email: 'two@example.test' });
    TestBed.tick();
    service.track('video-two', 'Second video');
    expect(localStorage.getItem('streamforge.pending-uploads.v2.two')).not.toContain('video-one');
  });
});
