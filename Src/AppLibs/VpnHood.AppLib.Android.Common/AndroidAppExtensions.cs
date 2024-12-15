using Android.Graphics;
using Android.Graphics.Drawables;

namespace VpnHood.AppLib.Droid.Common;

public static class AndroidAppExtensions
{
    public static Icon ToAndroidIcon(this AppResource.ImageData imageData)
    {
        return Icon.CreateWithData(imageData.Data, 0, imageData.Data.Length);
    }

    public static Color ToAndroidColor(this System.Drawing.Color color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }
}