import Hls, { ErrorData, Events, FragChangedData, Level } from 'hls.js';

export interface PlayerLevel { index: number; height: number; bitrate: number; }
export interface PlayerCallbacks {
  levels(levels: PlayerLevel[]): void;
  active(level: PlayerLevel): void;
  fatal(error: ErrorData): void;
}

export class VideoPlayerAdapter {
  private hls?: Hls;
  private activeLevelIndex = -1;
  constructor(private readonly video: HTMLVideoElement, private readonly callbacks: PlayerCallbacks) {}

  attach(manifestUrl: string): 'hls.js' | 'native' | 'unavailable' {
    this.destroy();
    if (Hls.isSupported()) {
      const hls = new Hls({ autoStartLoad: false, startLevel: 0, capLevelToPlayerSize: true, ignoreDevicePixelRatio: true });
      this.hls = hls;
      hls.on(Events.MANIFEST_PARSED, (_event, data) => this.callbacks.levels(data.levels.map((level: Level, index: number) => ({ index, height: level.height, bitrate: level.bitrate }))));
      hls.on(Events.FRAG_CHANGED, (_event, data: FragChangedData) => {
        if (data.frag.level === this.activeLevelIndex) return;
        this.activeLevelIndex = data.frag.level;
        const level = hls.levels[data.frag.level];
        if (level) this.callbacks.active({ index: data.frag.level, height: level.height, bitrate: level.bitrate });
      });
      hls.on(Events.ERROR, (_event, data) => { if (data.fatal) this.callbacks.fatal(data); });
      hls.loadSource(manifestUrl); hls.attachMedia(this.video); return 'hls.js';
    }
    if (this.video.canPlayType('application/vnd.apple.mpegurl')) { this.video.src = manifestUrl; this.callbacks.levels([]); return 'native'; }
    return 'unavailable';
  }
  start(): void { this.hls?.startLoad(-1); }
  select(index: number): void { if (this.hls) this.hls.nextLevel = index; }
  destroy(): void { this.hls?.destroy(); this.hls = undefined; this.activeLevelIndex = -1; }
}
