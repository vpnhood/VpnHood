using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Common;

public class CommandListener(string commandFilePath) : IDisposable
{
    private FileSystemWatcher? _fileSystemWatcher;
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;
    public bool IsStarted => _fileSystemWatcher != null;

    public void Start()
    {
        if (IsStarted)
            throw new Exception($"{nameof(CommandListener)} is already started!");

        try {
            // delete old command
            if (File.Exists(commandFilePath))
                File.Delete(commandFilePath);

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

            _fileSystemWatcher.Changed += (_, e) => {
                var command = ReadAllTextAndWait(e.FullPath);
                OnCommand(VhUtil.ParseArguments(command).ToArray());
            };
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(
                $"Could not start CommandListener! Message: {ex.Message}");
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
        try {
            Console.WriteLine($"Broadcasting a server command . command: {command}");
            File.WriteAllText(commandFilePath, command);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send command.");
        }
    }

    protected virtual void OnCommand(string[] args)
    {
        CommandReceived?.Invoke(this, new CommandReceivedEventArgs(args));
    }

    public void Dispose()
    {
        _fileSystemWatcher?.Dispose();
    }
}