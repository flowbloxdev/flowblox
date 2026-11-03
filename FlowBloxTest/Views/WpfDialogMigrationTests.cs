using System.Globalization;
using System.Reflection;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBloxTest.Views;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class WpfDialogMigrationTests
{
    [STATestMethod]
    public void Edit_CancelDoesNotChangeAnyProjectMetadata()
    {
        var project = new FlowBloxProject { ProjectName = "Original", Author = "Author", ProjectDescription = "Description", AssistantMetadata = new AssistantMetadata { ShortDescription = "Short" }, Notice = "Notice" };
        bool? result = null;
        var vm = new CreateEditProjectViewModel(project, true, value => result = value)
        {
            ProjectName = "Changed", Author = "New", Description = "Changed", ShortDescription = "Changed", Notice = "Changed"
        };
        vm.CancelCommand.Execute(null);
        Assert.AreEqual(false, result);
        Assert.AreEqual("Original", project.ProjectName);
        Assert.AreEqual("Author", project.Author);
        Assert.AreEqual("Description", project.ProjectDescription);
        Assert.AreEqual("Short", project.AssistantMetadata.ShortDescription);
        Assert.AreEqual("Notice", project.Notice);
    }

    [STATestMethod]
    public void Apply_RejectsBlankNamesAndCommitsAllMetadataOnlyOnSuccess()
    {
        var project = new FlowBloxProject { ProjectName = "Original" };
        bool? result = null;
        var vm = new CreateEditProjectViewModel(project, false, value => result = value) { ProjectName = "   " };
        vm.ApplyCommand.Execute(null);
        Assert.IsNull(result);
        Assert.AreEqual("Original", project.ProjectName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ErrorText));
        vm.ProjectName = "New project"; vm.Author = "Author"; vm.Description = "Description";
        vm.ShortDescription = "Short"; vm.Notice = "Notice";
        vm.ApplyCommand.Execute(null);
        Assert.AreEqual(true, result);
        Assert.AreEqual("New project", project.ProjectName);
        Assert.AreEqual("Author", project.Author);
        Assert.AreEqual("Description", project.ProjectDescription);
        Assert.AreEqual("Short", project.AssistantMetadata.ShortDescription);
        Assert.AreEqual("Notice", project.Notice);
    }

    [STATestMethod]
    public void Directories_FollowDraftNameAndOptions_AndSupportCreationAndErrors()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        var input = options.GetOption("Paths.InputDir"); var output = options.GetOption("Paths.OutputDir");
        var oldInput = input.PersistentValue; var oldOutput = output.PersistentValue;
        var root = Path.Combine(Path.GetTempPath(), "FlowBlox-DialogTests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            input.Value = Path.Combine(root, "in"); output.Value = Path.Combine(root, "out");
            var project = new FlowBloxProject { ProjectName = "Original" };
            var vm = new CreateEditProjectViewModel(project, true, _ => { }) { ProjectName = "Draft:Name" };
            Assert.AreEqual(project.GetProjectInputDirectory("Draft:Name"), vm.InputDirectory);
            Assert.AreEqual(project.GetProjectOutputDirectory("Draft:Name"), vm.OutputDirectory);
            Assert.IsFalse(Path.GetFileName(vm.InputDirectory).Contains(':'));
            Assert.IsTrue(vm.CreateInputDirectoryCommand.CanExecute(null));
            Assert.IsFalse(vm.OpenInputDirectoryCommand.CanExecute(null));
            vm.CreateInputDirectoryCommand.Execute(null); vm.CreateOutputDirectoryCommand.Execute(null);
            Assert.IsTrue(Directory.Exists(vm.InputDirectory)); Assert.IsTrue(Directory.Exists(vm.OutputDirectory));
            Assert.IsFalse(vm.CreateInputDirectoryCommand.CanExecute(null));
            Assert.IsTrue(vm.OpenInputDirectoryCommand.CanExecute(null));
            Assert.AreEqual("Original", project.ProjectName);
            vm.ProjectName = "Another";
            Assert.IsTrue(vm.CreateInputDirectoryCommand.CanExecute(null));
            input.Value = string.Empty;
            Assert.AreEqual(string.Empty, vm.InputDirectory);
            Assert.IsFalse(vm.CreateInputDirectoryCommand.CanExecute(null));
            var file = Path.Combine(root, "file"); File.WriteAllText(file, "blocking file");
            input.Value = file;
            vm.CreateInputDirectoryCommand.Execute(null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ErrorText));
        }
        finally
        {
            input.PersistentValue = oldInput; output.PersistentValue = oldOutput;
            Directory.Delete(root, true);
        }
    }

    [STATestMethod]
    public void ProjectEditors_MarkOnlySoftWraps_WithoutChangingTextOrFieldSize()
    {
        var project = new FlowBloxProject { ProjectName = "Test", ProjectDescription = "First line\r\nSecond line" };
        var window = new CreateEditProjectWindow(project, true) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            var editor = (ICSharpCode.AvalonEdit.TextEditor)window.FindName("DescriptionInput");
            var view = editor.TextArea.TextView;
            System.Windows.Media.DrawingGroup? DrawMarkerLayer()
            {
                window.UpdateLayout();
                view.EnsureVisualLines();
                var visual = new System.Windows.Media.DrawingVisual();
                using (var drawing = visual.RenderOpen()) view.BackgroundRenderers.Last().Draw(view, drawing);
                return System.Windows.Media.VisualTreeHelper.GetDrawing(visual);
            }

            Assert.IsTrue(DrawMarkerLayer()?.Bounds.IsEmpty != false, "Enter breaks must remain unmarked.");
            string text = string.Join(" ", Enumerable.Repeat("A long description with automatic wrapping", 80)) + "\r\nLast paragraph";
            editor.Document.Text = text;
            Assert.IsTrue(DrawMarkerLayer()?.Bounds.IsEmpty == false, "Automatic continuations must show arrows.");
            Assert.IsTrue(view.VisualLines.Any(line => line.TextLines.Count > 1));
            Assert.AreEqual(2, editor.Document.LineCount);
            Assert.AreEqual(text, ((CreateEditProjectViewModel)window.DataContext).Description);
            Assert.AreEqual("First line\r\nSecond line", project.ProjectDescription);
            Assert.AreEqual(90, editor.ActualHeight);
            Assert.IsFalse(editor.ShowLineNumbers);
            editor.ScrollToEnd();
            window.UpdateLayout();
            Assert.IsTrue(editor.VerticalOffset > 0);
            Assert.AreEqual(0, editor.HorizontalOffset);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow("DescriptionInput")]
    [DataRow("NoticeInput")]
    public void ProjectEditors_RenderSoftWrapArrowsInsideTheWrapIndentation(string editorName)
    {
        var window = new CreateEditProjectWindow(new FlowBloxProject { ProjectName = "Test" }, true)
        { Left = -10000, Top = -10000, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            var editor = (ICSharpCode.AvalonEdit.TextEditor)window.FindName(editorName);
            editor.Text = string.Join(" ", Enumerable.Repeat("A long project description with automatic wrapping", 8));
            var view = editor.TextArea.TextView;
            var renderer = view.BackgroundRenderers.Single(item => item.GetType().Name == "SoftWrapContinuationRenderer");

            byte[] RenderView()
            {
                window.UpdateLayout();
                view.EnsureVisualLines();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                // Render through a VisualBrush to exclude TextView's layout offset
                // within the dialog from this editor-local pixel comparison.
                var surface = new System.Windows.Media.DrawingVisual();
                using (var context = surface.RenderOpen())
                {
                    var bounds = new System.Windows.Rect(0, 0, view.ActualWidth, view.ActualHeight);
                    context.DrawRectangle(System.Windows.Media.Brushes.White, null, bounds);
                    context.DrawRectangle(new System.Windows.Media.VisualBrush(view)
                    {
                        ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,
                        Viewbox = bounds,
                        Stretch = System.Windows.Media.Stretch.None,
                        AlignmentX = System.Windows.Media.AlignmentX.Left,
                        AlignmentY = System.Windows.Media.AlignmentY.Top
                    }, null, bounds);
                }
                bitmap.Render(surface);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return pixels;
            }

            var withMarker = RenderView();
            var visualLine = view.VisualLines.First();
            Assert.IsTrue(visualLine.TextLines.Count > 1);
            var continuation = visualLine.TextLines[1];
            var indent = visualLine.GetTextLineVisualXPosition(continuation,
                visualLine.GetTextLineVisualStartColumn(continuation));
            var middle = visualLine.GetTextLineVisualYPosition(continuation,
                ICSharpCode.AvalonEdit.Rendering.VisualYPosition.TextMiddle) - view.VerticalOffset;
            view.BackgroundRenderers.Remove(renderer);
            var withoutMarker = RenderView();
            var stride = (int)Math.Ceiling(view.ActualWidth) * 4;
            var changedPixels = 0;
            for (var y = Math.Max(0, (int)middle - 5); y < Math.Min(view.ActualHeight, middle + 5); y++)
                for (var x = 0; x < indent; x++)
                {
                    var offset = y * stride + x * 4;
                    if (withMarker[offset] != withoutMarker[offset] ||
                        withMarker[offset + 1] != withoutMarker[offset + 1] ||
                        withMarker[offset + 2] != withoutMarker[offset + 2]) changedPixels++;
                }
            Assert.IsTrue(changedPixels > 5,
                $"The continuation arrow must be visible in the rendered editor. Changed pixels: {changedPixels}; indent: {indent}; row center: {middle}.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Windows_LoadTheirXamlStylesIconsAndLocalizedResources()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "en", "de" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var create = new CreateEditProjectWindow(new FlowBloxProject());
                var edit = new CreateEditProjectWindow(new FlowBloxProject(), true);
                var about = new AboutWindow();
                foreach (var window in new System.Windows.Window[] { create, edit, about })
                {
                    window.ApplyTemplate();
                    window.Measure(new System.Windows.Size(780, 760));
                    window.Arrange(new System.Windows.Rect(0, 0, 780, 760));
                    Assert.IsNotNull(window.Content);
                    window.Close();
                }
                var model = (AboutViewModel)about.DataContext;
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.ProductName));
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.Version));
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.Description));
                Assert.IsFalse(string.IsNullOrWhiteSpace(model.Copyright));
                Assert.AreNotEqual(((CreateEditProjectViewModel)create.DataContext).Title, ((CreateEditProjectViewModel)edit.DataContext).Title);
                if (culture == "de") Assert.AreEqual("_Schließen", FlowBlox.UICore.Resources.AboutWindow.Close);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [STATestMethod]
    public void Splash_InitializesOnceAndReportsFailureToCaller()
    {
        int calls = 0;
        var success = new SplashWindow(() => Interlocked.Increment(ref calls));
        var gif = new System.Windows.Media.Imaging.GifBitmapDecoder(
            new Uri("pack://application:,,,/FlowBlox.UICore;component/Resources/Images/FlowBloxStartup.gif"),
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Assert.HasCount(80, gif.Frames);
        Assert.AreEqual(2000, gif.Frames.Sum(frame => Convert.ToInt32(
            ((System.Windows.Media.Imaging.BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) * 10));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Assert.AreEqual(true, success.ShowDialog());
        Assert.IsTrue(elapsed.ElapsedMilliseconds >= 2500, "The two-second intro must be followed by the 500 ms keep time.");
        Assert.AreEqual(1, calls);
        Assert.IsNull(success.InitializationError);
        var exception = new InvalidOperationException("startup test");
        var failure = new SplashWindow(() => throw exception);
        Assert.AreEqual(false, failure.ShowDialog());
        Assert.AreSame(exception, failure.InitializationError);
    }

    [STATestMethod]
    public void Splash_HoldsAStaticFinalImageDuringHookAndFor500msAfterwards()
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        long hookStarted = 0, hookFinished = 0;
        System.Windows.Media.ImageSource? heldImage = null;
        SplashWindow splash = null!;
        splash = new SplashWindow(() =>
        {
            hookStarted = elapsed.ElapsedMilliseconds;
            splash.Dispatcher.Invoke(() =>
            {
                var image = (System.Windows.Controls.Image)splash.FindName("Logo");
                heldImage = image.Source;
                Assert.IsInstanceOfType<System.Windows.Media.Imaging.WriteableBitmap>(heldImage);
                Assert.IsTrue(heldImage.IsFrozen);
            });
            Thread.Sleep(150); // A real startup hook that takes longer than a single frame.
            splash.Dispatcher.Invoke(() => Assert.AreSame(heldImage,
                ((System.Windows.Controls.Image)splash.FindName("Logo")).Source));
            hookFinished = elapsed.ElapsedMilliseconds;
        });
        Assert.AreEqual(true, splash.ShowDialog());
        Assert.IsNull(splash.InitializationError);
        Assert.IsTrue(hookStarted >= 2000, "Initialization starts after the entire intro.");
        Assert.IsTrue(elapsed.ElapsedMilliseconds - hookFinished >= 500,
            "Keep time starts after the actual initialization has completed.");
        Assert.AreSame(heldImage, ((System.Windows.Controls.Image)splash.FindName("Logo")).Source);
    }

    [TestMethod]
    public void About_ReadsProductDescriptionVersionAndCopyrightFromAssembly()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var vm = new AboutViewModel(assembly);
        Assert.AreEqual(assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product, vm.ProductName);
        Assert.AreEqual(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, vm.RuntimeVersion);
        Assert.IsTrue(vm.Version.Contains(FlowBloxVersionHelper.GetDisplayVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Copyright));
    }

    [STATestMethod]
    public void SplashLogo_StaysCenteredThroughoutReconstruction()
    {
        var gif = new System.Windows.Media.Imaging.GifBitmapDecoder(
            new Uri("pack://application:,,,/FlowBlox.UICore;component/Resources/Images/FlowBloxStartup.gif"),
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        for (int index = 0; index < gif.Frames.Count; index++)
        {
            var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(gif.Frames[index],
                System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            int left = frame.PixelWidth, right = 0;
            for (int y = 0; y < frame.PixelHeight; y++)
                for (int x = 0; x < frame.PixelWidth; x++)
                {
                    int offset = (y * frame.PixelWidth + x) * 4;
                    if (pixels[offset] <= 110 || pixels[offset + 1] <= 100) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                }
            double center = (left + right) / 2.0;
            Assert.IsTrue(Math.Abs(center - (frame.PixelWidth - 1) / 2.0) <= 4,
                $"Frame {index}: visible logo center {center} must stay centered, including its glow.");
        }
    }
}
