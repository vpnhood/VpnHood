using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VpnHood.AppLib.App.Windows.WinNative;

// A private namespace whose boundary names Administrators: SYSTEM and an elevated administrator may
// make it and enter it, a standard user can do neither, so a name in it cannot be taken first the way
// one in the Global namespace can. The caller names it: the boundary's name, the same namespace in
// every session whichever process made it, and this process's prefix for it, under which an object in
// it is named Name(...).
internal sealed class AdministratorsNamespace : IDisposable
{
    private const int ErrorAlreadyExists = 183;
    private const int ErrorPathNotFound = 3;
    private const int OpenAttempts = 3;

    private readonly string _name;
    private readonly IntPtr _boundary;
    private readonly IntPtr _handle;

    private AdministratorsNamespace(string name, IntPtr boundary, IntPtr handle)
    {
        _name = name;
        _boundary = boundary;
        _handle = handle;
    }

    public static AdministratorsNamespace Open(string name)
    {
        var boundary = CreateBoundary(name);
        try {
            return new AdministratorsNamespace(name, boundary, CreateOrOpen(boundary, name));
        }
        catch {
            DeleteBoundaryDescriptor(boundary);
            throw;
        }
    }

    public string Name(string objectName)
    {
        return $@"{_name}\{objectName}";
    }

    // Made by whoever comes first, entered by the rest; one that goes as it is entered is made again.
    private static IntPtr CreateOrOpen(IntPtr boundary, string alias)
    {
        for (var attempt = 0; ; attempt++) {
            var handle = CreatePrivateNamespace(IntPtr.Zero, boundary, alias);
            if (handle != IntPtr.Zero)
                return handle;

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorAlreadyExists)
                throw new Win32Exception(error, $"Could not make the administrators' namespace {alias}.");

            handle = OpenPrivateNamespace(boundary, alias);
            if (handle != IntPtr.Zero)
                return handle;

            error = Marshal.GetLastWin32Error();
            if (error != ErrorPathNotFound || attempt == OpenAttempts - 1)
                throw new Win32Exception(error, $"Could not open the administrators' namespace {alias}.");
        }
    }

    private static IntPtr CreateBoundary(string name)
    {
        var boundary = CreateBoundaryDescriptor(name, 0);
        if (boundary == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make the namespace's boundary.");

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sid = new byte[administrators.BinaryLength];
        administrators.GetBinaryForm(sid, 0);
        if (AddSIDToBoundaryDescriptor(ref boundary, sid))
            return boundary;

        var error = Marshal.GetLastWin32Error();
        DeleteBoundaryDescriptor(boundary);
        throw new Win32Exception(error, "Could not name Administrators in the namespace's boundary.");
    }

    public void Dispose()
    {
        ClosePrivateNamespace(_handle, 0);
        DeleteBoundaryDescriptor(_boundary);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateBoundaryDescriptor(string name, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AddSIDToBoundaryDescriptor(ref IntPtr boundaryDescriptor, byte[] requiredSid);

    [DllImport("kernel32.dll")]
    private static extern void DeleteBoundaryDescriptor(IntPtr boundaryDescriptor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreatePrivateNamespace(IntPtr privateNamespaceAttributes, IntPtr boundaryDescriptor,
        string aliasPrefix);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenPrivateNamespace(IntPtr boundaryDescriptor, string aliasPrefix);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ClosePrivateNamespace(IntPtr handle, uint flags);
}
