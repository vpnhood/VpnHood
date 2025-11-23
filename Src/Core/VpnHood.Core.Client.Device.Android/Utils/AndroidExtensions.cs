using Android.Graphics;
using Android.Graphics.Drawables;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.Core.Client.Device.Droid.Utils;

public static class AndroidExtensions
{
    public static Color ToAndroidColor(this VhColor color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }

    extension(Drawable drawable)
    {
        public string DrawableEncodeToBase64(int quality)
        {
            var bitmap = DrawableToBitmap(drawable);
            var stream = new MemoryStream();
            if (!bitmap.Compress(Bitmap.CompressFormat.Png!, quality, stream))
                throw new Exception("Could not compress bitmap to png.");
            return Convert.ToBase64String(stream.ToArray());
        }

        public Bitmap DrawableToBitmap()
        {
            if (drawable is BitmapDrawable { Bitmap: not null } drawable1)
                return drawable1.Bitmap;

            //var bitmap = CreateBitmap(drawable.IntrinsicWidth, drawable.IntrinsicHeight, Config.Argb8888);
            var bitmap = Bitmap.CreateBitmap(32, 32, Bitmap.Config.Argb8888!);
            var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);

            return bitmap;
        }
    }
}