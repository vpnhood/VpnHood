using System.Security.Cryptography;
using System.Text;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides the app's files as entries of a key-value store the platform keeps where it keeps no files, such as
/// tvOS's defaults: one entry per file, named by its path under a root folder.
/// </summary>
/// <remarks>
/// Such a store is small (tvOS ends an app whose defaults reach 1 MB), so the entries share a budget, and a write past
/// it fails with the old content kept. The entries' keys start with <c>VpnHood.AppFiles/</c>; the store's other keys
/// count for nothing.
/// </remarks>
public abstract class KeyValueStorageProvider : IStorageProvider
{
    private const string KeyPrefix = "VpnHood.AppFiles/";
    private readonly string _rootFolderPath;
    private readonly long _budget;
    private readonly Lock _writeLock = new();
    private long? _size;

    /// <summary>Initializes a new instance of the <see cref="KeyValueStorageProvider"/> class.</summary>
    /// <param name="rootFolderPath">
    /// The folder the files' paths must be under, which their keys are relative to.
    /// </param>
    /// <param name="budget">The most the entries may take together, in bytes of UTF-8, their keys counted.</param>
    protected KeyValueStorageProvider(string rootFolderPath, long budget)
    {
        _rootFolderPath = Path.GetFullPath(rootFolderPath);
        _budget = budget;
    }

    /// <summary>Reads an entry of the platform's store.</summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>The entry's value, or <see langword="null"/> where there is none.</returns>
    protected abstract string? GetValue(string key);

    /// <summary>Writes an entry of the platform's store.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="value">The value.</param>
    protected abstract void SetValue(string key, string value);

    /// <summary>Removes an entry of the platform's store, if there is one.</summary>
    /// <param name="key">The entry's key.</param>
    protected abstract void RemoveValue(string key);

    /// <summary>Returns every key of the platform's store, this provider's own and others'.</summary>
    /// <returns>The keys.</returns>
    protected abstract IReadOnlyList<string> GetKeys();

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is outside the root folder.</exception>
    public string? Read(string filePath)
    {
        return GetValue(ToKey(filePath));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is outside the root folder.</exception>
    /// <exception cref="IOException">
    /// The write would take the entries past the budget; the old content is kept.
    /// </exception>
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

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is outside the root folder.</exception>
    public void Delete(string filePath)
    {
        var key = ToKey(filePath);
        lock (_writeLock) {
            // kept only once the entry is gone: a removal that fails frees nothing
            var size = GetSize() - GetEntrySize(key, GetValue(key));
            RemoveValue(key);
            _size = size;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unlike a file's, it reads the entry's whole value: an entry has no time of its own, so the signature is the
    /// start of its value's SHA-256 hash, or <c>none</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is outside the root folder.</exception>
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
