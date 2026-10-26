using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The machine's wakes, and whether it is going to sleep, as Windows tells a full-trust process: its suspend before a
// sleep or a hibernation, and its automatic resume after, which every wake raises, whether a person woke it or not.
internal sealed class WindowsVpnPluginSleepWatch : IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmSuspend = 4;
    private const uint PbtApmResumeAutomatic = 18;
    private readonly Lock _lock = new();
    // the callback and its parameters live as long as the registration: Windows calls back through them
    private readonly DeviceNotifyCallbackRoutine _callback;
    private readonly IntPtr _parameters;
    private readonly IntPtr _registration;
    private TaskCompletionSource _nextWakeTcs = NewWakeTcs();
    private bool _isAsleep; // a suspend since the last wake

    public WindowsVpnPluginSleepWatch()
    {
        _callback = OnPowerNotification;
        _parameters = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceNotifySubscribeParameters>());
        Marshal.StructureToPtr(new DeviceNotifySubscribeParameters {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = IntPtr.Zero
        }, _parameters, fDeleteOld: false);

        var error = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, _parameters, out _registration);
        if (error != 0) {
            Marshal.FreeHGlobal(_parameters);
            throw new Win32Exception((int)error, "Could not register for the machine's sleeps and wakes.");
        }
    }

    public Task NextWake {
        get {
            lock (_lock)
                return _nextWakeTcs.Task;
        }
    }

    // no suspend since the last wake
    public bool IsAwake {
        get {
            lock (_lock)
                return !_isAsleep;
        }
    }

    // on Windows' notification thread, as the machine goes to sleep: quick, and an exception here ends the process
    private uint OnPowerNotification(IntPtr context, uint type, IntPtr setting)
    {
        lock (_lock) {
            if (type == PbtApmSuspend) {
                _isAsleep = true;
            }
            else if (type == PbtApmResumeAutomatic) {
                _isAsleep = false;
                var wakeTcs = _nextWakeTcs;
                _nextWakeTcs = NewWakeTcs();
                wakeTcs.TrySetResult();
            }
        }

        return 0;
    }

    private static TaskCompletionSource NewWakeTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        PowerUnregisterSuspendResumeNotification(_registration);
        Marshal.FreeHGlobal(_parameters);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags, IntPtr recipient,
        out IntPtr registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr registrationHandle);
}
