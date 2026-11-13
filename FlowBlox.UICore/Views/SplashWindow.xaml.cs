using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FlowBlox.UICore.Views;

/// <summary>
/// Displays the logo while running the startup hook, then holds it for 500 ms.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly TimeSpan KeepTime = TimeSpan.FromMilliseconds(500);
    private readonly Action _initialize;
    private bool _started;
    private bool _finished;

    public Exception? InitializationError { get; private set; }

    public SplashWindow(Action initialize)
    {
        _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
        InitializeComponent();
        var source = new BitmapImage(new Uri("pack://application:,,,/FlowBlox.UICore;component/Resources/Images/FlowBloxLogo.png"));
        var logo = new CroppedBitmap(source, new Int32Rect(160, 188, 950, 205));
        logo.Freeze();
        Logo.Source = logo;
        ContentRendered += StartInitialization;
    }

    private async void StartInitialization(object? sender, EventArgs e)
    {
        if (_started) return;
        _started = true;
        try
        {
            await Task.Run(_initialize);
            await Task.Delay(KeepTime);
        }
        catch (Exception ex)
        {
            InitializationError = ex;
        }
        finally
        {
            _finished = true;
            DialogResult = InitializationError == null;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !_finished;
}
