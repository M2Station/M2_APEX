using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfImage = System.Windows.Controls.Image;

namespace Listly.Views;

/// <summary>
/// A small centered, click-to-dismiss preview that plays a short animated GIF showing what
/// Ctrl+` (open M2_Commander) does. Opened from the search bar's clickable hint. The GIF is
/// embedded as a Resource and animated with a keyframe animation over its decoded frames, so
/// the app keeps its zero-dependency, single-file build.
/// </summary>
public partial class GifPreviewWindow : Window
{
    private bool _activatedOnce;

    public GifPreviewWindow(Uri gifUri)
    {
        InitializeComponent();

        Activated += (_, _) => _activatedOnce = true;
        Deactivated += (_, _) => { if (_activatedOnce) Close(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => PlayGif(gifUri);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnBackdropClick(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>
    /// Plays <paramref name="gifUri"/> by scheduling each decoded frame as a discrete keyframe on
    /// the Image's Source, honouring each frame's own delay. These GIFs use full-size opaque
    /// frames, so simple frame swapping renders correctly without compositing.
    /// </summary>
    private void PlayGif(Uri gifUri)
    {
        GifBitmapDecoder decoder;
        try
        {
            decoder = new GifBitmapDecoder(gifUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch
        {
            Close();
            return;
        }

        var frames = decoder.Frames;
        if (frames.Count == 0)
            return;

        if (frames.Count == 1)
        {
            GifImage.Source = frames[0];
            return;
        }

        var animation = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var time = TimeSpan.Zero;
        foreach (var frame in frames)
        {
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(frame, KeyTime.FromTimeSpan(time)));
            time += TimeSpan.FromMilliseconds(FrameDelayMs(frame));
        }

        animation.Duration = time;
        GifImage.BeginAnimation(WpfImage.SourceProperty, animation);
    }

    /// <summary>Per-frame delay in ms from GIF metadata (stored in 1/100s), clamped to a sane minimum.</summary>
    private static int FrameDelayMs(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata
                && metadata.GetQuery("/grctlext/Delay") is ushort delay && delay > 0)
                return Math.Max(20, delay * 10);
        }
        catch
        {
            // Missing / oddly-encoded delay: fall back to a sensible default.
        }

        return 100;
    }
}
