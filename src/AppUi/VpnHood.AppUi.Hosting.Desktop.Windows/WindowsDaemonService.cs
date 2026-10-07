using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Windows;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// The daemon's run as the service control manager hosts it. The manager stops a service with a call,
// not a signal, so its Stop - and a shutdown - cancels the run here, and waits while the run
// disconnects. A run that ends by itself (a start that failed, say) stops the service with
// it. Started by hand from a console instead, the run is simply run, and Ctrl+C cancels it.
internal sealed class WindowsDaemonService : ServiceBase
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
    private readonly Func<CancellationToken, Task<int>> _run;
    private readonly CancellationTokenSource _cancellation = new();
    private Task<int>? _running;

    private WindowsDaemonService(string serviceName, Func<CancellationToken, Task<int>> run)
    {
        ServiceName = serviceName;
        CanStop = true;
        CanShutdown = true;

        // No "Service started/stopped successfully" in the Application log: the service control
        // manager records each start and stop in the System log, and here they were nine entries in
        // ten, pushing the warnings out of what "service log" shows of this log.
        AutoLog = false;
        _run = run;
    }

    // IDesktopPlatform.HostDaemon
    public static Task<int> Host(string serviceName, Func<CancellationToken, Task<int>> run,
        CancellationToken cancellationToken)
    {
        if (!IsStartedByServiceManager())
            return run(cancellationToken);

        // The service's warnings and errors go to the Application log under its name, from before
        // its run to after it. Run blocks until the manager has stopped the service; this is the
        // daemon's own thread.
        VhLogger.AddProvider(new WinEventLogLoggerProvider(serviceName));
        using var service = new WindowsDaemonService(serviceName, run);
        Run(service);
        return Task.FromResult(service.ExitCode);
    }

    protected override void OnStart(string[] args)
    {
        _running = Task.Run(() => _run(_cancellation.Token));
        _running.ContinueWith(_ => {
            // ended by itself rather than by OnStop: the service goes with it
            if (!_cancellation.IsCancellationRequested)
                Stop();
        }, TaskScheduler.Default);
    }

    protected override void OnStop()
    {
        EndRun();
    }

    protected override void OnShutdown()
    {
        EndRun();
    }

    private void EndRun()
    {
        RequestAdditionalTime((int)StopTimeout.TotalMilliseconds);
        _cancellation.Cancel();

        try {
            var running = _running ?? Task.FromResult(0);
            ExitCode = running.Wait(StopTimeout) ? running.Result : 1;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The service's run ended with an error.");
            ExitCode = 1;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cancellation.Dispose();

        base.Dispose(disposing);
    }

    // As .NET's own WindowsServiceHelpers tells: the service control manager is services.exe, the
    // parent of every service process it starts, in session 0.
    private static bool IsStartedByServiceManager()
    {
        try {
            using var parent = Process.GetProcessById(GetParentProcessId());
            return parent.SessionId == 0 &&
                   string.Equals(parent.ProcessName, "services", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) {
            return false; // the parent is gone
        }
    }

    private static int GetParentProcessId()
    {
        var info = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref info,
            Marshal.SizeOf<ProcessBasicInformation>(), out _);
        return status == 0 ? (int)info.InheritedFromUniqueProcessId : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);
}
