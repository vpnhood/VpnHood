using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.AppUi.SmokeTest;

// Edge, which comes with Windows, headless in a profile folder of its own, with one page open and read through its
// DevTools socket (Runtime.evaluate), so a test needs no browser download and no package.
internal sealed class HeadlessEdge : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _profileFolder;
    private readonly ClientWebSocket _socket = new();
    private int _lastId;

    private HeadlessEdge(Process process, string profileFolder)
    {
        _process = process;
        _profileFolder = profileFolder;
    }

    public static string? FindExecutable()
    {
        return new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            }
            .Select(x => Path.Combine(x, @"Microsoft\Edge\Application\msedge.exe"))
            .FirstOrDefault(File.Exists);
    }

    public static async Task<HeadlessEdge> StartAsync(string executablePath, Uri pageUrl, CancellationToken cancellationToken)
    {
        var profileFolder = Path.Combine(Path.GetTempPath(), $"VpnHood.UiSmokeTest.Edge.{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
        // --do-not-de-elevate: started elevated, Edge starts its browser again, de-elevated and outside this
        // process's tree, and exits, so the kill at the end would leave the browser running
        foreach (var argument in new[] {
                     "--headless=new", "--remote-debugging-port=0", $"--user-data-dir={profileFolder}", "--no-first-run",
                     "--no-default-browser-check", "--do-not-de-elevate", "--enable-unsafe-swiftshader", pageUrl.ToString()
                 })
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Edge did not start.");
        var edge = new HeadlessEdge(process, profileFolder);
        try {
            await edge.ConnectAsync(cancellationToken);
            return edge;
        }
        catch {
            await edge.DisposeAsync();
            throw;
        }
    }

    // Edge writes the port it chose into the profile, and the page's own socket is in its list of targets
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var portFile = Path.Combine(_profileFolder, "DevToolsActivePort");
        while (!File.Exists(portFile) || new FileInfo(portFile).Length == 0)
            await Task.Delay(200, cancellationToken);

        var portLines = await File.ReadAllLinesAsync(portFile, cancellationToken);
        using var httpClient = new HttpClient();
        while (true) {
            var targetsJson = await httpClient.GetStringAsync($"http://127.0.0.1:{portLines[0]}/json/list", cancellationToken);
            using var targets = JsonDocument.Parse(targetsJson);
            var page = targets.RootElement.EnumerateArray().FirstOrDefault(x => x.GetProperty("type").GetString() == "page");
            if (page.ValueKind == JsonValueKind.Object) {
                var socketUrl = page.GetProperty("webSocketDebuggerUrl").GetString()
                                ?? throw new InvalidOperationException("Edge names no DevTools socket for the page.");

                await _socket.ConnectAsync(new Uri(socketUrl), cancellationToken);
                return;
            }

            await Task.Delay(200, cancellationToken);
        }
    }

    public async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var id = ++_lastId;
        var request = JsonSerializer.SerializeToUtf8Bytes(new {
            id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true }
        });

        await _socket.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

        // no domain is enabled, so nothing else comes in, but a reply is matched by its id all the same
        while (true) {
            using var reply = await ReceiveAsync(cancellationToken);
            if (!reply.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id)
                continue;

            var result = reply.RootElement.GetProperty("result");
            if (result.TryGetProperty("exceptionDetails", out var exception))
                throw new InvalidOperationException($"The page threw on '{expression}': {exception}");

            return result.GetProperty("result").GetProperty("value").Clone();
        }
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true) {
            var received = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("Edge closed its DevTools socket.");

            message.Write(buffer, 0, received.Count);
            if (received.EndOfMessage)
                break;
        }

        message.Position = 0;
        return await JsonDocument.ParseAsync(message, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _socket.Dispose();
        try {
            if (!_process.HasExited) {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Edge (pid {_process.Id}) may still be running: {ex.Message}");
        }

        _process.Dispose();

        // its helpers let go of the profile a moment after they die
        for (var attempt = 0; attempt < 10 && Directory.Exists(_profileFolder); attempt++) {
            try {
                Directory.Delete(_profileFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                await Task.Delay(300, CancellationToken.None);
            }
        }

        if (Directory.Exists(_profileFolder))
            Console.WriteLine($"Edge's profile folder is left behind: {_profileFolder}");
    }
}
