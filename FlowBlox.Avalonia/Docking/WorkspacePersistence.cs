using System;
using System.IO;
using System.Text.Json;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Serializer;
using FlowBlox.Core.Logging;

namespace FlowBlox.AvaloniaDesktop.Docking;

public static class WorkspacePersistence
{
    private static readonly string DirectoryPath = App.StartupOptions.WorkspaceDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowBlox", "Avalonia");
    private static readonly DockSerializer Serializer = new();
    private static string _lastLayout;
    private static WindowBounds _lastWindow;
    public sealed record WindowBounds(double Width, double Height, bool Maximized);

    public static IRootDock Load()
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "layout.json");
            if (!File.Exists(path)) return null;
            return Serializer.Deserialize<IRootDock>(File.ReadAllText(path));
        }
        catch (Exception ex) { Log(ex); return null; }
    }

    public static void Save(IRootDock layout)
    {
        try
        {
            var json = Serializer.Serialize<IRootDock>(layout);
            if (json == _lastLayout) return;
            WriteAtomic("layout.json", json);
            _lastLayout = json;
        }
        catch (Exception ex) { Log(ex); }
    }

    public static WindowBounds LoadWindow()
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "window.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) { Log(ex); return null; }
    }

    public static void SaveWindow(WindowBounds bounds)
    {
        try
        {
            if (bounds == _lastWindow) return;
            WriteAtomic("window.json", JsonSerializer.Serialize(bounds));
            _lastWindow = bounds;
        }
        catch (Exception ex) { Log(ex); }
    }

    private static void WriteAtomic(string name, string contents)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, name);
        File.WriteAllText(path + ".tmp", contents);
        File.Move(path + ".tmp", path, true);
    }
    private static void Log(Exception ex) => FlowBloxLogManager.Instance.GetLogger().Error("Unable to persist Avalonia workspace.", ex);
}
