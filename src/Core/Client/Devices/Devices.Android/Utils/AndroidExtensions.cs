using Android.Graphics;
using Android.Graphics.Drawables;
using VpnHood.Core.Toolkit.Graphics;

namespace VpnHood.Core.Client.Devices.Droid.Utils;

public static class AndroidExtensions
{
    public static Color ToAndroidColor(this VhColor color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }

    extension(Drawable drawable)
    {
        public string DrawableEncodeToBase64(int size)
        {
            using var bitmap = drawable.DrawableToBitmap(size);
            using var stream = new MemoryStream();
            var format = Bitmap.CompressFormat.Png ??
                         throw new InvalidOperationException("Could not acquire the PNG compress format.");

            // quality is ignored by the lossless png encoder
            if (!bitmap.Compress(format, 100, stream))
                throw new Exception("Could not compress bitmap to png.");

            var base64 = Convert.ToBase64String(stream.ToArray());

            // free the native pixels at once, as this may be called for hundreds of apps in a row
            bitmap.Recycle();
            return base64;
        }

        public Bitmap DrawableToBitmap(int size)
        {
            var config = Bitmap.Config.Argb8888 ??
                         throw new InvalidOperationException("Could not acquire the Argb8888 bitmap config.");

            // every drawable is rendered into a fixed size canvas, including BitmapDrawable which
            // carries the icon at its own density (up to 432x432) and would bloat the encoded size.
            // it also keeps the result an owned bitmap that the caller can recycle safely
            var bitmap = Bitmap.CreateBitmap(size, size, config);
            using var canvas = new Canvas(bitmap);
            drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
            drawable.Draw(canvas);

            return bitmap;
        }
    }
}