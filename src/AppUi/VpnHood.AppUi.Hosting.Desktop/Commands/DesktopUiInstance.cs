using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// One window per person and session: the open window serves a named pipe of its own person's and
// session's - .NET's pipes are Unix sockets on Linux, so this one piece serves both desktops - and a
// second launch there asks it to come forward and ends. Another person, or the same one in another
// session (the console and Remote Desktop), finds no pipe of theirs and gets a window of its own.
//
// A launch looks for the window and, finding none, serves the name, both under a lock of the
// session, so of two launches at once - the sign-in entry's and a person's click - one opens the
// window and the other finds it. The pipe is asked before it is served, since on Linux a second
// server would take the name over rather than fail, and the open window always has the next
// listener waiting, so the name never goes unanswered between two launches.
//
// A launch given an access key - a file opened with the app - hands it to the open window, whose UI
// imports it, before bringing it forward: in the Store edition the app is in that window's process,
// and nothing else could reach it.
internal sealed class DesktopUiInstance : IAsyncDisposable
{
    private const string ShowCommand = "show";
    private const string ImportAccessKeyCommand = "import-access-key"; // the key on the next line
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    private readonly string _pipeName;
    private readonly IDesktopUi _ui;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _serving;
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    private DesktopUiInstance(string pipeName, IDesktopUi ui)
    {
        _pipeName = pipeName;
        _ui = ui;
        _serving = Serve(CreateServer(), _cancellation.Token);
    }

    // This launch's window, the UI a later launch brings forward, or null when this person already
    // has one open in this session, which has been asked to come forward and been handed the access
    // key, if this launch has one. On the calling thread throughout: the lock is let go only once this
    // launch's server holds the name.
    public static DesktopUiInstance? TryClaim(IDesktopUiPaths paths, IDesktopUi ui, string? accessKey)
    {
        var name = BuildName(paths.InstanceName);
        using var sessionLock = TakeSessionLock(paths.UiDataPath, name);
        return TryShowExisting(name, accessKey) ? null : new DesktopUiInstance(name, ui);
    }

    // one name per instance, person and session, in characters every platform's pipe and file names take
    private static string BuildName(string instanceName)
    {
        var name = GetSession() is { Length: > 0 } session
            ? $"{instanceName}-ui-{Environment.UserName}-{session}"
            : $"{instanceName}-ui-{Environment.UserName}";
        return new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
    }

    // The session the window opens in: Windows' own, which the console and each Remote Desktop
    // session have apart; on Linux its display, which every app of a desktop session is started with,
    // where logind's XDG_SESSION_ID is not: apps the user's systemd starts lack it. Empty with no
    // display, and then the person's alone.
    private static string GetSession()
    {
        if (OperatingSystem.IsWindows()) {
            using var process = Process.GetCurrentProcess();
            return process.SessionId.ToString(CultureInfo.InvariantCulture);
        }

        return Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 } waylandDisplay
            ? waylandDisplay
            : Environment.GetEnvironmentVariable("DISPLAY") ?? "";
    }

    // A file of the person's held with no sharing: an exclusive flock on Linux, a share lock on
    // Windows, which the system lets go of with the process, so a launch that dies holding it leaves
    // nothing behind. Another launch holds it for half a second at most, while it looks for the window.
    private static FileStream TakeSessionLock(string folderPath, string name)
    {
        Directory.CreateDirectory(folderPath);
        var options = new FileStreamOptions {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var path = Path.Combine(folderPath, $"{name}.lock");
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            try {
                return new FileStream(path, options);
            }
            catch (IOException ex) when (InstanceLockFile.IsHeld(ex) && stopwatch.Elapsed < LockTimeout) {
                Thread.Sleep(LockRetryDelay);
            }
        }
    }

    private static bool TryShowExisting(string pipeName, string? accessKey)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, Options);
        try {
            client.Connect(ConnectTimeout);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) {
            return false; // nobody serves it: this launch is the window
        }

        // a key is one line: vh:// and its base64
        using var writer = new StreamWriter(client);
        if (accessKey != null) {
            writer.WriteLine(ImportAccessKeyCommand);
            writer.WriteLine(accessKey);
        }
        else {
            writer.WriteLine(ShowCommand);
        }

        writer.Flush();
        return true;
    }

    private NamedPipeServerStream CreateServer()
    {
        return new NamedPipeServerStream(_pipeName, PipeDirection.In,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, Options);
    }

    private async Task Serve(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                await server.WaitForConnectionAsync(cancellationToken).Vhc();
                var connected = server;
                server = CreateServer();
                _ = Answer(connected, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the window is gone
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The window stopped listening for a second launch.");
        }
        finally {
            await server.DisposeAsync().Vhc();
        }
    }

    // What a second launch asks: this window, to the front, after importing the key it was given.
    private async Task Answer(NamedPipeServerStream connected, CancellationToken cancellationToken)
    {
        await using var pipe = connected;
        try {
            using var reader = new StreamReader(pipe);
            var command = await reader.ReadLineAsync(cancellationToken).Vhc();
            if (command == ImportAccessKeyCommand &&
                await reader.ReadLineAsync(cancellationToken).Vhc() is { } accessKey) {
                await _ui.ImportAccessKey(accessKey, cancellationToken).Vhc();
                await _ui.BringToFront(cancellationToken).Vhc();
            }
            else if (command == ShowCommand) {
                await _ui.BringToFront(cancellationToken).Vhc();
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "Could not bring the window forward for a second launch.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end
        using var scopeLock = await _disposeLock.LockAsync().Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        await _cancellation.CancelAsync().Vhc();
        await _serving.Vhc();
        _cancellation.Dispose();
    }
}
