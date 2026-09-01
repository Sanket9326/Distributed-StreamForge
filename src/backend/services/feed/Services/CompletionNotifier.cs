using System.Threading.Channels;

namespace StreamForge.Feed.Api.Services;

public sealed record CompletionNotification(Guid VideoId, DateTimeOffset AvailableAtUtc);

public sealed class CompletionNotifier
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, HashSet<Channel<CompletionNotification>>> subscribers = [];

    public CompletionSubscription Subscribe(Guid videoId)
    {
        var channel = Channel.CreateBounded<CompletionNotification>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        lock (sync)
        {
            if (!subscribers.TryGetValue(videoId, out var channels))
            {
                channels = [];
                subscribers[videoId] = channels;
            }

            channels.Add(channel);
        }

        return new CompletionSubscription(channel.Reader, () => Remove(videoId, channel));
    }

    public void Publish(CompletionNotification notification)
    {
        Channel<CompletionNotification>[] channels;
        lock (sync)
        {
            if (!subscribers.Remove(notification.VideoId, out var current))
            {
                return;
            }

            channels = current.ToArray();
        }

        foreach (var channel in channels)
        {
            channel.Writer.TryWrite(notification);
            channel.Writer.TryComplete();
        }
    }

    private void Remove(Guid videoId, Channel<CompletionNotification> channel)
    {
        lock (sync)
        {
            if (!subscribers.TryGetValue(videoId, out var channels))
            {
                return;
            }

            channels.Remove(channel);
            channel.Writer.TryComplete();
            if (channels.Count == 0)
            {
                subscribers.Remove(videoId);
            }
        }
    }
}

public sealed class CompletionSubscription(
    ChannelReader<CompletionNotification> reader,
    Action dispose) : IDisposable
{
    public ChannelReader<CompletionNotification> Reader { get; } = reader;

    public void Dispose() => dispose();
}
