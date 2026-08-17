using System.Collections.Concurrent;

namespace CanonScanStudio.Scanning.Wia;

/// <summary>
/// WIA Automation exige STA. Todas las llamadas COM se serializan en un hilo dedicado.
/// </summary>
internal sealed class WiaStaDispatcher : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _thread;
    private volatile bool _disposed;

    public WiaStaDispatcher()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "CanonScanStudio-WIA-STA"
        };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }
        _thread.Start();
    }

    public T Invoke<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new WorkItem(() => func(), typeof(T), cancellationToken);
        _queue.Add(item, cancellationToken);
        return (T)item.Completion.Task.GetAwaiter().GetResult()!;
    }

    public void Invoke(Action action, CancellationToken cancellationToken = default) =>
        Invoke<object?>(() =>
        {
            action();
            return null;
        }, cancellationToken);

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new WorkItem(() => func(), typeof(T), cancellationToken);
        _queue.Add(item, cancellationToken);
        return item.Completion.Task.ContinueWith(t => (T)t.Result!, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Loop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
                continue;
            }

            try
            {
                item.Completion.TrySetResult(item.Func());
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // El hilo STA puede estar bloqueado en Transfer(); no se fuerza el abort.
        }

        _queue.Dispose();
    }

    private sealed class WorkItem
    {
        public WorkItem(Func<object?> func, Type resultType, CancellationToken cancellationToken)
        {
            Func = func;
            ResultType = resultType;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Func<object?> Func { get; }
        public Type ResultType { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<object?> Completion { get; }
    }
}
