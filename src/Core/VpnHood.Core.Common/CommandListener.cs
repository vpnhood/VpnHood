using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Common;

public class CommandListener(string commandFilePath) : IDisposable
{
    private FileSystemWatcher? _fileSystemWatcher;
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;
    public bool IsStarted => _fileSystemWatcher != null;
    public string CommandFilePath => commandFilePath;

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

    public void SendCommand(string command)
    {
        VhLogger.Instance.LogInformation("Broadcasting a command. Command: {Command}", command);
        Directory.CreateDirectory(Path.GetDirectoryName(commandFilePath)!);
        File.WriteAllText(commandFilePath, command);
    }

    public void TrySendCommand(string command)
    {
        try {
            SendCommand(command);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send command.");
        }
    }

    protected void OnCommand(string[] args)
    {
        CommandReceived?.Invoke(this, new CommandReceivedEventArgs(args));
    }

    public void Dispose()
    {
        _fileSystemWatcher?.Dispose();
    }
}