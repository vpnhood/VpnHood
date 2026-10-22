using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.WindowsTun;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// The daemon's run as the service control manager hosts it. The manager stops a service with a call,
// not a signal, so its Stop - and a shutdown - cancels the run here, and waits while the run closes
// the app: a stop's disconnects first, a shutdown's at once. With Fast Startup on, Windows' default,
// "Shut down" hibernates the service instead of stopping it: no shutdown reaches it, and it wakes at
// the next start as from a sleep. A run that ends by itself (a start that failed, say) stops the
// service with it. Started by hand from a console instead, the run is run as a signal stops it, and
// Ctrl+C cancels it.
internal sealed class WindowsDaemonService : ServiceBase
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    // The app's graceful close in a stop, which leaves its quick one the rest of StopTimeout.
    private static readonly TimeSpan GracefulCloseTimeout = StopTimeout - TimeSpan.FromSeconds(5);

    // A little over StopTimeout, so a stop that takes all of it is not called hung (services.msc,
    // net stop) a moment before it ends.
    private static readonly TimeSpan StopWaitHint = StopTimeout + TimeSpan.FromSeconds(5);
    private readonly DaemonRun _run;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationTokenSource _quickClose = new();
    private Task<int>? _running;

    private WindowsDaemonService(string serviceName, DaemonRun run)
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
    public static Task<int> Host(string serviceName, DaemonRun run, CancellationToken cancellationToken)
    {
        if (!IsStartedByServiceManager())
            return DesktopHost.HostDaemonStoppedBySignal(run, cancellationToken);

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
        _running = Task.Run(() => _run(_cancellation.Token, _quickClose.Token));
        _running.ContinueWith(_ => {
            // ended by itself rather than by OnStop: the service goes with it
            if (!_cancellation.IsCancellationRequested)
                Stop();
        }, TaskScheduler.Default);
    }

    // A stop is pending while the run disconnects, so it may ask for the time that takes. A shutdown is
    // not: there RequestAdditionalTime would throw (NotInPendingState) and crash the process before the
    // run disconnects. So a shutdown closes the app at once, with no disconnect, in what Windows gives
    // every service then, WaitToKillServiceTimeout (5 s by default), well short of StopTimeout.
    protected override void OnStop()
    {
        RequestAdditionalTime((int)StopWaitHint.TotalMilliseconds);
        _quickClose.CancelAfter(GracefulCloseTimeout);
        EndRun();
    }

    protected override void OnShutdown()
    {
        Cancel(_quickClose);
        EndRun();
    }

    // A run that ended by itself keeps its code, so the manager restarts a failed one
    // (ServiceRegistration). A stop that was asked for reports success even when the run fails or
    // outlasts it, or the manager would start the service again after an installer stopped it.
    private void EndRun()
    {
        var endedByItself = _running is { IsCompleted: true };
        Cancel(_cancellation);
        var exitCode = WaitRun();
        ExitCode = endedByItself ? exitCode : 0;
    }

    // A callback on the run's tokens that throws is logged: out of OnStop or OnShutdown it would kill
    // the process before the stop is reported. Every callback has still run, so the run ends anyway.
    private static void Cancel(CancellationTokenSource cancellation)
    {
        try {
            cancellation.Cancel();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Cancelling the service's run threw.");
        }
    }

    // The run's code once it ends; 1 for a run that failed or outlasted the stop.
    private int WaitRun()
    {
        try {
            var running = _running ?? Task.FromResult(0);
            if (running.Wait(StopTimeout))
                return running.Result;

            VhLogger.Instance.LogError("The service's run did not end within {StopTimeout} of the stop.", StopTimeout);
            return 1;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The service's run ended with an error.");
            return 1;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _cancellation.Dispose();
            _quickClose.Dispose();
        }

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
