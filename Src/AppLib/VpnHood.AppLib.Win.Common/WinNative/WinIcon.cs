using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace VpnHood.AppLib.Win.Common.WinNative;

public static class WinIcon
{
    public const uint RES_ICON = 1;
    public const uint DEFAULT_ICON_SIZE = 0;
    public const uint IMAGE_ICON = 1; // Load an icon
    public const uint LR_LOADFROMFILE = 0x00000010; // Load from file path
    public const uint LR_DEFAULTSIZE = 0x00000040; // Use system default icon size

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(byte[] buffer, uint dwResSize, bool fIcon, uint dwVer,
        int cxDesired,
        int cyDesired, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge,
        IntPtr[] phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired,
        uint fuLoad);

    public static IntPtr LoadIconFromBytes(byte[] icoBytes)
    {
        // 1. Create a temp .ico file
        var tempIco = Path.GetTempFileName();

        File.Move(tempIco, tempIco);
        File.WriteAllBytes(tempIco, icoBytes);

        // 2. Load the icon using Win32
        var hIcon = LoadImage(
            IntPtr.Zero,
            tempIco,
            IMAGE_ICON,
            0,
            0,
            LR_LOADFROMFILE | LR_DEFAULTSIZE);

        File.Delete(tempIco);

        return hIcon != IntPtr.Zero ? hIcon : throw new InvalidOperationException("LoadImage failed.");
    }

    public static IntPtr ExtractLargeIcon(string exePath, int index = 0)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];

        var result = ExtractIconEx(exePath, index, large, small, 1);
        return result != 0 ? large[0] : throw new InvalidOperationException("No icon found in executable.");
    }

    public static IntPtr ExtractSmallIcon(string exePath, int index = 0)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];

        var result = ExtractIconEx(exePath, index, large, small, 1);
        return result != 0 ? small[0] : throw new InvalidOperationException("No icon found in executable.");
    }
}