using Android.Graphics;

namespace VpnHood.Core.Client.Device.Droid.Utils;

public static class AndroidExtensions
{
    public static Color ToAndroidColor(this System.Drawing.Color color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }
}