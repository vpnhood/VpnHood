using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// One window per person: the open window serves a named pipe of its own person's - .NET's pipes are
// Unix sockets on Linux, so this one piece serves both desktops - and a second launch by the same
// person asks it to come forward and ends. Another person's launch finds no pipe of theirs and gets
// a window of its own. The pipe is asked before it is served, since on Linux a second server would
// take the name over rather than fail, and the open window always has the next listener waiting,
// so the name never goes unanswered between two launches.
internal sealed class DesktopUiInstance : IAsyncDisposable
{
    private const string ShowCommand = "show";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _serving;

    private DesktopUiInstance(string pipeName)
    {
        _pipeName = pipeName;
        _serving = Serve(CreateServer(), _cancellation.Token);
    }

    // This launch's window, or null when this person already has one open, which has been asked to
    // come forward.
    public static async Task<DesktopUiInstance?> TryClaim(string instanceName,
        CancellationToken cancellationToken)
    {
        var pipeName = BuildPipeName(instanceName);
        if (await TryShowExisting(pipeName, cancellationToken).Vhc())
            return null;

        return new DesktopUiInstance(pipeName);
    }

    // one pipe per person and instance, in characters every platform's pipe names take
    private static string BuildPipeName(string instanceName)
    {
        var name = $"{instanceName}-ui-{Environment.UserName}";
        return new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
    }

    private static async Task<bool> TryShowExisting(string pipeName, CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, Options);
        try {
            await client.ConnectAsync(ConnectTimeout, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) {
            return false; // nobody serves it: this launch is the window
        }

        await using var writer = new StreamWriter(client);
        await writer.WriteLineAsync(ShowCommand.AsMemory(), cancellationToken).Vhc();
        await writer.FlushAsync(cancellationToken).Vhc();
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

    // What a second launch asks: this window, to the front.
    private static async Task Answer(NamedPipeServerStream connected, CancellationToken cancellationToken)
    {
        await using var pipe = connected;
        try {
            using var reader = new StreamReader(pipe);
            var command = await reader.ReadLineAsync(cancellationToken).Vhc();
            if (command == ShowCommand && AppUiContext.Context is { } uiContext)
                await uiContext.BringToFront(cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "Could not bring the window forward for a second launch.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().Vhc();
        await _serving.Vhc();
        _cancellation.Dispose();
    }
}
