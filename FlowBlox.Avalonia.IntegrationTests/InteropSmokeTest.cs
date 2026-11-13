using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer;
using FlowBlox.AppWindow.Contents;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.Interfaces;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.AvaloniaDesktop.Docking;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using MainWindow = FlowBlox.AppWindow.AppWindow;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private readonly MainWindow _window;
    private DockControl Workspace => _window.FindControl<DockControl>("Workspace");
    private Menu MainMenu => _window.FindControl<Menu>("MainMenu");
    private ProjectPanel ProjectPanelView => _window.Panels().OfType<ProjectPanel>().Single();
    private FieldView FieldPanel => _window.Panels().OfType<FieldView>().Single();
    private TestView TestPanel => _window.Panels().OfType<TestView>().Single();
    private ManagedObjectsView ManagedObjectsPanel => _window.Panels().OfType<ManagedObjectsView>().Single();

    public WorkspaceIntegrationChecks(MainWindow window) => _window = window;

    // Runs inside the real desktop lifecycle, with an isolated workspace and project copy.
    public async Task<bool> RunAsync(string directory, string sourceProjectPath, bool fieldSelectionOnly = false, bool launcherOnly = false)
    {
        var results = new List<string>();
        string error = null;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            results.Add(name);
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "progress.txt"), results);
        }
        try
        {
            Directory.CreateDirectory(directory);
            if (launcherOnly)
            {
                await CheckProjectLauncherAsync(Check, directory, sourceProjectPath);
                return true;
            }
            // Use a Windows-supplied, already installed family to exercise the
            // bootstrap's early exit without installing a font on the test machine.
            var systemFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
            using (var installedFonts = new System.Drawing.Text.InstalledFontCollection())
                if (!File.Exists(systemFont) || !installedFonts.Families.Any(family => family.Name == "Segoe UI"))
                    throw new InvalidOperationException("The installed-font check requires the standard Windows Segoe UI font.");
            var fontFileName = "flowblox-installed-font-check-" + Guid.NewGuid().ToString("N") + ".ttf";
            var fontCopy = Path.Combine(directory, fontFileName);
            File.Copy(systemFont, fontCopy);
            Check(!AvaloniaDesktop.Interop.Windows.FontInstaller.InstallFontForCurrentUser(fontCopy),
                "Font bootstrap skips an already installed system family");
            Check(!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts", fontFileName)),
                "Installed system font is not copied into the user font directory");
            Check(_window.Menus.Count == 6 && _window.Menus.All(x => !x.Text.Contains("_Text")), "Localized menu hierarchy");
            var texts = AvaloniaDesktop.Resources.FlowBloxMainUITexts.ResourceManager;
            Check(texts.GetString("About_Text", System.Globalization.CultureInfo.GetCultureInfo("en")) == "About FlowBlox",
                "Application-owned English resources resolve after namespace migration");
            Check(texts.GetString("AppWindow_CloseProjectConfirm_Title", System.Globalization.CultureInfo.GetCultureInfo("de")) !=
                  texts.GetString("AppWindow_CloseProjectConfirm_Title", System.Globalization.CultureInfo.GetCultureInfo("en")),
                "Application-owned German satellite resources resolve after namespace migration");
            CheckSimplePropertyFieldSelection(Check);
            await CheckEnabledFieldSelectionEditorsAsync(Check);
            await CheckLiteralGridRenderingAsync(Check);
            await CheckManagedObjectActionsDropDownAsync(directory, Check);
            await CheckAssistantProviderSelectionAsync(Check);
            CheckProjectSpaceCards(Check, directory);
            await CheckMissingProjectSpaceGuidAsync(Check);
            CheckFieldSelectionDialogScrolling(Check, directory);
            CheckFieldSelectionDialogContext(Check);
            await CaptureNotificationBarPreviewAsync(directory);
            if (fieldSelectionOnly)
                return true;
            await CheckDesktopDialogsAsync(Check, directory);
            Check(!_window.itmSaveProject.Enabled && !_window.itmCloseProject.Enabled, "Commands disabled without project");
            CheckWorkspaceObserver(Check);
            // Use the same entry-assembly pack URI and image decoder as ExtensionsWindow.xaml.
            var extensionIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Icons/extension.ico"));
            Check(extensionIcon.PixelWidth > 0 && extensionIcon.PixelHeight > 0,
                "Extensions window icon resolves and decodes through WPF pack URI");
            var projectPath = Path.Combine(directory, "smoke.fbprj");
            File.Copy(sourceProjectPath, projectPath, true);
            var project = FlowBloxProject.FromFile(projectPath);
            _window.SetProjectFile(projectPath);
            FlowBloxProjectManager.Instance.ActiveProjectPath = projectPath;
            FlowBloxProjectManager.Instance.ActiveProject = project;
            _window.AfterProjectFullyInitialized();
            ProjectPanelView.OnAfterProjectOpened(project);
            _window.UpdateUI();
            _window.UpdateUI_ProjectName();
            Check(_window.itmSaveProject.Enabled && _window.itmCloseProject.Enabled, "Commands enabled with project");
            foreach (var panel in _window.Panels())
            {
                _window.ActivatePanel(panel);
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    _window.UpdateLayout();
                    await Task.Delay(100);
                    if (panel.Handle != IntPtr.Zero && panel.WpfContent.IsLoaded && panel.WpfContent.ActualWidth > 0) break;
                }
                var panelModel = _window.WorkspaceFactory.GetDockable<IDockable>(panel.GetType().Name);
                Check(panel.Handle != IntPtr.Zero, panel.GetType().Name + $" HWND created (Owner={panelModel?.Owner?.Id}, Active={(panelModel?.Owner as IDock)?.ActiveDockable?.Id}, Factory={panelModel?.Factory != null}, Content={ReferenceEquals((panelModel as PanelTool)?.Content ?? (panelModel as PanelDocument)?.Content, panel)}, Parent={panel.GetVisualParent()?.GetType().Name})");
                var content = panel.WpfContent;
                File.WriteAllText(Path.Combine(directory, panel.GetType().Name + "-layout.txt"), $"Self={panel.IsVisible}, Desired={panel.DesiredSize}, Measure={panel.IsMeasureValid}, Arrange={panel.IsArrangeValid}, Alignment={panel.HorizontalAlignment}/{panel.VerticalAlignment}\n" + string.Join("\n", panel.GetVisualAncestors().Select(x => x.GetType().Name + " " + x.Bounds + " " + x.IsVisible)));
                Check(content.IsLoaded && content.ActualWidth > 0 && content.ActualHeight > 0, panel.GetType().Name + $" WPF layout loaded (Loaded={content.IsLoaded}, Size={content.ActualWidth}x{content.ActualHeight}, Host={panel.Bounds})");
                var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(directory, panel.GetType().Name + ".png"));
                encoder.Save(file);
            }
            await CheckConnectionPreviewBeforeMessageAsync(Check);
            await CheckHostedFocusAsync(Check);
            await CheckEditorPopupsAsync(directory, Check);
            await CheckContextMenusAsync(directory, Check);
            CheckProjectCenterDocking(Check);
            await CheckAutomaticLayoutSaveAsync(directory, Check);
            var state = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            state.SetExternalProjectEditActive(true);
            Check(!_window.itmSaveProject.Enabled && !_window.itmCreateProject.Enabled, "Runtime state service disables editing commands");
            state.SetExternalProjectEditActive(false);
            Check(_window.itmSaveProject.Enabled, "Runtime state service restores editing commands");
            var field = _window.WorkspaceFactory.GetDockable<IDockable>(nameof(FieldView));
            Check(field != null, "Dockable lookup");
            _window.WorkspaceFactory.HideDockable(field);
            _window.WorkspaceFactory.GetDockable<IDockable>("Left").Proportion = 0.2;
            _window.ActivatePanel(TestPanel);
            var serializer = new DockSerializer();
            var json = serializer.Serialize<IRootDock>((IRootDock)Workspace.Layout);
            var restored = serializer.Deserialize<IRootDock>(json);
            Check(restored.HiddenDockables.Any(x => x.Id == nameof(FieldView)), "Hidden panel survives layout serialization");
            _window.WorkspaceFactory.DetachContent(Workspace.Layout);
            Workspace.Layout = null;
            _window.UpdateLayout();
            _window.WorkspaceFactory.InitLayout(restored);
            Workspace.Layout = restored;
            Check(Math.Abs(_window.WorkspaceFactory.GetDockable<IDockable>("Left").Proportion - 0.2) < 0.00001, "Panel size survives layout reload");
            Check(_window.WorkspaceFactory.GetDockable<IDock>("Bottom").ActiveDockable.Id == nameof(TestView), "Selected tab survives layout reload");
            _window.ActivatePanel(FieldPanel);
            await Task.Delay(250);
            Check(!restored.HiddenDockables.Any(x => x.Id == nameof(FieldView)), "Hidden panel restored after layout reload");
            var restoredField = _window.WorkspaceFactory.GetDockable<IDockable>(nameof(FieldView));
            Check(restoredField.Owner is IDock visibleOwner && visibleOwner.VisibleDockables.Contains(restoredField), "Restored panel is attached to its saved dock");
            _window.ActivatePanel(ManagedObjectsPanel);
            _window.SaveLayout();
            Check(File.Exists(Path.Combine(directory, "layout.json")), "Layout persisted to disk");
            var savePath = Path.Combine(directory, "saved.fbprj");
            _window.SaveActiveProjectFile(savePath);
            Check(File.Exists(savePath) && FlowBloxProjectManager.Instance.ActiveProjectPath == savePath, "Project saved through shell save pipeline");
            _window.WorkspaceFactory.HideDockable(_window.WorkspaceFactory.GetDockable<IDockable>(nameof(FieldView)));
            _window.SaveLayout();
            Check(((WorkspaceRoot)Workspace.Layout).HiddenOwners.ContainsKey(nameof(FieldView)), "Hidden panel origin persisted for next process startup");
            await CheckProjectClosingHidesWorkspaceAsync(Check, directory);
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(new { Passed = error == null, Checks = results, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }
        return error == null;
    }
}
