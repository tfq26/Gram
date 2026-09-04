using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public enum SandboxJobStatus { Queued, Starting, Ready, Failed, Cancelled }
public enum SandboxJobPriority { Interactive = 0, Normal = 1, Background = 2, Batch = 3 }

public sealed record SandboxJobRequest(string RepositoryPath, string? RunId = null, string? Name = null, string Engine = "Auto", string NetworkMode = "Disabled", IReadOnlyList<string>? AllowedNetworkHosts = null, string? IdempotencyKey = null, SandboxJobPriority Priority = SandboxJobPriority.Normal, SandboxResourceClass ResourceClass = SandboxResourceClass.Small, int MaxRuntimeSeconds = 180);
public sealed record SandboxJobSnapshot(string JobId, SandboxJobStatus Status, int? QueuePosition, string? SandboxId, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, SandboxJobPriority Priority = SandboxJobPriority.Normal, SandboxResourceClass ResourceClass = SandboxResourceClass.Small, int MaxRuntimeSeconds = 180);
public sealed record SandboxJobRecord(string JobId, SandboxJobRequest Request, SandboxJobStatus Status, string? SandboxId, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SandboxOutboxRecord(string MessageId, string JobId, string Payload, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt = null);

public interface ISandboxJobQueue
{
    Task<SandboxJobSnapshot> EnqueueAsync(SandboxJobRequest request, CancellationToken cancellationToken = default);
    SandboxJobSnapshot? Get(string jobId);
    Task<SandboxJobSnapshot?> GetAsync(string jobId, CancellationToken cancellationToken = default);
    bool Cancel(string jobId);
    IAsyncEnumerable<SandboxJobSnapshot> SubscribeAsync(string jobId, CancellationToken cancellationToken = default);
}

public sealed class SandboxJobQueue : ISandboxJobQueue
{
    internal sealed class Job
    {
        public required string Id { get; init; }
        public required SandboxJobRequest Request { get; init; }
        public SandboxJobStatus Status { get; set; } = SandboxJobStatus.Queued;
        public string? SandboxId { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public CancellationTokenSource Cancellation { get; } = new();
        public Channel<SandboxJobSnapshot> Events { get; } = Channel.CreateUnbounded<SandboxJobSnapshot>();
    }

    private readonly PriorityQueue<Job, int> _pending = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly SemaphoreSlim _capacity = new(32, 32);
    private readonly object _pendingLock = new();
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _idempotency = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _persistencePath;
    private readonly object _persistenceLock = new();
    private readonly IDatabaseProvider? _database;
    private readonly SemaphoreSlim _databaseLoadLock = new(1, 1);
    private bool _databaseLoaded;

    public SandboxJobQueue(StorageService? storage = null, IDatabaseProvider? database = null)
    {
        _persistencePath = storage?.GetFilePath("sandbox-jobs.json") ?? Path.Combine(Path.GetTempPath(), $"gram-sandbox-jobs-{Guid.NewGuid():N}.json");
        _database = database;
        Restore();
    }

    public async Task<SandboxJobSnapshot> EnqueueAsync(SandboxJobRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseLoadedAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && _idempotency.TryGetValue(request.IdempotencyKey, out var existingId) && _jobs.TryGetValue(existingId, out var existing)) return ToSnapshot(existing);
        await _capacity.WaitAsync(cancellationToken);
        var job = new Job { Id = "job-" + Guid.NewGuid().ToString("N")[..12], Request = request };
        _jobs[job.Id] = job;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey)) _idempotency[request.IdempotencyKey] = job.Id;
        lock (_pendingLock) _pending.Enqueue(job, (int)request.Priority);
        Publish(job);
        _pendingSignal.Release();
        Persist();
        await PersistNewJobWithOutboxAsync(job);
        return ToSnapshot(job);
    }

    internal async ValueTask<JobWork?> DequeueAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseLoadedAsync(cancellationToken);
        await _pendingSignal.WaitAsync(cancellationToken);
        Job? job;
        lock (_pendingLock) job = _pending.Count == 0 ? null : _pending.Dequeue();
        _capacity.Release();
        return job is null ? null : new JobWork(job);
    }

    public SandboxJobSnapshot? Get(string jobId) => _jobs.TryGetValue(jobId, out var job) ? ToSnapshot(job) : null;

    public async Task<SandboxJobSnapshot?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseLoadedAsync(cancellationToken);
        return Get(jobId);
    }

    public bool Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || job.Status is SandboxJobStatus.Ready or SandboxJobStatus.Failed or SandboxJobStatus.Cancelled) return false;
        job.Cancellation.Cancel();
        if (job.Status == SandboxJobStatus.Queued) { job.Status = SandboxJobStatus.Cancelled; job.UpdatedAt = DateTimeOffset.UtcNow; Publish(job); job.Events.Writer.TryComplete(); Persist(); _ = PersistDatabaseAsync(job); }
        return true;
    }

    public async IAsyncEnumerable<SandboxJobSnapshot> SubscribeAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) yield break;
        yield return ToSnapshot(job);
        await foreach (var update in job.Events.Reader.ReadAllAsync(cancellationToken)) yield return update;
    }

    internal sealed class JobWork(Job job) : IDisposable
    {
        internal Job Job { get; } = job;
        public void Dispose() => Job.Cancellation.Dispose();
    }

    internal void PublishStatus(JobWork work, SandboxJobStatus status, string? sandboxId = null, string? error = null)
    {
        work.Job.Status = status;
        work.Job.SandboxId = sandboxId ?? work.Job.SandboxId;
        work.Job.Error = error;
        work.Job.UpdatedAt = DateTimeOffset.UtcNow;
        Publish(work.Job);
        Persist();
        _ = PersistDatabaseAsync(work.Job);
        if (status is SandboxJobStatus.Ready or SandboxJobStatus.Failed or SandboxJobStatus.Cancelled) work.Job.Events.Writer.TryComplete();
    }

    private void Publish(Job job) => job.Events.Writer.TryWrite(ToSnapshot(job));

    private async Task EnsureDatabaseLoadedAsync(CancellationToken ct)
    {
        if (_database is null || _databaseLoaded) return;
        await _databaseLoadLock.WaitAsync(ct);
        try
        {
            if (_databaseLoaded) return;
            foreach (var record in await _database.ListSandboxJobsAsync(ct))
            {
                if (_jobs.ContainsKey(record.JobId)) continue;
                var status = record.Status == SandboxJobStatus.Starting ? SandboxJobStatus.Queued : record.Status;
                var job = new Job { Id = record.JobId, Request = record.Request, Status = status, SandboxId = record.SandboxId, Error = status == SandboxJobStatus.Queued ? "Recovered after the backend restarted." : record.Error, CreatedAt = record.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
                _jobs[job.Id] = job;
                if (!string.IsNullOrWhiteSpace(job.Request.IdempotencyKey)) _idempotency[job.Request.IdempotencyKey] = job.Id;
                if (status == SandboxJobStatus.Queued && _capacity.Wait(0)) { lock (_pendingLock) _pending.Enqueue(job, (int)job.Request.Priority); _pendingSignal.Release(); Publish(job); }
            }
            _databaseLoaded = true;
        }
        finally { _databaseLoadLock.Release(); }
    }

    private async Task PersistDatabaseAsync(Job job)
    {
        if (_database is null) return;
        try { await _database.SaveSandboxJobAsync(new SandboxJobRecord(job.Id, job.Request, job.Status, job.SandboxId, job.Error, job.CreatedAt, job.UpdatedAt)); }
        catch { /* JSON persistence remains the local fallback */ }
    }

    private async Task PersistNewJobWithOutboxAsync(Job job)
    {
        if (_database is null) { Persist(); return; }
        try
        {
            var record = new SandboxJobRecord(job.Id, job.Request, job.Status, job.SandboxId, job.Error, job.CreatedAt, job.UpdatedAt);
            var payload = JsonSerializer.Serialize(job.Request);
            await _database.SaveSandboxJobWithOutboxAsync(record, new SandboxOutboxRecord("msg-" + Guid.NewGuid().ToString("N")[..12], job.Id, payload, DateTimeOffset.UtcNow));
        }
        catch { Persist(); }
    }

    private void Persist()
    {
        lock (_persistenceLock)
        {
            try
            {
                var records = _jobs.Values.Select(job => new PersistedJob(job.Id, job.Request, job.Status, job.SandboxId, job.Error, job.CreatedAt, job.UpdatedAt)).ToList();
                var temporaryPath = _persistencePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, _persistencePath, true);
            }
            catch { /* persistence must not stop sandbox execution */ }
        }
    }

    private void Restore()
    {
        try
        {
            if (!File.Exists(_persistencePath)) return;
            var records = JsonSerializer.Deserialize<List<PersistedJob>>(File.ReadAllText(_persistencePath)) ?? [];
            foreach (var record in records)
            {
                var status = record.Status is SandboxJobStatus.Starting ? SandboxJobStatus.Queued : record.Status;
                var job = new Job { Id = record.Id, Request = record.Request, Status = status, SandboxId = record.SandboxId, Error = status == SandboxJobStatus.Queued ? "Recovered after the backend restarted." : record.Error, CreatedAt = record.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
                _jobs[job.Id] = job;
                if (!string.IsNullOrWhiteSpace(record.Request.IdempotencyKey)) _idempotency[record.Request.IdempotencyKey] = job.Id;
                if (status == SandboxJobStatus.Queued && _capacity.Wait(0)) { lock (_pendingLock) _pending.Enqueue(job, (int)job.Request.Priority); _pendingSignal.Release(); Publish(job); }
            }
        }
        catch { /* a corrupt job file should not prevent Gram from starting */ }
    }

    private sealed record PersistedJob(string Id, SandboxJobRequest Request, SandboxJobStatus Status, string? SandboxId, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private SandboxJobSnapshot ToSnapshot(Job job)
    {
        int? position = null;
        if (job.Status == SandboxJobStatus.Queued) lock (_pendingLock) position = _pending.UnorderedItems.Count(item => item.Element.Id != job.Id && item.Priority <= (int)job.Request.Priority) + 1;
        return new(job.Id, job.Status, position, job.SandboxId, job.Error, job.CreatedAt, job.UpdatedAt, job.Request.Priority, job.Request.ResourceClass, job.Request.MaxRuntimeSeconds);
    }
}

public sealed class SandboxJobWorker : BackgroundService
{
    private readonly SandboxJobQueue _queue;
    private readonly IGramBoxService _gramBox;
    private readonly ILogger<SandboxJobWorker> _logger;
    public SandboxJobWorker(SandboxJobQueue queue, IGramBoxService gramBox, ILogger<SandboxJobWorker> logger) { _queue = queue; _gramBox = gramBox; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SandboxJobQueue.JobWork? work = null;
            try
            {
                work = await _queue.DequeueAsync(stoppingToken);
                if (work is null) continue;
                if (work.Job.Cancellation.IsCancellationRequested) { _queue.PublishStatus(work, SandboxJobStatus.Cancelled); continue; }
                _queue.PublishStatus(work, SandboxJobStatus.Starting);
                var request = work.Job.Request;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(work.Job.Cancellation.Token, stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.MaxRuntimeSeconds, 1, 180)));
                var session = await _gramBox.CreateSandboxAsync(request.RepositoryPath, taskName: request.Name, runId: request.RunId, networkMode: Enum.TryParse<SandboxNetworkMode>(request.NetworkMode, true, out var mode) ? mode : SandboxNetworkMode.Disabled, allowedNetworkHosts: request.AllowedNetworkHosts, resourceClass: request.ResourceClass, ct: timeout.Token);
                _queue.PublishStatus(work, SandboxJobStatus.Ready, session.Id);
            }
            catch (OperationCanceledException) when (work?.Job.Cancellation.IsCancellationRequested == true || stoppingToken.IsCancellationRequested)
            { if (work is not null) _queue.PublishStatus(work, SandboxJobStatus.Cancelled); }
            catch (Exception ex)
            { if (work is not null) _queue.PublishStatus(work, SandboxJobStatus.Failed, error: ex.Message); else _logger.LogError(ex, "Sandbox worker failed before receiving a job."); }
            finally { work?.Dispose(); }
        }
    }
}
