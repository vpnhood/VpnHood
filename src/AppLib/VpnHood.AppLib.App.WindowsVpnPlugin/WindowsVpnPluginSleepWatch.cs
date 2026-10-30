using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VpnHood.AppLib.App.WindowsVpnPlugin;

// The machine's sleeps and wakes, and its screen, as Windows tells a process, a sandboxed one too: its suspend before a
// sleep or a hibernation, its automatic resume after, which every wake raises, whether a person woke it or not, and the
// console's display turning on or off - off through a wake no person made, a timer's, say. All on Windows' notification
// thread, as the machine goes to sleep: quick, and an exception there ends the process.
internal sealed class WindowsVpnPluginSleepWatch : IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmSuspend = 4;
    private const uint PbtApmResumeAutomatic = 18;
    private const uint PbtPowerSettingChange = 0x8013;
    private const int DisplayOff = 0; // 1 on, 2 dimmed: a person is still about
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private readonly Action _onSuspend;
    private readonly Action _onWake;
    private readonly Action<bool> _onDisplayChanged;
    // the callback and its parameters live as long as the registrations: Windows calls back through them
    private readonly DeviceNotifyCallbackRoutine _callback;
    private readonly IntPtr _parameters;
    private readonly IntPtr _registration;
    private readonly IntPtr _displayRegistration;

    // onDisplayChanged: whether the screen is on; Windows tells it once at the registration too
    public WindowsVpnPluginSleepWatch(Action onSuspend, Action onWake, Action<bool> onDisplayChanged)
    {
        _onSuspend = onSuspend;
        _onWake = onWake;
        _onDisplayChanged = onDisplayChanged;
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

        error = PowerSettingRegisterNotification(ConsoleDisplayState, DeviceNotifyCallback, _parameters,
            out _displayRegistration);
        if (error != 0) {
            PowerUnregisterSuspendResumeNotification(_registration);
            Marshal.FreeHGlobal(_parameters);
            throw new Win32Exception((int)error, "Could not register for the machine's screen turning on or off.");
        }
    }

    private uint OnPowerNotification(IntPtr context, uint type, IntPtr setting)
    {
        if (type == PbtApmSuspend)
            _onSuspend();
        else if (type == PbtApmResumeAutomatic)
            _onWake();
        else if (type == PbtPowerSettingChange)
            OnPowerSettingChange(setting);
        return 0;
    }

    // a POWERBROADCAST_SETTING: the setting's GUID, the length of its data, then the data - the display's, a DWORD
    private void OnPowerSettingChange(IntPtr setting)
    {
        if (setting == IntPtr.Zero ||
            Marshal.PtrToStructure<Guid>(setting) != ConsoleDisplayState ||
            Marshal.ReadInt32(setting, 16) < sizeof(int))
            return;

        _onDisplayChanged(Marshal.ReadInt32(setting, 20) != DisplayOff);
    }

    public void Dispose()
    {
        PowerSettingUnregisterNotification(_displayRegistration);
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

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(in Guid settingGuid, uint flags, IntPtr recipient,
        out IntPtr registrationHandle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingUnregisterNotification(IntPtr registrationHandle);
}
