using System.Security.Cryptography;
using System.Text;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App;

// The app's files as entries of a key-value store the platform keeps where it keeps no files (tvOS's
// defaults: VpnHood.AppLib.App.Ios's UserDefaultsStorageProvider), one entry per file, named by its path under a
// root folder. Such a store is small - tvOS ends an app whose defaults reach 1 MB - so the entries share a
// budget, and a write past it fails with the old content kept.
public abstract class KeyValueStorageProvider : IStorageProvider
{
    private const string KeyPrefix = "VpnHood.AppFiles/";
    private readonly string _rootFolderPath;
    private readonly long _budget;
    private readonly Lock _writeLock = new();
    private long? _size;

    protected KeyValueStorageProvider(string rootFolderPath, long budget)
    {
        _rootFolderPath = Path.GetFullPath(rootFolderPath);
        _budget = budget;
    }

    protected abstract string? GetValue(string key);
    protected abstract void SetValue(string key, string value);
    protected abstract void RemoveValue(string key);

    // every key of the platform's store, the store's own and others'
    protected abstract IReadOnlyList<string> GetKeys();

    public string? Read(string filePath)
    {
        return GetValue(ToKey(filePath));
    }

    public void Write(string filePath, string content)
    {
        var key = ToKey(filePath);
        lock (_writeLock) {
            var size = GetSize() - GetEntrySize(key, GetValue(key)) + GetEntrySize(key, content);
            if (size > _budget)
                throw new IOException(
                    $"This device keeps at most {_budget / 1024} KB of the app's data, and this change would " +
                    $"take it to {size / 1024} KB. Remove some access keys, proxies or split-tunnel entries.");

            SetValue(key, content);
            _size = size;
        }
    }

    public void Delete(string filePath)
    {
        var key = ToKey(filePath);
        lock (_writeLock) {
            _size = GetSize() - GetEntrySize(key, GetValue(key));
            RemoveValue(key);
        }
    }

    // the content's hash, as an entry has no time of its own
    public string GetSignature(string filePath)
    {
        var content = Read(filePath);
        return content is null
            ? "none"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)), 0, 8);
    }

    // what the entries take, counted once: every write after keeps it
    private long GetSize()
    {
        return _size ??= GetKeys()
            .Where(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            .Sum(key => GetEntrySize(key, GetValue(key)));
    }

    private static long GetEntrySize(string key, string? value)
    {
        return value is null ? 0 : Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value);
    }

    private string ToKey(string filePath)
    {
        var relativePath = Path.GetRelativePath(_rootFolderPath, Path.GetFullPath(filePath));
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"The file is outside the store's folder. FilePath: {filePath}",
                nameof(filePath));

        return KeyPrefix + relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
