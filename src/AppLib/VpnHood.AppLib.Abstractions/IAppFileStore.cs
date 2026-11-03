namespace VpnHood.AppLib.Abstractions;

// Where the app keeps the small files it cannot rebuild - the settings, the profiles with their keys, the
// state, the install id, the sign-in session, the split lists - each named by the path it has as a file.
// Plain files on every platform but tvOS, which empties every folder an app may write when the device runs
// low on storage and keeps only the app's defaults: there each file is an entry of those. What can be
// fetched or made again - the log, the caches, the downloads - stays in plain files everywhere.
public interface IAppFileStore
{
    // null where there is none
    string? Read(string filePath);

    // A write the store cannot keep fails, and the old content stays.
    void Write(string filePath, string content);

    void Delete(string filePath);

    // Changes whenever the content does - a file's time and length, read without the content.
    string GetSignature(string filePath);
}
