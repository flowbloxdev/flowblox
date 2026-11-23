using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowBlox.UICore.Views;

/// <summary>
/// Fits an image initially and supports pointer-anchored zoom, pixel scrolling and drag panning.
/// </summary>
public partial class ZoomableImageView : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(ZoomableImageView),
        new PropertyMetadata(null, SourceChanged));

    private const double MaximumScale = 4;
    private double _scale = 1;
    private bool _isFitted = true;
    private Point? _dragStart;
    private Point _dragOffsets;
    private MouseButton _dragButton;

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ZoomableImageView()
    {
        InitializeComponent();
        Loaded += ImageView_Loaded;
        Unloaded += ImageView_Unloaded;
    }

    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (ZoomableImageView)sender;
        view.StopPanning();
        view.ResetToFit();
    }

    private double FitScale => Source == null ? 1 : Math.Min(1, Math.Min(
        Math.Max(1, ImageScroller.ActualWidth - 2) / Source.Width,
        Math.Max(1, ImageScroller.ActualHeight - 2) / Source.Height));

    public void ResetToFit()
    {
        _isFitted = true;
        if (Source == null || ImageScroller.ActualWidth <= 0 || ImageScroller.ActualHeight <= 0)
            return;

        SetScale(FitScale);
        ImageScroller.ScrollToHome();
        ImageScroller.Cursor = Cursors.Arrow;
    }

    private void SetScale(double scale)
    {
        _scale = scale;
        Picture.Width = Source!.Width * scale;
        Picture.Height = Source.Height * scale;
    }

    private void Zoom(double factor, Point anchor)
    {
        if (Source == null || Picture.ActualWidth <= 0 || Picture.ActualHeight <= 0)
            return;

        var imagePoint = ImageScroller.TranslatePoint(anchor, Picture);
        var relativeX = Math.Clamp(imagePoint.X / Picture.ActualWidth, 0, 1);
        var relativeY = Math.Clamp(imagePoint.Y / Picture.ActualHeight, 0, 1);
        var scale = Math.Clamp(_scale * factor, FitScale, MaximumScale);
        _isFitted = scale <= FitScale;
        SetScale(scale);
        ImageScroller.UpdateLayout();

        var origin = Picture.TranslatePoint(new Point(), ImageScroller);
        ImageScroller.ScrollToHorizontalOffset(ImageScroller.HorizontalOffset + origin.X + relativeX * Picture.ActualWidth - anchor.X);
        ImageScroller.ScrollToVerticalOffset(ImageScroller.VerticalOffset + origin.Y + relativeY * Picture.ActualHeight - anchor.Y);
        ImageScroller.Cursor = _isFitted ? Cursors.Arrow : Cursors.Hand;
    }

    private void ImageScroller_MouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (Source == null || IsScrollBar(args.OriginalSource))
            return;

        Zoom(Math.Pow(1.2, args.Delta / 120d), args.GetPosition(ImageScroller));
        args.Handled = true;
    }

    private void ImageScroller_MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (Source == null || IsScrollBar(args.OriginalSource))
            return;

        ImageScroller.Focus();
        if (args.ChangedButton == MouseButton.Left && args.ClickCount == 2)
        {
            ResetToFit();
            args.Handled = true;
            return;
        }

        if ((args.ChangedButton != MouseButton.Left && args.ChangedButton != MouseButton.Middle) ||
            (ImageScroller.ScrollableWidth <= 0 && ImageScroller.ScrollableHeight <= 0))
            return;

        _dragStart = args.GetPosition(ImageScroller);
        _dragOffsets = new Point(ImageScroller.HorizontalOffset, ImageScroller.VerticalOffset);
        _dragButton = args.ChangedButton;
        ImageScroller.CaptureMouse();
        ImageScroller.Cursor = Cursors.SizeAll;
        args.Handled = true;
    }

    private void ImageScroller_MouseMove(object sender, MouseEventArgs args)
    {
        if (_dragStart == null || !ImageScroller.IsMouseCaptured)
            return;

        var displacement = args.GetPosition(ImageScroller) - _dragStart.Value;
        ImageScroller.ScrollToHorizontalOffset(_dragOffsets.X - displacement.X);
        ImageScroller.ScrollToVerticalOffset(_dragOffsets.Y - displacement.Y);
        args.Handled = true;
    }

    private void ImageScroller_MouseUp(object sender, MouseButtonEventArgs args)
    {
        if (_dragStart == null || args.ChangedButton != _dragButton)
            return;

        StopPanning();
        args.Handled = true;
    }

    private void StopPanning()
    {
        _dragStart = null;
        if (ImageScroller == null)
            return;
        if (ImageScroller.IsMouseCaptured)
            ImageScroller.ReleaseMouseCapture();
        ImageScroller.Cursor = _isFitted ? Cursors.Arrow : Cursors.Hand;
    }

    private static bool IsScrollBar(object source)
    {
        var element = source as DependencyObject;
        while (element is Visual)
        {
            if (element is ScrollBar)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void ImageScroller_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Home)
            ResetToFit();
        else if (args.Key is Key.Add or Key.OemPlus)
            Zoom(1.2, new Point(ImageScroller.ActualWidth / 2, ImageScroller.ActualHeight / 2));
        else if (args.Key is Key.Subtract or Key.OemMinus)
            Zoom(1 / 1.2, new Point(ImageScroller.ActualWidth / 2, ImageScroller.ActualHeight / 2));
        else
            return;
        args.Handled = true;
    }

    private void ImageView_Loaded(object sender, RoutedEventArgs args) => ResetToFit();
    private void ImageView_Unloaded(object sender, RoutedEventArgs args) => StopPanning();
    private void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_isFitted)
            ResetToFit();
    }
    private void ImageScroller_LostMouseCapture(object sender, MouseEventArgs args) => StopPanning();
}
