using System.Collections.Concurrent;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// The host's main thread, lent to the one thing that needs it: the UI's window, which on Windows
// must run on the STA thread Main was given. Everything else - the parser, every command - runs off
// it; the ui command posts its window here and waits for it, and the main thread runs what is
// posted until the whole command line is done.
internal sealed class MainThreadQueue : IDisposable
{
    private readonly BlockingCollection<Action> _actions = new();
    private volatile bool _isBusy;

    // Running something posted right now: the main thread is not free to end the process.
    public bool IsBusy => _isBusy;

    // From any thread: the action, on the main thread; done when it has returned.
    public Task Run(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _actions.Add(() => {
            try {
                action();
                done.SetResult();
            }
            catch (Exception ex) {
                done.SetException(ex);
            }
        });

        return done.Task;
    }

    // On the main thread: runs what is posted until the task is done. Whatever posted an action
    // waits for it, so nothing is posted once the task is done.
    public void RunUntil(Task task)
    {
        task.ContinueWith(_ => _actions.CompleteAdding(), TaskScheduler.Default);
        foreach (var action in _actions.GetConsumingEnumerable()) {
            _isBusy = true;
            try {
                action();
            }
            finally {
                _isBusy = false;
            }
        }
    }

    public void Dispose()
    {
        _actions.Dispose();
    }
}
