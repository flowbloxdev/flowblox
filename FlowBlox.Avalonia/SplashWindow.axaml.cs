using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using System.Windows.Media.Imaging;

namespace FlowBlox.AvaloniaDesktop;

public partial class SplashWindow : Window
{
    private bool _finished;
    public SplashWindow()
    {
        InitializeComponent();
        Status.Text = UICore.Resources.SplashWindow.Tagline;
        Closing += (_, args) => args.Cancel = !_finished;
        Quit.Click += (_, _) => Complete();
        Closed += (_, _) => (Logo.Source as Bitmap)?.Dispose();
    }
    public void Complete() { _finished = true; Close(); }
    public async Task PlayIntroAsync()
    {
        var decoder = new GifBitmapDecoder(new Uri("pack://application:,,,/FlowBlox.UICore;component/Resources/Images/FlowBloxStartup.gif"), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        // The GIF palette dithers the flat background. Match its average edge color
        // after scaling, and keep the card opaque so desktop content cannot tint it.
        SplashSurface.Background = GetAnimationBackground(decoder.Frames[0]);
        foreach (var frame in decoder.Frames)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(frame);
            using var stream = new MemoryStream();
            png.Save(stream); stream.Position = 0;
            var previous = Logo.Source as Bitmap;
            Logo.Source = new Bitmap(stream);
            previous?.Dispose();
            var delay = frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is ushort value ? value * 10 : 50;
            await Task.Delay(Math.Max(10, delay));
        }
    }
    private static Avalonia.Media.IBrush GetAnimationBackground(BitmapSource frame)
    {
        var pixels = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var row = new byte[stride];
        pixels.CopyPixels(new System.Windows.Int32Rect(0, 0, pixels.PixelWidth, 1), row, stride, 0);
        long red = 0, green = 0, blue = 0;
        for (var x = 0; x < stride; x += 4)
        {
            blue += row[x]; green += row[x + 1]; red += row[x + 2];
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(
            (byte)(red / pixels.PixelWidth), (byte)(green / pixels.PixelWidth), (byte)(blue / pixels.PixelWidth)));
    }
    public void ShowError(Exception error)
    {
        Height = 360;
        Progress.IsVisible = false;
        Status.Text = string.Format(UICore.Resources.SplashWindow.Error, error.Message);
        Quit.IsVisible = true;
    }
}
