using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace MagSuitBuilderWeb.Services;

public sealed record SseEvent(long EventId, string Type, string JsonData);

/// <summary>
/// Fan-out hub for server-sent events. Each SSE connection subscribes to its own bounded channel;
/// slow consumers drop oldest events (clients recover via the snapshot event sent on reconnect).
/// </summary>
public sealed class EventHub
{
	static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	readonly ConcurrentDictionary<Guid, Channel<SseEvent>> subscribers = new();
	long nextEventId;

	public sealed class Subscription : IDisposable
	{
		readonly EventHub hub;
		readonly Guid id;

		public ChannelReader<SseEvent> Reader { get; }

		internal Subscription(EventHub hub, Guid id, ChannelReader<SseEvent> reader)
		{
			this.hub = hub;
			this.id = id;
			Reader = reader;
		}

		public void Dispose()
		{
			hub.subscribers.TryRemove(id, out _);
		}
	}

	public Subscription Subscribe()
	{
		var id = Guid.NewGuid();
		var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(1024)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
		});

		subscribers[id] = channel;
		return new Subscription(this, id, channel.Reader);
	}

	public void Publish(string eventType, object payload)
	{
		if (subscribers.IsEmpty)
			return;

		var evt = new SseEvent(
			Interlocked.Increment(ref nextEventId),
			eventType,
			JsonSerializer.Serialize(payload, JsonOptions));

		foreach (var channel in subscribers.Values)
			channel.Writer.TryWrite(evt);
	}
}
