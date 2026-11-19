using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.ViewModels.ProjectPanel;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckFlowBlockIconsAsync(Action<bool, string> check, string directory)
    {
        var block = new ExecuteProjectFlowBlock { Name = "ExecuteProject", ProjectFile = "local.fbprj" };
        var panel = new ProjectPanelWpfControl();
        var node = new FlowBlockNodeViewModel(block);
        panel.ViewModel.Nodes.Add(node);
        var window = new Window { Content = panel, Width = 440, Height = 380, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            var image = MenuVisuals<Image>(panel).Single(item => ReferenceEquals(item.DataContext, node));
            var localPixels = ReadIconPixels((BitmapSource)image.Source);
            SaveMenuImage(directory, panel, "execute-project-local-icon.png");

            block.ProjectSpaceGuid = "e267c48d7dccad99d9454364016bd03c";
            FlowBloxComponentHelper.RaisePropertyChanged(block, nameof(block.ProjectSpaceGuid));
            await Task.Delay(50);
            check(!localPixels.SequenceEqual(ReadIconPixels((BitmapSource)image.Source)),
                "Standard flow-block header refreshes the Project Space icon after an ordinary property change");
            SaveMenuImage(directory, panel, "execute-project-space-icon.png");

            block.ProjectSpaceGuid = null;
            FlowBloxComponentHelper.RaisePropertyChanged(block, nameof(block.ProjectSpaceGuid));
            await Task.Delay(50);
            check(localPixels.SequenceEqual(ReadIconPixels((BitmapSource)image.Source)),
                "Clearing Project Space GUID restores the standard local project icon");
        }
        finally
        {
            window.Close();
            panel.ViewModel.Dispose();
        }
    }

    private static byte[] ReadIconPixels(BitmapSource bitmap)
    {
        var stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
