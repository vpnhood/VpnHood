using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VpnHood.Core.Client.Devices.WindowsTun.WinNative;

/// <summary>
/// Represents a private namespace whose boundary descriptor names the Administrators group, made with the default
/// security, meant to keep a standard user from taking a name in it first, as one can in the Global namespace.
/// </summary>
/// <remarks>
/// The caller names it: the boundary's name, the same namespace in every session whichever process made it, and this
/// process's prefix for it, under which an object in it is named by <see cref="Name"/>.
/// </remarks>
public sealed class AdministratorsNamespace : IDisposable
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

    /// <summary>Makes the namespace, or enters it where another process made it first.</summary>
    /// <param name="name">The namespace's name, which is both its boundary's and this process's prefix for it.</param>
    /// <returns>The namespace, open in this process.</returns>
    /// <exception cref="Win32Exception">Windows refused the boundary or the namespace, as for a standard user.</exception>
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

    /// <summary>Gets an object's name in the namespace.</summary>
    /// <param name="objectName">The object's own name.</param>
    /// <returns>The namespace's prefix, a backslash, and the object's name.</returns>
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

    /// <summary>Closes this process's handle on the namespace and deletes its boundary descriptor.</summary>
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
