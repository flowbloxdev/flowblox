using FlowBlox.Core.Constants;
using CommandLine;
using FlowBlox.CommandLine;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Util.Fonts;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlowBlox
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (InstallFonts())
                return;

            SetupCulture();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var splash = new UICore.Views.SplashWindow(AppWindow.AppWindow.InitApp);
            if (splash.ShowDialog() != true)
            {
                var error = splash.InitializationError;
                FlowBloxLogManager.Instance.GetLogger().Error("Application initialization failed.", e: error);
                System.Windows.MessageBox.Show(
                    string.Format(UICore.Resources.SplashWindow.Error, error?.Message),
                    UICore.Resources.SplashWindow.ErrorTitle,
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            var options = ParseCommandLine(args);

            var flowBloxAutoLoginExecutor = new FlowBloxAutoLoginExecutor();
            Task.Run(async () => await flowBloxAutoLoginExecutor.TryAutoLoginAsync())
                .GetAwaiter()
                .GetResult();

            InitProjectFile(options);
            Application.Run(AppWindow.AppWindow.Instance);
        }

        private static void SetupCulture()
        {
            var uiCultureOption = FlowBloxOptions
                .GetOptionInstance()
                .GetOption(OptionsConstants.UICulture);

            if (uiCultureOption == null || string.IsNullOrWhiteSpace(uiCultureOption.Value))
                return;

            try
            {
                var culture = CultureInfo.GetCultureInfo(uiCultureOption.Value);

                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch (CultureNotFoundException ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error($"Invalid UI culture '{uiCultureOption.Value}'. Falling back to operating system culture.", e: ex);
            }
        }

        private static Options ParseCommandLine(string[] args)
        {
            var parserResult = new Parser().ParseArguments<Options>(args);
            Options parsedOptions = null;

            parserResult.WithParsed(opts => parsedOptions = opts)
                        .WithNotParsed(errs => parsedOptions = new Options());

            return parsedOptions;
        }

        private static void InitProjectFile(Options options)
        {
            if (!string.IsNullOrEmpty(options?.ProjectFile))
                AppWindow.AppWindow.Instance.SetProjectFile(options.ProjectFile);

            if (!string.IsNullOrEmpty(options?.ProjectSpaceGuid))
                AppWindow.AppWindow.Instance.SetProjectSpaceGuid(options.ProjectSpaceGuid);
        }

        private static bool InstallFonts()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            string fontsDirectory = Path.Combine(assemblyDir, "data", "fonts");

            if (!Directory.Exists(fontsDirectory))
                return false;

            var fontFiles = Directory.GetFiles(fontsDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

            bool anyInstalled = false;
            foreach (var fontFile in fontFiles)
            {
                try
                {
                    if (FontInstaller.InstallFontForCurrentUser(fontFile))
                        anyInstalled = true;
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error($"Failed to install font: '{fontFile}'", ex);
                }
            }

            if (anyInstalled)
            {
                Application.Restart();
                return true;
            }

            return false;
        }
    }
}
