using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common;

public class CommandListener : IDisposable
{
    private FileSystemWatcher? _fileSystemWatcher;
    private readonly string _commandFilePath;
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    public CommandListener(string commandFilePath)
    {
        _commandFilePath = commandFilePath;
    }

    public bool IsStarted => _fileSystemWatcher != null;

    public void Start()
    {
        if (IsStarted)
            throw new Exception($"{nameof(CommandListener)} is already started!");

        try
        {
            // delete old command
            if (File.Exists(_commandFilePath))
                File.Delete(_commandFilePath);

            var watchFolderPath = Path.GetDirectoryName(_commandFilePath)!;
            Directory.CreateDirectory(watchFolderPath);

            // watch new commands
            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = watchFolderPath,
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(_commandFilePath),
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileSystemWatcher.Changed += (_, e) =>
            {
                var command = ReadAllTextAndWait(e.FullPath);
                OnCommand(Util.ParseArguments(command).ToArray());
            };
        }
        catch (Exception ex)
        {
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
            try
            {
                return File.ReadAllText(fileName);
            }
            catch (IOException ex)
            {
                exception = ex;
                Thread.Sleep(500);
            }

        throw exception;
    }

    public void SendCommand(string command)
    {
        try
        {
            Console.WriteLine($"Broadcasting a server command . command: {command}");
            File.WriteAllText(_commandFilePath, command);
        }
        catch (Exception ex)
        {
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