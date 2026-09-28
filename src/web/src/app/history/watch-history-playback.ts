import { WatchHistoryService, WatchProgress } from './watch-history.service';

/** Owns one player's progress without persisting guest or preview activity. */
export class WatchHistoryPlayback {
  private account: string | null = null;
  private snapshot: WatchProgress | null = null;
  private lastSent = '';
  private pending: Promise<boolean> = Promise.resolve(true);
  private saveGeneration = 0;
  private readonly unregister: () => void;
  private disposed = false;

  constructor(private readonly history: WatchHistoryService, private readonly videoId: string,
    private readonly player: HTMLVideoElement, private readonly notice: (message: string) => void,
    private readonly sourceChanging: () => boolean) {
    this.unregister = history.register(() => this.flush());
  }

  playing(): void {
    const account = this.history.accountKey();
    if (account !== this.account) { this.account = account; this.snapshot = null; this.lastSent = ''; }
    if (account && !this.sourceChanging()) this.capture();
  }

  private capture(completed = this.player.ended): void {
    if (!this.account || this.account !== this.history.accountKey()) return;
    const duration = this.player.duration;
    const position = this.player.currentTime;
    if (!Number.isFinite(duration) || duration <= 0 || !Number.isFinite(position)) return;
    this.snapshot = { positionMs: Math.round(Math.max(0, Math.min(position, duration)) * 1000),
      durationMs: Math.round(duration * 1000), isCompleted: completed };
  }

  flush(keepalive = false, completed = this.player.ended): Promise<boolean> {
    if (this.disposed || !this.account || this.account !== this.history.accountKey()) return Promise.resolve(false);
    if (!this.sourceChanging()) this.capture(completed);
    if (!this.snapshot) return Promise.resolve(false);
    const key = JSON.stringify(this.snapshot);
    if (this.lastSent === key) return this.pending;
    this.lastSent = key;
    const account = this.account;
    const generation = ++this.saveGeneration;
    this.pending = this.history.save(this.videoId, { ...this.snapshot }, account, keepalive).then((saved) => {
      if (!this.disposed && generation === this.saveGeneration && account === this.history.accountKey()) {
        if (!saved) {
          // A later lifecycle event may try again; overlapping events still share the pending save.
          this.lastSent = '';
          this.notice('Your latest watch progress could not be confirmed.');
        }
        else this.notice('');
      }
      return saved;
    });
    return this.pending;
  }

  dispose(): void {
    void this.flush(true);
    this.disposed = true;
    this.unregister();
  }
}
