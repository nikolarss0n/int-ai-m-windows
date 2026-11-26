namespace InterviewMaster.Support;

/// The UI thread, as the processor needs it. Both calls keep first-in, first-out order with each other.
public interface IMainThread
{
    /// Runs the work on the UI thread and completes when it has run (`MainActor.run`).
    Task RunAsync(Action action);

    /// Queues the work on the UI thread without waiting (`DispatchQueue.main.async`).
    void Post(Action action);
}

public static class MainThreadExtensions
{
    public static async Task<T> RunAsync<T>(this IMainThread mainThread, Func<T> work)
    {
        T result = default!;
        await mainThread.RunAsync(() => { result = work(); }).ConfigureAwait(false);
        return result;
    }
}

/// A single worker thread that stands in for the UI thread in tests and headless tools.
/// Like a UI thread, it resumes `await`s started on it back on itself.
public sealed class QueueMainThread : IMainThread, IDisposable
{
    private sealed class Context(QueueMainThread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => owner.Post(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (owner.IsCurrent)
            {
                callback(state);
            }
            else
            {
                owner.RunAsync(() => callback(state)).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    private readonly System.Collections.Concurrent.BlockingCollection<Action> queue = new();
    private readonly Thread thread;

    public QueueMainThread()
    {
        thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new Context(this));
            foreach (var work in queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception error)
                {
                    Log.Print($"Main thread work failed: {error.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "InterviewMaster.MainThread"
        };
        thread.Start();
    }

    public bool IsCurrent => Thread.CurrentThread == thread;

    public Task RunAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        });
        return completion.Task;
    }

    public void Post(Action action)
    {
        try
        {
            if (!queue.IsAddingCompleted) queue.Add(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
    }
}
