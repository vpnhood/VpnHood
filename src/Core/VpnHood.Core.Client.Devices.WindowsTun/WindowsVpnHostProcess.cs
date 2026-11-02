using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.WindowsTun.WinNative;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.Devices.WindowsTun;

// The service's side of the VPN host (WindowsVpnHost): its process, started at each connect, one at a time. A job
// ends it with the service's process, a crash's too; the updater, also the service's child, stays out of the job,
// as it must outlive the service.
internal sealed class WindowsVpnHostProcess(string executablePath, string storageFolder, string serviceName)
    : IDisposable
{
    private readonly KillOnCloseJob _job = new();
    private readonly AsyncLock _startLock = new();
    private Process? _process;
    private Task _processExit = Task.CompletedTask; // the process's exit, once told
    private int _endedProcessId; // the one this side ended, whose exit is no news
    private bool _disposed;

    public async Task Start(CancellationToken cancellationToken)
    {
        using var startLock = await _startLock.LockAsync(cancellationToken).Vhc();
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One at a time: the last one, its session over, may be in its last seconds still, and its end would delete
        // the new one's channel file (TcpMessageListener) and could write over its state.
        if (_process is { } lastProcess) {
            if (!lastProcess.HasExited) {
                Volatile.Write(ref _endedProcessId, lastProcess.Id);
                lastProcess.Kill();
            }

            await _processExit.WaitAsync(cancellationToken).Vhc();
            lastProcess.Dispose();
            _process = null;
        }

        var startInfo = new ProcessStartInfo(executablePath,
            [WindowsVpnHost.StorageOption, storageFolder, WindowsVpnHost.ServiceOption, serviceName]) {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = Process.Start(startInfo) ??
                      throw new InvalidOperationException($"Could not start the VPN host. Path: {executablePath}");

        // fail-closed: a VPN host the job does not hold would outlive a crashed service
        try {
            _job.Add(process);
        }
        catch {
            process.Kill();
            process.Dispose();
            throw;
        }

        VhLogger.Instance.LogInformation("The VPN host has started. ProcessId: {ProcessId}", process.Id);
        _process = process;
        _processExit = LogExit(process);
    }

    // A VPN host ends by itself with its session, exit code 0. Any other end is told here, as one that died before
    // its log opened tells nothing itself; the session's end is the app's (VpnServiceUnreachableException).
    private async Task LogExit(Process process)
    {
        await process.WaitForExitAsync(CancellationToken.None).Vhc();
        if (process.ExitCode == 0 || process.Id == Volatile.Read(ref _endedProcessId))
            VhLogger.Instance.LogDebug("The VPN host has exited. ProcessId: {ProcessId}, ExitCode: {ExitCode}",
                process.Id, process.ExitCode);
        else
            VhLogger.Instance.LogError(
                "The VPN host has ended without its session. ProcessId: {ProcessId}, ExitCode: 0x{ExitCode:X8}",
                process.Id, process.ExitCode);
    }

    // The quick close: the job's end ends the VPN host at once. Its graceful end is its session's, which the app's
    // disconnect asks for before this.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (_process is { } process)
            Volatile.Write(ref _endedProcessId, process.Id);

        _job.Dispose();
    }
}
