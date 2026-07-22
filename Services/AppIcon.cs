using System.IO;

using Listly.Assets;

using Drawing = System.Drawing;

namespace Listly.Services;

/// <summary>
/// Builds a crisp, multi-resolution tray icon from the M2 logo at runtime (no binary asset needed).
/// Windows selects the best-fitting frame for the current notification-area DPI, so the mark stays sharp.
/// </summary>
public static class AppIcon
{
    // The frame sizes Windows chooses between for the tray/taskbar across DPI scales.
    private static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64 };

    public static Drawing.Icon CreateTrayIcon()
    {
        using var stream = BuildIco(IconSizes);
        return new Drawing.Icon(stream);
    }

    // Broader range for the executable / shortcut icon (Explorer uses up to 256 px at high DPI).
    private static readonly int[] FileIconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    /// <summary>Writes the app's multi-resolution icon to <paramref name="path"/> as a .ico file.</summary>
    public static void SaveIconFile(string path)
    {
        using var stream = BuildIco(FileIconSizes);
        using var file = File.Create(path);
        stream.CopyTo(file);
    }

    /// <summary>Assembles an in-memory <c>.ico</c> containing one 32bpp DIB frame per size.</summary>
    private static MemoryStream BuildIco(int[] sizes)
    {
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            frames[i] = BuildDibFrame(sizes[i]);

        var output = new MemoryStream();
        var writer = new BinaryWriter(output);

        // ICONDIR header.
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: 1 = icon
        writer.Write((ushort)sizes.Length);

        int offset = 6 + 16 * sizes.Length; // header + one directory entry per frame
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height (0 => 256)
            writer.Write((byte)0);    // palette colours
            writer.Write((byte)0);    // reserved
            writer.Write((ushort)1);  // colour planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames)
            writer.Write(frame);

        writer.Flush();
        output.Position = 0;
        return output;
    }

    /// <summary>
    /// Renders one frame and packs it as a bottom-up 32bpp DIB (BITMAPINFOHEADER + BGRA colour
    /// data + an empty AND mask); the alpha channel carries transparency.
    /// </summary>
    private static byte[] BuildDibFrame(int size)
    {
        var source = M2Logo.RenderBitmap(size, M2Logo.Foreground, M2Logo.BadgeBackground);
        int stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        // WPF renders premultiplied alpha (Pbgra32); ICO DIBs expect straight alpha.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i]     = (byte)Math.Min(255, pixels[i]     * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }

        int maskStride = ((size + 31) / 32) * 4; // 1bpp AND mask, rows padded to 4 bytes
        var frame = new MemoryStream();
        var writer = new BinaryWriter(frame);

        // BITMAPINFOHEADER - height is doubled to span the colour data and the AND mask.
        writer.Write(40);            // biSize
        writer.Write(size);          // biWidth
        writer.Write(size * 2);      // biHeight
        writer.Write((ushort)1);     // biPlanes
        writer.Write((ushort)32);    // biBitCount
        writer.Write(0);             // biCompression = BI_RGB
        writer.Write(stride * size); // biSizeImage
        writer.Write(0);             // biXPelsPerMeter
        writer.Write(0);             // biYPelsPerMeter
        writer.Write(0);             // biClrUsed
        writer.Write(0);             // biClrImportant

        // Colour data, bottom-up.
        for (int y = size - 1; y >= 0; y--)
            writer.Write(pixels, y * stride, stride);

        // AND mask - all zero: every pixel opaque, the alpha channel handles transparency.
        writer.Write(new byte[maskStride * size]);

        writer.Flush();
        return frame.ToArray();
    }
}
