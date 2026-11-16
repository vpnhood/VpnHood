using Android.Graphics;
using Android.Graphics.Drawables;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.Core.Client.Devices.Android.Utils;

/// <summary>
/// Provides conversions to Android's colors and images.
/// </summary>
public static class AndroidExtensions
{
    /// <summary>Converts a color to Android's.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color, with its alpha.</returns>
    public static Color ToAndroidColor(this VhColor color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }

    /// <summary>Provides a drawable's rendering into a square bitmap, and into a PNG.</summary>
    /// <param name="drawable">The drawable.</param>
    extension(Drawable drawable)
    {
        /// <summary>Renders the drawable into a square PNG of a size, as Base64.</summary>
        /// <param name="size">The side, in pixels.</param>
        /// <returns>The PNG, as Base64.</returns>
        /// <exception cref="InvalidOperationException">Android gave no PNG format or no ARGB 8888 bitmap config.</exception>
        /// <exception cref="Exception">The bitmap could not be compressed.</exception>
        internal string DrawableEncodeToBase64(int size)
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

        /// <summary>
        /// Renders the drawable into a new square bitmap of a size, which the caller owns and may recycle.
        /// </summary>
        /// <param name="size">The side, in pixels.</param>
        /// <returns>The bitmap.</returns>
        /// <exception cref="InvalidOperationException">Android gave no ARGB 8888 bitmap config.</exception>
        internal Bitmap DrawableToBitmap(int size)
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