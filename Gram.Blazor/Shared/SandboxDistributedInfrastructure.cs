using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

namespace Gram.Blazor.Shared;

public sealed record SandboxDispatchMessage(string MessageId, string JobId, string Payload);

public interface ISandboxMessageBus
{
    Task PublishAsync(SandboxDispatchMessage message, CancellationToken cancellationToken = default);
}

public sealed class InProcessSandboxMessageBus : ISandboxMessageBus
{
    public ConcurrentQueue<SandboxDispatchMessage> Published { get; } = new();
    public Task PublishAsync(SandboxDispatchMessage message, CancellationToken cancellationToken = default) { Published.Enqueue(message); return Task.CompletedTask; }
}

public sealed class RedisSandboxMessageBus : ISandboxMessageBus, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _stream;
    public RedisSandboxMessageBus(string connectionString, string stream = "grambox:sandbox-jobs") { _redis = ConnectionMultiplexer.Connect(connectionString); _stream = stream; }
    public async Task PublishAsync(SandboxDispatchMessage message, CancellationToken cancellationToken = default)
    {
        await _redis.GetDatabase().StreamAddAsync(_stream, [new NameValueEntry("messageId", message.MessageId), new NameValueEntry("jobId", message.JobId), new NameValueEntry("payload", message.Payload)]);
    }
    public void Dispose() => _redis.Dispose();
}

public interface IGramCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class InMemoryGramCache : IGramCache
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, string Json)> _items = new();
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_items.TryGetValue(key, out var item) || item.Expires <= DateTimeOffset.UtcNow) { _items.TryRemove(key, out _); return Task.FromResult<T?>(default); }
        return Task.FromResult(JsonSerializer.Deserialize<T>(item.Json));
    }
    public Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken = default) { _items[key] = (DateTimeOffset.UtcNow.Add(lifetime), JsonSerializer.Serialize(value)); return Task.CompletedTask; }
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { _items.TryRemove(key, out _); return Task.CompletedTask; }
}

public sealed class RedisGramCache : IGramCache, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;
    public RedisGramCache(string connectionString, string prefix = "gram:") { _redis = ConnectionMultiplexer.Connect(connectionString); _prefix = prefix; }
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) { var value = await _redis.GetDatabase().StringGetAsync(_prefix + key); return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value!); }
    public async Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken = default) => await _redis.GetDatabase().StringSetAsync(_prefix + key, JsonSerializer.Serialize(value), lifetime);
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => _redis.GetDatabase().KeyDeleteAsync(_prefix + key);
    public void Dispose() => _redis.Dispose();
}

public sealed class SandboxOutboxProcessor : BackgroundService
{
    private readonly IDatabaseProvider _database;
    private readonly ISandboxMessageBus _bus;
    private readonly ILogger<SandboxOutboxProcessor> _logger;
    public SandboxOutboxProcessor(IDatabaseProvider database, ISandboxMessageBus bus, ILogger<SandboxOutboxProcessor> logger) { _database = database; _bus = bus; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var message in await _database.ListPendingSandboxOutboxAsync(100, stoppingToken))
                {
                    await _bus.PublishAsync(new SandboxDispatchMessage(message.MessageId, message.JobId, message.Payload), stoppingToken);
                    await _database.MarkSandboxOutboxPublishedAsync(message.MessageId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Sandbox outbox processing failed; messages will be retried."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
