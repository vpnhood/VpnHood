using System;
using System.Collections.Generic;
using System.Linq;

namespace VpnHood.Core.Toolkit.Memory;

public static class VhTypeTracker
{
    private sealed record TrackedReference(WeakReference Reference, string Name);

    private static readonly List<TrackedReference> TrackedObjects = new();
    private static readonly Dictionary<string, long> Counters = new(StringComparer.Ordinal);
    private static int _addedCount;
    private static bool _enabled;

    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set
        {
            lock (TrackedObjects)
            {
                if (value && !_enabled)
                {
                    TrackedObjects.Clear();
                    Counters.Clear();
                    _addedCount = 0;
                }

                Volatile.Write(ref _enabled, value);
            }
        }
    }

    public static bool Track(object obj)
    {
        return Track(obj, obj.GetType().Name);
    }

    public static bool Track(object obj, string diagnosticName)
    {
        if (!Enabled || obj == null) return false;
        lock (TrackedObjects)
        {
            TrackedObjects.Add(new TrackedReference(new WeakReference(obj), diagnosticName));
            IncrementNoLock($"{diagnosticName}.created");
            _addedCount++;
            if (_addedCount % 100 == 0)
            {
                TrackedObjects.RemoveAll(r => !r.Reference.IsAlive);
            }
        }
        return true;
    }

    public static void Record(string eventName)
    {
        if (!Enabled) return;
        lock (TrackedObjects)
        {
            IncrementNoLock(eventName);
        }
    }

    public static string GetSnapshotString()
    {
        lock (TrackedObjects)
        {
            TrackedObjects.RemoveAll(r => !r.Reference.IsAlive);
            var summary = TrackedObjects
                .Select(r => (r.Name, Target: r.Reference.Target))
                .Where(x => x.Target != null)
                .GroupBy(x => x.Target is Task task
                    ? $"{x.Name}.{(task.IsCompleted ? "Completed" : "Active")}" 
                    : x.Name)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}:{g.Select(x => x.Target!).Distinct(ReferenceEqualityComparer.Instance).Count()}")
                .ToList();
            var counters = Counters
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}:{x.Value}");
            return $"live=[{string.Join(" ", summary)}] events=[{string.Join(" ", counters)}]";
        }
    }

    private static void IncrementNoLock(string eventName)
    {
        Counters.TryGetValue(eventName, out var value);
        Counters[eventName] = value + 1;
    }
}
