using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.Common.JobController;

namespace VpnHood.Common.Collections;

public class TaskCollection : IAsyncDisposable, IJob
{
    private readonly ConcurrentDictionary<Task, bool> _tasks = new();
    public JobSection JobSection { get; } = new();

    public void Add(Task task)
    {
        _tasks.TryAdd(task, true);
    }

    public void Add(ValueTask valueTask)
    {
        _tasks.TryAdd(valueTask.AsTask(), true);
    }

    public TaskCollection()
    {
        JobRunner.Default.Add(this);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_tasks.Keys);
    }

    public Task RunJob()
    {
        var completedItems = _tasks.Where(x => x.Key.IsCompleted).ToArray();
        foreach (var item in completedItems)
            _tasks.TryRemove(item.Key, out _);

        return Task.CompletedTask;
    }
}