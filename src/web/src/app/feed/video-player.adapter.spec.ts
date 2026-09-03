const hlsMock = vi.hoisted(() => ({ supported: true, instances: [] as any[] }));

vi.mock('hls.js', () => {
  class MockHls {
    static isSupported = () => hlsMock.supported;
    levels = [{ height: 360, bitrate: 800_000 }, { height: 720, bitrate: 3_000_000 }];
    nextLevel = -1;
    handlers = new Map<string, (...args: any[]) => void>();
    loadSource = vi.fn(); attachMedia = vi.fn(); startLoad = vi.fn(); destroy = vi.fn();
    constructor(public config: unknown) { hlsMock.instances.push(this); }
    on(event: string, handler: (...args: any[]) => void) { this.handlers.set(event, handler); }
  }
  return { default: MockHls, Events: { MANIFEST_PARSED: 'manifest', FRAG_CHANGED: 'fragment', ERROR: 'error' } };
});

import { VideoPlayerAdapter } from './video-player.adapter';

describe('VideoPlayerAdapter', () => {
  beforeEach(() => { hlsMock.supported = true; hlsMock.instances.length = 0; });

  it('discovers levels without auto-loading, switches at the next boundary, and destroys HLS', () => {
    const levels = vi.fn(); const active = vi.fn(); const fatal = vi.fn();
    const video = document.createElement('video');
    const adapter = new VideoPlayerAdapter(video, { levels, active, fatal });
    expect(adapter.attach('/master.m3u8')).toBe('hls.js');
    const hls = hlsMock.instances[0];
    expect(hls.config).toMatchObject({ autoStartLoad: false, startLevel: 0, capLevelToPlayerSize: true, ignoreDevicePixelRatio: true });
    expect(hls.loadSource).toHaveBeenCalledWith('/master.m3u8');
    hls.handlers.get('manifest')?.('', { levels: hls.levels });
    expect(levels).toHaveBeenCalledWith([{ index: 0, height: 360, bitrate: 800_000 }, { index: 1, height: 720, bitrate: 3_000_000 }]);
    adapter.start(); adapter.select(1);
    expect(hls.startLoad).toHaveBeenCalledWith(-1); expect(hls.nextLevel).toBe(1);
    hls.handlers.get('fragment')?.('', { frag: { level: 1 } });
    expect(active).toHaveBeenCalledWith({ index: 1, height: 720, bitrate: 3_000_000 });
    hls.handlers.get('error')?.('', { fatal: true }); expect(fatal).toHaveBeenCalled();
    adapter.destroy(); expect(hls.destroy).toHaveBeenCalled();
  });

  it('uses native HLS when MSE is unavailable', () => {
    hlsMock.supported = false; const video = document.createElement('video');
    vi.spyOn(video, 'canPlayType').mockReturnValue('maybe');
    const adapter = new VideoPlayerAdapter(video, { levels: vi.fn(), active: vi.fn(), fatal: vi.fn() });
    expect(adapter.attach('/master.m3u8')).toBe('native');
    expect(video.src).toContain('/master.m3u8');
  });
});
