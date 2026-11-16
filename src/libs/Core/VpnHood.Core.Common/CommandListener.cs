using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Common;

/// <summary>
/// Passes commands between runs of an app through a file: one run writes a command line to it, and another, watching
/// it, gets the command's arguments.
/// </summary>
/// <param name="commandFilePath">The command file's path.</param>
public class CommandListener(string commandFilePath) : IDisposable
{
    private FileSystemWatcher? _fileSystemWatcher;

    /// <summary>
    /// Occurs when the command file is written, with its command line split into arguments, on the file watcher's
    /// thread. A failure to read the file or of a handler is logged.
    /// </summary>
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    /// <summary>Gets a value indicating whether the listener is watching the file.</summary>
    public bool IsStarted => _fileSystemWatcher != null;

    /// <summary>Gets the command file's path.</summary>
    public string CommandFilePath => commandFilePath;

    /// <summary>
    /// Starts watching the command file, making its folder if missing, then deletes a command an earlier run left.
    /// </summary>
    /// <remarks>A failure to start is logged, and leaves the listener stopped or watching, as far as it got.</remarks>
    /// <exception cref="Exception">The listener is started already.</exception>
    public void Start()
    {
        if (IsStarted)
            throw new Exception("CommandListener is already started!");

        try {
            var watchFolderPath = Path.GetDirectoryName(commandFilePath)!;
            Directory.CreateDirectory(watchFolderPath);

            // watch new commands
            _fileSystemWatcher = new FileSystemWatcher {
                Path = watchFolderPath,
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(commandFilePath),
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileSystemWatcher.Changed += FileSystemWatcher_Changed;

            // A previous run's command goes, but only once the watch is on: a stop command still
            // waiting sends its command again when it sees the file gone.
            if (File.Exists(commandFilePath))
                File.Delete(commandFilePath);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not start CommandListener! ");
        }
    }

    // a failure here would escape the watcher's thread and end the process
    private void FileSystemWatcher_Changed(object? sender, FileSystemEventArgs e)
    {
        try {
            var command = ReadAllTextAndWait(e.FullPath);
            OnCommand(VhUtils.ParseArguments(command).ToArray());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not run the command. CommandFile: {CommandFile}", e.FullPath);
        }
    }

    /// <summary>Stops watching the command file.</summary>
    public void Stop()
    {
        _fileSystemWatcher?.Dispose();
        _fileSystemWatcher = null;
    }

    private static string ReadAllTextAndWait(string fileName, long retry = 5)
    {
        Exception exception = new($"Could not read {fileName}");
        for (var i = 0; i < retry; i++)
            try {
                return File.ReadAllText(fileName);
            }
            catch (IOException ex) {
                exception = ex;
                Thread.Sleep(500);
            }

        throw exception;
    }

    /// <summary>Writes a command line to the command file, for the run that watches it.</summary>
    /// <param name="command">The command line.</param>
    public void SendCommand(string command)
    {
        VhLogger.Instance.LogInformation("Broadcasting a command. Command: {Command}", command);
        Directory.CreateDirectory(Path.GetDirectoryName(commandFilePath)!);
        File.WriteAllText(commandFilePath, command);
    }

    /// <summary>Writes a command line to the command file, as <see cref="SendCommand"/> does, logging a failure.</summary>
    /// <param name="command">The command line.</param>
    public void TrySendCommand(string command)
    {
        try {
            SendCommand(command);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send command.");
        }
    }

    /// <summary>Raises <see cref="CommandReceived"/>.</summary>
    /// <param name="args">The command's arguments.</param>
    protected void OnCommand(string[] args)
    {
        CommandReceived?.Invoke(this, new CommandReceivedEventArgs(args));
    }

    /// <summary>Stops watching the command file.</summary>
    public void Dispose()
    {
        _fileSystemWatcher?.Dispose();
    }
}