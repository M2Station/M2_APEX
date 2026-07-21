using System.Windows.Media.Imaging;

using Listly.Assets;

using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace Listly.Services;

/// <summary>Builds the tray/notification icon from the M2 logo at runtime (no binary asset needed).</summary>
public static class AppIcon
{
    public static Drawing.Icon CreateTrayIcon()
    {
        const int size = 32;
        return ToIcon(M2Logo.RenderBitmap(size, M2Logo.Foreground, M2Logo.BadgeBackground), size);
    }

    private static Drawing.Icon ToIcon(RenderTargetBitmap source, int size)
    {
        int stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        // WPF renders premultiplied alpha (Pbgra32); GDI+ expects straight alpha.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }

        using var bitmap = new Drawing.Bitmap(size, size, Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Drawing.Rectangle(0, 0, size, size),
            Imaging.ImageLockMode.WriteOnly,
            Imaging.PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);

        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
