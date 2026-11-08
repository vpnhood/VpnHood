namespace VpnHood.Net.Toolkit.Storages;

/// <summary>
/// Represents the platform's part of a <see cref="Storage"/>: small text files by full path, kept as files or as the
/// entries of a store that keeps no files.
/// </summary>
/// <remarks>
/// It is synchronous, as it holds the app's own state, which the app reads as it starts, on the main thread, and writes
/// behind synchronous setters. A store on the network would need a contract of its own: asynchronous, with a local copy
/// and timeouts.
/// </remarks>
public interface IStorageProvider
{
    /// <summary>Reads a file's text.</summary>
    /// <param name="filePath">The file's full path.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such file.</returns>
    string? Read(string filePath);

    /// <summary>Writes a file's text, whole or not at all: a write the provider cannot keep fails, and the old text stays.</summary>
    /// <param name="filePath">The file's full path.</param>
    /// <param name="content">The text.</param>
    void Write(string filePath, string content);

    /// <summary>Deletes a file; a missing one is passed over.</summary>
    /// <param name="filePath">The file's full path.</param>
    void Delete(string filePath);

    /// <summary>
    /// Gets a signature of a file, read without its content, which changes when the file is written as a rule: a write
    /// that keeps what the signature reads, such as a file's time and length, shows no change.
    /// </summary>
    /// <param name="filePath">The file's full path.</param>
    /// <returns>The signature.</returns>
    string GetSignature(string filePath);
}
