using System.Collections.Concurrent;

namespace Gram.Blazor.Shared;

/// <summary>
/// Buffers incoming streaming tokens and yields them at a calm, readable pace
/// rather than dumping raw chunk bursts into the UI.
/// </summary>
public sealed class SmoothStreamBuffer
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Func<string, Task> _onAppend;
    private readonly Func<Task> _onRender;
    private readonly CancellationTokenSource _internalCts = new();
    private Task? _pacerTask;
    private volatile bool _completed;

    public SmoothStreamBuffer(Func<string, Task> onAppend, Func<Task> onRender)
    {
        _onAppend = onAppend;
        _onRender = onRender;
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token).Token;
        _pacerTask = Task.Run(async () =>
        {
            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    if (_queue.TryDequeue(out var slice))
                    {
                        await _onAppend(slice);
                        await _onRender();

                        // Calm, readable human pace
                        var remaining = _queue.Count;
                        var delayMs = remaining > 80 ? 10 : (remaining > 30 ? 18 : 28);
                        await Task.Delay(delayMs, linkedToken);
                    }
                    else
                    {
                        if (_completed) break;
                        await Task.Delay(14, linkedToken);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, linkedToken);
    }

    public void Enqueue(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Partition into small, digestible 1-2 character atoms
        for (var i = 0; i < text.Length; i += 2)
        {
            var len = Math.Min(2, text.Length - i);
            _queue.Enqueue(text.Substring(i, len));
        }
    }

    public async Task FlushAndCompleteAsync()
    {
        _completed = true;
        while (_queue.TryDequeue(out var slice))
        {
            await _onAppend(slice);
            await _onRender();
            await Task.Delay(12);
        }

        _internalCts.Cancel();
        if (_pacerTask != null)
        {
            try
            {
                await _pacerTask;
            }
            catch { }
        }
    }

    public void Cancel()
    {
        _completed = true;
        _internalCts.Cancel();
    }
}
