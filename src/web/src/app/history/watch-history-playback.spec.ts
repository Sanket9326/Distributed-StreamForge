import { WatchHistoryPlayback } from './watch-history-playback';
import { WatchHistoryService } from './watch-history.service';

describe('WatchHistoryPlayback', () => {
  let account: string | null;
  let player: HTMLVideoElement;
  let save: ReturnType<typeof vi.fn>;
  let controller: WatchHistoryPlayback;
  let changing: boolean;
  let notice = vi.fn<(message: string) => void>();
  beforeEach(() => {
    account = 'alice:1'; changing = false;
    player = document.createElement('video');
    Object.defineProperty(player, 'duration', { configurable: true, value: 600 });
    save = vi.fn().mockResolvedValue(true);
    const history = { accountKey: () => account, save, register: () => () => undefined } as unknown as WatchHistoryService;
    notice = vi.fn();
    controller = new WatchHistoryPlayback(history, 'video', player, notice, () => changing);
  });
  it('requires actual authenticated playback and coalesces pause, hide and teardown', async () => {
    player.currentTime = 120;
    await controller.flush();
    expect(save).not.toHaveBeenCalled();
    account = null; controller.playing(); await controller.flush();
    expect(save).not.toHaveBeenCalled();
    account = 'alice:1'; controller.playing();
    await controller.flush(); await controller.flush(true); controller.dispose();
    expect(save).toHaveBeenCalledOnce();
    expect(save).toHaveBeenCalledWith('video', { positionMs: 120_000, durationMs: 600_000, isCompleted: false }, 'alice:1', false);
  });
  it('saves a backward seek and completion while preserving progress during source replacement', async () => {
    player.currentTime = 300; controller.playing(); await controller.flush();
    player.currentTime = 30; await controller.flush();
    expect(save.mock.calls[1][1].positionMs).toBe(30_000);
    changing = true; player.currentTime = 0; await controller.flush(true);
    expect(save).toHaveBeenCalledTimes(2);
    changing = false; player.currentTime = 600; await controller.flush(false, true);
    expect(save.mock.calls[2][1]).toEqual({ positionMs: 600_000, durationMs: 600_000, isCompleted: true });
  });
  it('never sends the previous account snapshot under a new account', async () => {
    player.currentTime = 120; controller.playing();
    account = 'bob:2'; await controller.flush();
    expect(save).not.toHaveBeenCalled();
    player.currentTime = 160; controller.playing(); await controller.flush();
    expect(save).toHaveBeenCalledWith('video', expect.objectContaining({ positionMs: 160_000 }), 'bob:2', false);
  });
  it('retries an unsuccessful pause save on the next exit event without a retry loop', async () => {
    save.mockResolvedValueOnce(false);
    player.currentTime = 120; controller.playing();
    expect(await controller.flush()).toBe(false);
    expect(save).toHaveBeenCalledOnce();
    expect(await controller.flush(true)).toBe(true);
    controller.dispose();
    expect(save).toHaveBeenCalledTimes(2);
    expect(save.mock.calls[1][3]).toBe(true);
    expect(notice).toHaveBeenLastCalledWith('');
  });
  it('coalesces an in-flight save and ignores an older failure after newer progress succeeds', async () => {
    let fail!: (saved: boolean) => void;
    save.mockReturnValueOnce(new Promise<boolean>((resolve) => { fail = resolve; }));
    player.currentTime = 120; controller.playing();
    const older = controller.flush();
    expect(controller.flush(true)).toBe(older);
    player.currentTime = 200;
    await controller.flush();
    fail(false); await older;
    await controller.flush(true);
    expect(save).toHaveBeenCalledTimes(2);
    expect(notice).toHaveBeenLastCalledWith('');
  });
  it('ignores the previous account save result even when both accounts watched the same position', async () => {
    let fail!: (saved: boolean) => void;
    save.mockReturnValueOnce(new Promise<boolean>((resolve) => { fail = resolve; }));
    player.currentTime = 120; controller.playing();
    const older = controller.flush();
    account = 'bob:2'; controller.playing();
    await controller.flush();
    fail(false); await older;
    await controller.flush(true);
    expect(save).toHaveBeenCalledTimes(2);
    expect(notice).toHaveBeenCalledTimes(1);
    expect(notice).toHaveBeenLastCalledWith('');
  });
});
