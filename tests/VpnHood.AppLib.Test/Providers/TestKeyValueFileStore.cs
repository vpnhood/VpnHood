using System.Collections.Concurrent;
using VpnHood.AppLib.App;

namespace VpnHood.AppLib.Test.Providers;

// An Apple TV's defaults, in memory (KeyValueFileStore): they outlive the app's folders, as the defaults
// outlive tvOS emptying an app's caches.
public class TestKeyValueFileStore(string rootFolderPath, long budget = 256 * 1024)
    : KeyValueFileStore(rootFolderPath, budget)
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    protected override string? GetValue(string key) => _values.GetValueOrDefault(key);
    protected override void SetValue(string key, string value) => _values[key] = value;
    protected override void RemoveValue(string key) => _values.TryRemove(key, out _);
    protected override IReadOnlyList<string> GetKeys() => _values.Keys.ToArray();
}
