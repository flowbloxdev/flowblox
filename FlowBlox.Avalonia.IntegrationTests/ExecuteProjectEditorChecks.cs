using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckExecuteProjectEditorsAsync(Action<bool, string> check, string directory)
    {
        var projectPath = Path.Combine(directory, "execute-project-input-metadata.fbprj");
        File.WriteAllText(projectPath, """
            {
              "ProjectName": "Input metadata check",
              "UserFields": [
                {
                  "Name": "FileContent",
                  "UserField": true,
                  "UserFieldType": "Input",
                  "FieldType": { "FieldType": "ByteArray" }
                }
              ]
            }
            """);
        var block = new ExecuteProjectFlowBlock
        {
            ProjectFile = projectPath
        };
        var mapping = new ExecuteProjectParameterMappingEntry { TargetKey = "FileContent" };
        block.ParameterMappings.Add(mapping);
        var content = new StackPanel { Margin = new Thickness(12) };
        var window = new Window { Content = content, Width = 820, Height = 360, ShowInTaskbar = false, ShowActivated = false };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FlowBlox.UICore;component/Views/Styles/HostedControlStyles.xaml", UriKind.Relative)
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FlowBlox.UICore;component/Views/Styles/PropertyViewStyles.xaml", UriKind.Relative)
        });
        var resolver = new PropertyControlResolver(window);
        var targetEditor = resolver.Resolve(typeof(ExecuteProjectParameterMappingEntry).GetProperty(nameof(mapping.TargetKey)),
            mapping, false, block, new PropertyControlBindingContext(mapping)).Control;
        var metadataEditor = resolver.Resolve(typeof(ExecuteProjectFlowBlock).GetProperty(nameof(block.InputFields)),
            block, false, null, new PropertyControlBindingContext(block)).Control;
        content.Children.Add(targetEditor);
        content.Children.Add(metadataEditor);
        try
        {
            window.Show();
            await Task.Delay(50);
            window.UpdateLayout();
            var choices = MenuVisuals<SelectionDropDownControl>(targetEditor).Single();
            check(choices.ItemsSource.Any(item => Equals(item.Value, "FileContent")),
                "Execute Project Target Key offers input names from the selected child project");
            var grid = MenuVisuals<DataGrid>(metadataEditor).Single();
            check(grid.IsReadOnly && grid.Items.OfType<ProjectFieldMetadata>().Single().Name == "FileContent"
                && grid.Columns.Count == 5 && !grid.BeginEdit(),
                "Execute Project renders input metadata in a read-only five-column grid");
            SaveMenuImage(directory, content, "execute-project-input-metadata.png");

            mapping.TargetType = ExecuteProjectTargetType.Option;
            FlowBloxComponentHelper.RaisePropertyChanged(mapping, nameof(mapping.TargetType));
            await Task.Delay(50);
            choices = MenuVisuals<SelectionDropDownControl>(targetEditor).Single();
            check(choices.ItemsSource.Any(item => Equals(item.Value, OptionsConstants.PathsInputDir))
                && !choices.ItemsSource.Any(item => Equals(item.Value, "FileContent")),
                "Switching the mapping target to Option replaces input suggestions with option keys");
        }
        finally
        {
            window.Close();
        }
    }
}
