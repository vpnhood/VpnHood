using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The app's own icon, as its build made it from the head's ApplicationIcon: on Windows the icon the
// executable holds, every size of it; on Linux, whose executables hold none, its largest picture,
// which the build places beside the binary as AppIcon.png (VpnHood.AppUi.Hosting.Desktop.Linux's
// targets) and the installer gives the menu too. None where the build named no icon.
internal static class AvaloniaAppIcon
{
    private const string LinuxFileName = "AppIcon.png";
    private const uint LoadLibraryAsDatafile = 0x2;
    private const uint LoadLibraryAsImageResource = 0x20;
    private static readonly IntPtr RtIcon = 3;
    private static readonly IntPtr RtGroupIcon = 14;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Each picture of the icon, the smallest first.
    public static IReadOnlyList<Bitmap> Read()
    {
        if (OperatingSystem.IsWindows())
            return Environment.ProcessPath is { } exePath ? ReadExecutableIcon(exePath) : [];

        var file = Path.Combine(AppContext.BaseDirectory, LinuxFileName);
        return File.Exists(file) ? [new Bitmap(file)] : [];
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<Bitmap> ReadExecutableIcon(string exePath)
    {
        var module = LoadLibraryExW(exePath, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try {
            if (ReadFirstIconGroup(module) is not { } group)
                return [];

            // GRPICONDIR: reserved, type and count, then 14 bytes a picture - an icon file's entry, with
            // the picture's RT_ICON id where the file has its offset
            var count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
            var pictures = new List<Bitmap>(count);
            try {
                for (var i = 0; i < count; i++) {
                    var entry = group.AsSpan(6 + 14 * i, 14);
                    var id = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
                    pictures.Add(Decode(entry[..12], ReadResource(module, id, RtIcon)));
                }
            }
            catch {
                foreach (var picture in pictures)
                    picture.Dispose();
                throw;
            }

            return pictures.OrderBy(x => x.PixelSize.Width).ToArray();
        }
        finally {
            FreeLibrary(module);
        }
    }

    // The first icon group, the one Windows shows for the executable. Read inside the enumeration,
    // where a name that is a string is still valid, and an error carried out of the callback, which
    // must not throw into Windows.
    [SupportedOSPlatform("windows")]
    private static byte[]? ReadFirstIconGroup(IntPtr module)
    {
        byte[]? group = null;
        Exception? error = null;
        EnumResourceNamesW(module, RtGroupIcon, (_, _, name, _) => {
            try {
                group = ReadResource(module, name, RtGroupIcon);
            }
            catch (Exception ex) {
                error = ex;
            }

            return false;
        }, IntPtr.Zero);

        if (error != null)
            ExceptionDispatchInfo.Throw(error);

        return group;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ReadResource(IntPtr module, IntPtr name, IntPtr type)
    {
        var info = FindResourceW(module, name, type);
        var data = info == IntPtr.Zero ? IntPtr.Zero : LockResource(LoadResource(module, info));
        if (data == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var bytes = new byte[SizeofResource(module, info)];
        Marshal.Copy(data, bytes, 0, bytes.Length);
        return bytes;
    }

    // A PNG picture reads as it is. A bitmap one is an icon file's picture without the file around it,
    // so it gets the file's header and its one entry: the group's 12 bytes, then the picture's offset.
    private static Bitmap Decode(ReadOnlySpan<byte> entry, byte[] picture)
    {
        if (picture.AsSpan().StartsWith(PngSignature))
            return new Bitmap(new MemoryStream(picture));

        var file = new byte[22 + picture.Length];
        file[2] = 1; // an icon
        file[4] = 1; // of one picture
        entry.CopyTo(file.AsSpan(6));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 22);
        picture.CopyTo(file, 22);
        return new Bitmap(new MemoryStream(file));
    }

    private delegate bool EnumResourceNameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResourceNameCallback callback,
        IntPtr param);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

    [DllImport("kernel32")]
    private static extern IntPtr LockResource(IntPtr resourceData);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);
}
