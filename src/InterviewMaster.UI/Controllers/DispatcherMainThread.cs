using Avalonia.Threading;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// The UI thread as the processor and the audio coordinator see it. Both calls queue, so work
/// arrives in the order it was sent.
public sealed class DispatcherMainThread : IMainThread
{
    public Task RunAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
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

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
