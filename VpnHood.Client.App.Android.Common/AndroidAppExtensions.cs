namespace VpnHood.Client.App.Droid.Common;

public static class AndroidAppExtensions
{
    public static Android.Graphics.Drawables.Icon ToAndroidIcon(this AppResources.ImageData imageData)
    {
        return Android.Graphics.Drawables.Icon.CreateWithData(imageData.Data, 0, imageData.Data.Length); 
    }

    public static Android.Graphics.Color ToAndroidColor(this System.Drawing.Color color)
    {
        return new Android.Graphics.Color(color.R, color.G, color.B, color.A);
    }
}