using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// The machine's sleeps and wakes, as Windows tells a process, a sandboxed one too: its suspend before a sleep or a
// hibernation, and its automatic resume after, which every wake raises, whether a person woke it or not. Both on
// Windows' notification thread, as the machine goes to sleep: quick, and an exception there ends the process.
internal sealed class WindowsVpnPluginSleepWatch : IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmSuspend = 4;
    private const uint PbtApmResumeAutomatic = 18;
    private readonly Action _onSuspend;
    private readonly Action _onWake;
    // the callback and its parameters live as long as the registration: Windows calls back through them
    private readonly DeviceNotifyCallbackRoutine _callback;
    private readonly IntPtr _parameters;
    private readonly IntPtr _registration;

    public WindowsVpnPluginSleepWatch(Action onSuspend, Action onWake)
    {
        _onSuspend = onSuspend;
        _onWake = onWake;
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

    private uint OnPowerNotification(IntPtr context, uint type, IntPtr setting)
    {
        if (type == PbtApmSuspend)
            _onSuspend();
        else if (type == PbtApmResumeAutomatic)
            _onWake();
        return 0;
    }

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
