using System;
using System.IO;
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
        Logo.Source = LoadLogo();
        Status.Text = UICore.Resources.SplashWindow.Tagline;
        Closing += (_, args) => args.Cancel = !_finished;
        Quit.Click += (_, _) => Complete();
        Closed += (_, _) => (Logo.Source as Bitmap)?.Dispose();
    }
    public void Complete() { _finished = true; Close(); }
    private static Bitmap LoadLogo()
    {
        var source = new BitmapImage(new Uri("pack://application:,,,/FlowBlox.UICore;component/Resources/Images/FlowBloxLogo.png"));
        // Remove the artwork's outer padding and retain its smooth PNG colors
        // instead of reducing them to a dithered GIF palette.
        var logo = new System.Windows.Media.Imaging.CroppedBitmap(
            source,
            new System.Windows.Int32Rect(160, 188, 950, 205));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(logo));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
    public void ShowError(Exception error)
    {
        Height = 360;
        Progress.IsVisible = false;
        Status.Text = string.Format(UICore.Resources.SplashWindow.Error, error.Message);
        Quit.IsVisible = true;
    }
}
