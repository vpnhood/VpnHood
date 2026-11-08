namespace VpnHood.Net.Toolkit.Memory;

/// <summary>
/// Provides a process-wide tracker of objects and events for memory investigations: it counts, by name, the objects it
/// was given that are still alive, and counts named events.
/// </summary>
/// <remarks>
/// It does nothing until <see cref="Enabled"/>. Each call takes a lock, so it is no tool for production paths.
/// </remarks>
public static class VhTypeTracker
{
    private sealed record TrackedReference(WeakReference Reference, string Name);

    private static readonly List<TrackedReference> TrackedObjects = [];
    private static readonly Dictionary<string, long> Counters = new(StringComparer.Ordinal);
    private static int _addedCount;

    /// <summary>
    /// Gets or sets a value indicating whether the tracker records. Turning it on clears what was recorded before.
    /// </summary>
    public static bool Enabled
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (TrackedObjects)
            {
                if (value && !field)
                {
                    TrackedObjects.Clear();
                    Counters.Clear();
                    _addedCount = 0;
                }

                Volatile.Write(ref field, value);
            }
        }
    }

    /// <summary>Tracks an object under its type's name, holding it weakly, and counts its creation.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is tracked; <see langword="false"/> when the tracker is off.</returns>
    public static bool Track(object obj)
    {
        return Track(obj, obj.GetType().Name);
    }

    /// <summary>
    /// Tracks an object under a name, holding it weakly, and counts the event <c>name.created</c>.
    /// </summary>
    /// <param name="obj">The object, or <see langword="null"/>.</param>
    /// <param name="diagnosticName">The name it is counted under.</param>
    /// <returns>
    /// <see langword="true"/> if it is tracked; <see langword="false"/> when the tracker is off or the object is
    /// <see langword="null"/>.
    /// </returns>
    public static bool Track(object? obj, string diagnosticName)
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

    /// <summary>Counts an event.</summary>
    /// <param name="eventName">The event's name.</param>
    public static void Record(string eventName)
    {
        if (!Enabled) return;
        lock (TrackedObjects)
        {
            IncrementNoLock(eventName);
        }
    }

    /// <summary>
    /// Returns the tracked objects still alive, counted by name, a task's split into its completed and active ones, and
    /// the event counts.
    /// </summary>
    /// <returns>One line, such as <c>live=[Name:3] events=[Name.created:5]</c>.</returns>
    public static string GetSnapshotString()
    {
        lock (TrackedObjects)
        {
            TrackedObjects.RemoveAll(r => !r.Reference.IsAlive);
            var summary = TrackedObjects
                .Select(r => (r.Name, r.Reference.Target))
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
