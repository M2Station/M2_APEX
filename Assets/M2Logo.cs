using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace Listly.Assets;

/// <summary>
/// The M2 wordmark logo (from the M2Station/M2_GIT_DIFF project), exposed as a WPF
/// <see cref="System.Windows.Media.Geometry"/>, a vector image and rendered bitmaps so it
/// can be reused for window icons, the tray icon and in-app branding — no binary asset needed.
/// </summary>
public static class M2Logo
{
    /// <summary>
    /// Even-odd (<c>F0</c>) path data on a 1024×1024 canvas: a ring enclosing the "M2" mark.
    /// Taken from the original <c>src/assets/logo.svg</c> and reused as WPF geometry
    /// (the SVG path mini-language is compatible with WPF's).
    /// </summary>
    public const string PathData =
        "F0 M474.500 5.091 C 369.720 13.291,269.833 53.534,190.500 119.511 C 164.616 141.037,141.773 163.914,121.904 188.211 C 46.367 280.576,6.980 391.590,7.025 512.000 C 7.071 633.721,52.941 752.327,135.677 844.650 C 179.034 893.031,229.632 931.814,286.500 960.256 C 367.991 1001.013,456.545 1018.990,546.170 1012.970 C 646.559 1006.228,740.144 970.848,821.000 909.073 C 903.470 846.063,968.149 753.678,997.474 657.000 C 1012.996 605.829,1018.990 565.277,1018.997 511.395 C 1019.000 481.311,1018.159 466.844,1014.934 441.500 C 995.771 290.911,906.460 155.598,773.500 75.706 C 711.963 38.730,644.372 15.731,571.500 6.970 C 557.228 5.255,546.562 4.739,520.000 4.479 C 501.575 4.299,481.100 4.575,474.500 5.091 M553.000 92.045 C 622.278 98.285,693.122 124.222,751.779 164.820 C 778.194 183.102,807.986 209.567,828.612 233.070 C 874.497 285.356,907.232 350.199,922.475 419.000 C 935.933 479.742,935.737 546.581,921.934 603.104 C 898.062 700.867,843.346 784.061,763.277 844.336 C 705.991 887.461,634.151 916.559,563.000 925.454 C 400.593 945.760,237.673 864.755,152.797 721.500 C 122.057 669.616,101.064 605.170,96.065 547.339 C 94.676 531.267,94.638 489.693,95.998 474.000 C 102.282 401.519,128.084 329.349,168.658 270.765 C 198.517 227.653,236.961 189.944,281.000 160.570 C 298.844 148.668,307.730 143.541,328.142 133.370 C 372.979 111.026,426.624 96.097,476.500 92.082 C 484.200 91.462,492.525 90.785,495.000 90.577 C 502.349 89.959,540.596 90.927,553.000 92.045 M254.667 238.667 C 254.300 239.033,254.000 358.508,254.000 504.167 L 254.000 769.000 297.497 769.000 L 340.995 769.000 341.247 577.250 L 341.500 385.500 425.500 469.203 C 471.700 515.239,509.801 553.039,510.170 553.203 C 510.538 553.366,569.157 495.475,640.435 424.555 L 770.030 295.611 769.765 267.055 L 769.500 238.500 741.000 238.379 L 712.500 238.258 612.000 338.218 L 511.500 438.178 411.967 338.089 L 312.434 238.000 283.884 238.000 C 268.181 238.000,255.033 238.300,254.667 238.667 M640.020 475.903 L 510.539 604.791 465.551 560.645 C 440.807 536.365,409.861 505.967,396.781 493.094 L 373.000 469.687 373.097 531.094 L 373.194 592.500 393.244 611.500 C 404.271 621.950,435.315 651.823,462.229 677.885 L 511.164 725.269 516.332 720.311 C 535.862 701.573,603.741 635.446,640.827 599.028 C 665.208 575.088,685.570 555.350,686.077 555.167 C 686.637 554.964,687.000 596.948,687.000 661.917 L 687.000 769.000 728.500 769.000 L 770.000 769.000 770.000 558.000 C 770.000 441.950,769.888 347.003,769.750 347.008 C 769.612 347.012,711.234 405.015,640.020 475.903 ";

    /// <summary>The brand cyan accent (#36D6FF) — used for UI accents, not the logo fill.</summary>
    public static Color Cyan => Color.FromRgb(0x36, 0xD6, 0xFF);

    /// <summary>Default logo fill — white, to sit on the dark M2 palette.</summary>
    public static Color Foreground => Colors.White;

    /// <summary>Dark badge background for icon rendering (matches the app panel colour).</summary>
    public static Color BadgeBackground => Color.FromRgb(0x12, 0x1A, 0x28);

    private static Geometry? _geometry;

    /// <summary>The frozen logo geometry (shared; clone before applying a transform).</summary>
    public static Geometry Geometry
    {
        get
        {
            if (_geometry is null)
            {
                _geometry = Geometry.Parse(PathData);
                _geometry.Freeze();
            }

            return _geometry;
        }
    }

    /// <summary>A vector image of the logo (white by default), suitable for an <c>Image.Source</c>.</summary>
    public static DrawingImage CreateImage() => CreateImage(Foreground);

    /// <summary>A vector image of the logo in the given colour.</summary>
    public static DrawingImage CreateImage(Color fill)
    {
        var drawing = new GeometryDrawing(new SolidColorBrush(fill), null, Geometry);
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Renders the logo into a square bitmap. When <paramref name="background"/> is set,
    /// the mark is drawn on a rounded badge of that colour (so an icon stays visible on any
    /// taskbar); otherwise the background is transparent.
    /// </summary>
    public static RenderTargetBitmap RenderBitmap(int size, Color? foreground = null, Color? background = null)
    {
        // Render large, then downscale with high-quality filtering, so the thin ring
        // strokes stay smooth even at tiny tray sizes (16-24 px).
        const int ss = 4;
        int hi = size * ss;

        var fill = foreground ?? Foreground;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double pad;
            if (background is Color badge)
            {
                double radius = hi * 0.23;

                // A subtle vertical gradient gives the flat badge some depth.
                var backdrop = new LinearGradientBrush(Lighten(badge, 0.14), Darken(badge, 0.12), 90);
                dc.DrawRoundedRectangle(backdrop, null, new Rect(0, 0, hi, hi), radius, radius);

                // A faint inner rim-light reads as a soft edge without a hard 1px border.
                double inset = Math.Max(1.0, hi / 64.0);
                var rim = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), inset);
                dc.DrawRoundedRectangle(null, rim,
                    new Rect(inset, inset, hi - 2 * inset, hi - 2 * inset),
                    radius - inset, radius - inset);

                pad = hi * 0.18;
            }
            else
            {
                pad = hi * 0.06;
            }

            var geometry = Geometry.Clone();
            var bounds = geometry.Bounds;
            double scale = Math.Min((hi - 2 * pad) / bounds.Width, (hi - 2 * pad) / bounds.Height);

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(scale, scale));
            transform.Children.Add(new TranslateTransform(
                (hi - bounds.Width * scale) / 2 - bounds.X * scale,
                (hi - bounds.Height * scale) / 2 - bounds.Y * scale));
            geometry.Transform = transform;

            dc.DrawGeometry(new SolidColorBrush(fill), null, geometry);
        }

        var supersampled = new RenderTargetBitmap(hi, hi, 96, 96, PixelFormats.Pbgra32);
        supersampled.Render(visual);

        var downVisual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(downVisual, BitmapScalingMode.HighQuality);
        using (var dc = downVisual.RenderOpen())
        {
            dc.DrawImage(supersampled, new Rect(0, 0, size, size));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(downVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color Lighten(Color c, double amount) => Blend(c, Colors.White, amount);

    private static Color Darken(Color c, double amount) => Blend(c, Colors.Black, amount);

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
