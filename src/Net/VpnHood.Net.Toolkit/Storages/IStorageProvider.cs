namespace VpnHood.Net.Toolkit.Storages;

// The platform's part of a Storage: small text files by full path, kept as files or as the entries of a
// store that keeps no files. Sync, as it holds the app's own state, which the app reads as it starts - on
// the main thread, from each head's sync entry - and writes behind sync setters. A store on the network
// would need a contract of its own: async, with a local copy and timeouts.
public interface IStorageProvider
{
    // null where there is none
    string? Read(string filePath);

    // A write the provider cannot keep fails, and the old content stays.
    void Write(string filePath, string content);

    void Delete(string filePath);

    // Changes whenever the content does - for a file, its time and length, read without the content.
    string GetSignature(string filePath);
}
