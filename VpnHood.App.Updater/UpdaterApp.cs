using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.App.Updater;

public class UpdaterApp : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly Timer _timer;
    private string UpdateFolderPath { get; }
    private readonly CancellationTokenSource _cancellationTokenSource;
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;
    public PublishInfo LocalPublishInfo { get; }
    public event EventHandler? InstallerScriptExecuted;


    public UpdaterApp(UpdaterOptions options)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        UpdateFolderPath = options.UpdateFolderPath;

        var localPublishInfoJson = File.ReadAllText(options.PublishInfoFilePath);
        LocalPublishInfo = JsonSerializer.Deserialize<PublishInfo>(localPublishInfoJson) ??
                           throw new Exception($"Could not load {nameof(PublishInfo)}!");

        // make sure UpdateFolderPath exists 
        _timer = new Timer(CheckUpdateInterval, null, TimeSpan.Zero, options.CheckInterval);
        Directory.CreateDirectory(UpdateFolderPath);
    }

    private void CheckUpdateInterval(object? state)
    {
        CheckUpdate().ContinueWith(x =>
        {
            if (x.Exception != null)
                Console.WriteLine($"Could not check for update. Error: {x.Exception.Message}");

        }, CancellationToken);
    }

    public async Task CheckUpdate()
    {
        // read online version
        Console.WriteLine($"Checking for update on {LocalPublishInfo.UpdateInfoUrl}. CurrentVersion: {LocalPublishInfo.Version}");
        var onlinePublishInfoJson = await _httpClient.GetStringAsync(LocalPublishInfo.UpdateInfoUrl, CancellationToken);
        var onlinePublishInfo = JsonSerializer.Deserialize<PublishInfo>(onlinePublishInfoJson) ??
                                throw new Exception($"Could not load {nameof(PublishInfo)}!");

        // download if newer
        if (onlinePublishInfo.Version <= LocalPublishInfo.Version)
        {
            Console.WriteLine("Your app is uptodate");
            return;
        }


        Console.WriteLine($"Updating has been started... " +
                          $"CurrentVersion: {LocalPublishInfo.Version}, " +
                          $"OnlineVersion: {onlinePublishInfo.Version}, " +
                          $"UpdateScriptUrl: {onlinePublishInfo.UpdateScriptUrl}");

        var scriptFile = await DownloadFile(onlinePublishInfo.UpdateScriptUrl);
        await RunScriptFile(scriptFile).WaitForExitAsync(CancellationToken);
        InstallerScriptExecuted?.Invoke(this, EventArgs.Empty);
    }

    private async Task<string> DownloadFile(Uri fileUri)
    {
        Console.WriteLine($"Downloading new version! Url: {fileUri}");
        var scriptFilePath = Path.Combine(UpdateFolderPath, Path.GetFileName(fileUri.LocalPath));

        // open source stream from net
        await using var readStream = await _httpClient.GetStreamAsync(fileUri, CancellationToken);
        await using var writeStream = File.OpenWrite(scriptFilePath);
        await readStream.CopyToAsync(writeStream, CancellationToken);
        return scriptFilePath;
    }

    public static Process RunScriptFile(string scriptFilePath)
    {
        var process = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RunWindowsScriptFile(scriptFilePath)
            : RunLinuxScriptFile(scriptFilePath);

        if (process == null)
            throw new Exception("Could not launch the updater.");

        return process;
    }

    public static Process RunLinuxScriptFile(string scriptFilePath)
    {
        return Process.Start("sh", scriptFilePath);
    }

    private static Process RunWindowsScriptFile(string scriptFilePath)
    {
        return Process.Start("powershell", scriptFilePath);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _timer.Dispose();
    }
}
