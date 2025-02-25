using System.Collections.Concurrent;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Collections;

public class TaskCollection : IAsyncDisposable, IJob
{
    private readonly ConcurrentDictionary<Task, bool> _tasks = new();
    public JobSection JobSection { get; } = new();

    public TaskCollection()
    {
        JobRunner.Default.Add(this);
    }

    public void Add(Task task)
    {
        _tasks.TryAdd(task, true);
    }

    public void Add(ValueTask valueTask)
    {
        _tasks.TryAdd(valueTask.AsTask(), true);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_tasks.Keys).VhConfigureAwait();
    }

    public Task RunJob()
    {
        var completedItems = _tasks.Where(x => x.Key.IsCompleted).ToArray();
        foreach (var item in completedItems)
            _tasks.TryRemove(item.Key, out _);

        return Task.CompletedTask;
    }
}