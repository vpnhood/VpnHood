using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace VpnHood.AppUi.SmokeTest;

// The Avalonia dev head, run for a walk. It takes "--storage", so the run gets a folder of its
// own and nothing an installed client wrote is read, changed or deleted; the folder is made fresh,
// so every walk is a first run and sees what a new install sees, the terms page included.
// "--connect" asks for the Connect product's look, which is the one most people meet.
internal sealed class DevHeadSession : IDisposable
{
    // Named for what it is, and never anything else, because this folder gets deleted.
    private const string StorageFolderName = "VpnHood.UiSmokeTest";
    private const string ExecutableName = "VpnHoodAvaloniaDev.exe";

    private DevHeadSession(UiDriver driver, string storageFolderPath)
    {
        Driver = driver;
        StorageFolderPath = storageFolderPath;
    }

    public UiDriver Driver { get; }
    public string StorageFolderPath { get; }
    public string LogPath => Path.Combine(StorageFolderPath, "app.log");

    public static DevHeadSession Start()
    {
        var executablePath = FindExecutable();
        var storageFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StorageFolderName);

        DeleteStorageFolder(storageFolderPath);
        var driver = UiDriver.Start(executablePath,
            ["--connect", "--storage", StorageFolderName], TimeSpan.FromSeconds(90));

        return new DevHeadSession(driver, storageFolderPath);
    }

    // What the head wrote about itself, which is the only account of a failure that survives the
    // window closing.
    public string ReadLog()
    {
        if (!File.Exists(LogPath))
            return "";

        // the head still holds it open, so the read must not ask for the write side
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        Driver.Dispose();
        DeleteStorageFolder(StorageFolderPath);
    }

    private static string FindExecutable()
    {
        var directory = typeof(DevHeadSession).Assembly
                            .GetCustomAttributes<AssemblyMetadataAttribute>()
                            .FirstOrDefault(x => x.Key == "DevHeadDirectory")?.Value
                        ?? throw new InvalidOperationException(
                            "This build recorded no dev head folder; rebuild the test project.");

        var path = Path.Combine(directory, ExecutableName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"The dev head is not built. Run: dotnet build src/Apps/Tools/AvaloniaUI.Dev", path);

        return path;
    }

    // The head keeps a file or two open for a moment after it dies, so a delete is given a few
    // tries before it is believed. The name is checked first: this deletes a folder.
    private static void DeleteStorageFolder(string path)
    {
        if (!string.Equals(Path.GetFileName(path), StorageFolderName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to delete '{path}': it is not the walk's own folder.");

        for (var attempt = 0; attempt < 10; attempt++) {
            if (!Directory.Exists(path))
                return;

            try {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) {
                Thread.Sleep(300);
            }
        }

        throw new IOException($"Could not remove the walk's folder '{path}'; a head may still be running.");
    }
}
