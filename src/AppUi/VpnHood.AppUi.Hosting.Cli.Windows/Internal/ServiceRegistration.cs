using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.ServiceProcess;

// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
namespace VpnHood.AppUi.Hosting.Cli.Windows.Internal;

// The service's registration, through the service control manager's own API rather than sc.exe: no
// console window flashes when the window registers the service, and a failure arrives as the error
// it is. Needs an administrator; the callers ask for one first.
internal static class ServiceRegistration
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceAllAccess = 0xF01FF;
    private const uint Delete = 0x10000;
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceAutoStart = 0x2;
    private const uint ServiceErrorNormal = 0x1;
    private const int ServiceConfigDescription = 1;
    private const int ServiceConfigFailureActions = 2;
    private const int ServiceConfigFailureActionsFlag = 4;
    private const int ScActionRestart = 1;
    private const int ErrorServiceExists = 1073;
    private const int DaclSecurityInformation = 4;

    // SYSTEM and Administrators as the default service DACL has them; interactive users may query it
    // and START it - only start (hosting plan §7.1-6), so a person's window can bring a stopped
    // service back without a prompt, and no one but an administrator can stop it.
    private const string ServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    // Restarted after a crash, or after a start that failed, a few times a day at most.
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailureResetPeriod = TimeSpan.FromDays(1);

    public static bool IsRegistered(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        try {
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    // Automatic, LocalSystem, restarted on failure, startable by interactive users. On a service
    // already registered, the same settings are written over whatever it has.
    public static void Register(string serviceName, string displayName, string description, string commandLine)
    {
        var scm = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
        if (scm == IntPtr.Zero)
            throw new Win32Exception();

        try {
            var service = CreateService(scm, serviceName, displayName, ServiceAllAccess, ServiceWin32OwnProcess,
                ServiceAutoStart, ServiceErrorNormal, commandLine, null, IntPtr.Zero, null, null, null);

            var wasRegistered = service == IntPtr.Zero;
            if (wasRegistered) {
                if (Marshal.GetLastWin32Error() != ErrorServiceExists)
                    throw new Win32Exception();

                service = OpenService(scm, serviceName, ServiceAllAccess);
                if (service == IntPtr.Zero)
                    throw new Win32Exception();
            }

            try {
                if (wasRegistered && !ChangeServiceConfig(service, ServiceWin32OwnProcess, ServiceAutoStart,
                        ServiceErrorNormal, commandLine, null, IntPtr.Zero, null, "LocalSystem", null, displayName))
                    throw new Win32Exception();

                SetDescription(service, description);
                SetFailureActions(service);
                SetStartRights(service);
            }
            finally {
                CloseServiceHandle(service);
            }
        }
        finally {
            CloseServiceHandle(scm);
        }
    }

    public static void Unregister(string serviceName)
    {
        var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero)
            throw new Win32Exception();

        try {
            var service = OpenService(scm, serviceName, Delete);
            if (service == IntPtr.Zero)
                throw new Win32Exception();

            try {
                if (!DeleteService(service))
                    throw new Win32Exception();
            }
            finally {
                CloseServiceHandle(service);
            }
        }
        finally {
            CloseServiceHandle(scm);
        }
    }

    private static void SetDescription(IntPtr service, string description)
    {
        var info = new ServiceDescription { lpDescription = description };
        if (!ChangeServiceConfig2(service, ServiceConfigDescription, ref info))
            throw new Win32Exception();
    }

    private static void SetFailureActions(IntPtr service)
    {
        var actions = new ScAction[3];
        for (var i = 0; i < actions.Length; i++)
            actions[i] = new ScAction { Type = ScActionRestart, Delay = (int)RestartDelay.TotalMilliseconds };

        var actionsHandle = GCHandle.Alloc(actions, GCHandleType.Pinned);
        try {
            var info = new ServiceFailureActions {
                dwResetPeriod = (int)FailureResetPeriod.TotalSeconds,
                cActions = actions.Length,
                lpsaActions = actionsHandle.AddrOfPinnedObject()
            };
            if (!ChangeServiceConfig2(service, ServiceConfigFailureActions, ref info))
                throw new Win32Exception();
        }
        finally {
            actionsHandle.Free();
        }

        // a start that failed - the service ended with an error of its own - counts as much as a crash
        var flag = new ServiceFailureActionsFlag { fFailureActionsOnNonCrashFailures = true };
        if (!ChangeServiceConfig2(service, ServiceConfigFailureActionsFlag, ref flag))
            throw new Win32Exception();
    }

    private static void SetStartRights(IntPtr service)
    {
        var securityDescriptor = new RawSecurityDescriptor(ServiceSddl);
        var binaryForm = new byte[securityDescriptor.BinaryLength];
        securityDescriptor.GetBinaryForm(binaryForm, 0);
        if (!SetServiceObjectSecurity(service, DaclSecurityInformation, binaryForm))
            throw new Win32Exception();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        public string lpDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceFailureActions
    {
        public int dwResetPeriod;
        public string? lpRebootMsg;
        public string? lpCommand;
        public int cActions;
        public IntPtr lpsaActions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction
    {
        public int Type;
        public int Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceFailureActionsFlag
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fFailureActionsOnNonCrashFailures;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(IntPtr scManager, string serviceName, string displayName,
        uint desiredAccess, uint serviceType, uint startType, uint errorControl, string binaryPathName,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType,
        uint errorControl, string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies,
        string? serviceStartName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref ServiceDescription info);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref ServiceFailureActions info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref ServiceFailureActionsFlag info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceObjectSecurity(IntPtr service, int securityInformation, byte[] securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
