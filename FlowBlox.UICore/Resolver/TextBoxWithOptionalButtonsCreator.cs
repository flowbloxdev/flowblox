using FlowBlox.Core.Constants;
using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using TextBoxWithOptionalButtonsCreatorResources = FlowBlox.UICore.Resources.ClassResources.TextBoxWithOptionalButtonsCreator;
using FlowBlox.Core;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Attributes.FlowBlox.Core.Attributes;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Factory.Adapter;
using FlowBlox.UICore.Factory.PropertyView;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.IconPacks;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlowBlox.Core.Util.FlowBlocks;

namespace FlowBlox.UICore.Resolver
{
    public class TextBoxWithOptionalButtonsCreator
    {
        private Window _window;

        public TextBoxWithOptionalButtonsCreator(Window window)
        {
            this._window = window;
        }

        private const double IconSize = 10;

        public FrameworkElement CreateTextBoxWithOptionalButtons(
            PropertyInfo property,
            object target,
            string displayName,
            FlowBloxUIAttribute uiAttribute,
            Binding binding,
            bool readOnly,
            bool? passwordOverride = null)
        {
            var textAttribute = property.GetCustomAttribute<FlowBloxTextBoxAttribute>();
            var isPassword = passwordOverride ?? (string.IsNullOrWhiteSpace(textAttribute?.IsPasswordMethod)
                ? textAttribute?.IsPassword == true
                : (bool)target.GetType().GetMethod(textAttribute.IsPasswordMethod).Invoke(target, null));
            if (isPassword)
            {
                var passwordBox = new PasswordBox
                {
                    Password = property.GetValue(target) as string ?? string.Empty,
                    IsEnabled = !readOnly
                };
                passwordBox.PasswordChanged += (_, _) =>
                {
                    if (readOnly)
                        return;
                    property.SetValue(target, passwordBox.Password);
                    FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
                };
                return passwordBox;
            }
            var fieldSelectionAttribute = property.GetCustomAttribute<FlowBloxFieldSelectionAttribute>();
            var openFromFileSystemAttribute = property.GetCustomAttribute<FlowBloxOpenFromFileSystemAttribute>();
            var buttonVerticalAlignment = textAttribute?.MultiLine == true
                ? VerticalAlignment.Top
                : VerticalAlignment.Center;
            FrameworkElement baseTextBox;

            if (textAttribute?.IsCodingMode == true)
            {
                var factory = new AvalonEditTextBoxFactory(property, target, readOnly);
                baseTextBox = factory.Create(textAttribute);
            }
            else if (textAttribute?.MultiLine == true)
            {
                var factory = new MultilineTextBoxFactory(property, target, readOnly);
                baseTextBox = factory.Create();
            }
            else
            {
                var textBox = new TextBox
                {
                    IsReadOnly = readOnly
                };
                textBox.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.TextEditorStyle");
                PropertyEditorBinding.Bind(textBox, TextBox.TextProperty, binding, target, property.Name);
                baseTextBox = textBox;
            }

            var textBoxControl = GetTextboxFromFrameworkElement(baseTextBox);

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            System.Windows.Controls.Grid.SetColumn(baseTextBox, 0);
            grid.Children.Add(baseTextBox);

            int columnIndex = 1;

            // File Selection
            if (uiAttribute?.UiOptions.HasFlag(UIOptions.EnableFileSelection) == true)
            {
                var fileSelectionAttribute = property.GetCustomAttribute<FlowBloxUIFileSelectionAttribute>();
                var filter = fileSelectionAttribute?.Filter ?? "All files (*.*)|*.*";

                var fileButton = new Button
                {
                    Content = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.FileDocumentOutline,
                        Width = IconSize,
                        Height = IconSize
                    },
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = buttonVerticalAlignment,
                    ToolTip = FlowBloxResourceUtil.GetLocalizedString("EnableFileSelection_Tooltip", typeof(TextBoxWithOptionalButtonsCreatorResources)),
                    IsEnabled = !readOnly
                };

                fileButton.Click += (s, e) =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = filter
                    };
                    ApplyFileDialogInitialPath(
                        dialog,
                        property.GetValue(target)?.ToString(),
                        ResolveInitialDirectory(target, openFromFileSystemAttribute));

                    if (dialog.ShowDialog() == true)
                    {
                        var replacedPath = ReplacePathPrefixesWithKnownPlaceholders(dialog.FileName);
                        property.SetValue(target, replacedPath);

                        if (textBoxControl is WpfTextBoxAdapter wpfAdapter)
                        {
                            var expression = wpfAdapter.InnerTextBox.GetBindingExpression(TextBox.TextProperty);
                            expression?.UpdateTarget();
                        }
                        else if (textBoxControl is AvalonEditAdapter avalonAdapter)
                        {
                            avalonAdapter.Text = property.GetValue(target)?.ToString();
                        }
                    }
                };

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(fileButton, columnIndex++);
                grid.Children.Add(fileButton);
            }

            // Folder Selection
            if (uiAttribute?.UiOptions.HasFlag(UIOptions.EnableFolderSelection) == true)
            {
                var folderButton = new Button
                {
                    Content = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.FolderOutline,
                        Width = IconSize,
                        Height = IconSize
                    },
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = buttonVerticalAlignment,
                    ToolTip = FlowBloxResourceUtil.GetLocalizedString("EnableFolderSelection_Tooltip", typeof(TextBoxWithOptionalButtonsCreatorResources)),
                    IsEnabled = !readOnly
                };

                folderButton.Click += (s, e) =>
                {
                    using var dialog = new System.Windows.Forms.FolderBrowserDialog();
                    ApplyFolderDialogInitialPath(
                        dialog,
                        property.GetValue(target)?.ToString(),
                        ResolveInitialDirectory(target, openFromFileSystemAttribute));

                    var result = dialog.ShowDialog();

                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        var replacedPath = ReplacePathPrefixesWithKnownPlaceholders(dialog.SelectedPath);
                        property.SetValue(target, replacedPath);

                        if (textBoxControl is WpfTextBoxAdapter wpfAdapter)
                        {
                            var expression = wpfAdapter.InnerTextBox.GetBindingExpression(TextBox.TextProperty);
                            expression?.UpdateTarget();
                        }
                        else if (textBoxControl is AvalonEditAdapter avalonAdapter)
                        {
                            avalonAdapter.Text = property.GetValue(target)?.ToString();
                        }
                    }
                };

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(folderButton, columnIndex++);
                grid.Children.Add(folderButton);
            }

            // Field Selection
            if (uiAttribute?.UiOptions.HasFlag(UIOptions.EnableFieldSelection) == true)
            {
                var fieldButton = new Button
                {
                    Content = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.Variable,
                        Width = IconSize,
                        Height = IconSize
                    },
                    Margin = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = buttonVerticalAlignment,
                    ToolTip = FlowBloxResourceUtil.GetLocalizedString("EnableFieldSelection_Tooltip", typeof(TextBoxWithOptionalButtonsCreatorResources)),
                    IsEnabled = !readOnly
                };

                ((PackIconMaterial)fieldButton.Content).SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.FieldSelectionIconStyle");
                fieldButton.Click += (s, e) =>
                {
                    Utilities.TextBoxHelper.ShowFieldSelectionDialog(target, uiAttribute, fieldSelectionAttribute, textBoxControl, _window);
                };

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(fieldButton, columnIndex++);
                grid.Children.Add(fieldButton);
            }

            // Toolbox
            if (uiAttribute?.ToolboxCategory != null && !string.IsNullOrEmpty(uiAttribute.ToolboxCategory))
            {
                var toolboxButton = new Button
                {
                    Content = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.Tools,
                        Width = IconSize,
                        Height = IconSize
                    },
                    Margin = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = buttonVerticalAlignment,
                    ToolTip = FlowBloxResourceUtil.GetLocalizedString("EnableToolbox_Tooltip", typeof(TextBoxWithOptionalButtonsCreatorResources)),
                    IsEnabled = !readOnly
                };

                toolboxButton.Click += (s, e) =>
                {
                    Utilities.TextBoxHelper.ShowToolboxDialog(textBoxControl, uiAttribute.ToolboxCategory, _window);
                };

                if (!readOnly)
                {
                    if (uiAttribute.ToolboxCategory == nameof(FlowBloxToolboxCategory.Regex))
                    {
                        Utilities.TextBoxHelper.RegisterRegexOnParameterSelectedAction(textBoxControl, _window);
                    }
                    else if (uiAttribute.UiOptions.HasFlag(UIOptions.EnableFieldSelection))
                    {
                        Utilities.TextBoxHelper.RegisterOnParameterSelectedAction(target, textBoxControl, _window);
                    }
                }

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(toolboxButton, columnIndex++);
                grid.Children.Add(toolboxButton);
            }

            return grid;
        }

        private ITextBoxLike GetTextboxFromFrameworkElement(FrameworkElement frameworkElement)
        {
            if (frameworkElement is TextBox textBox)
                return new WpfTextBoxAdapter(textBox);

            if (frameworkElement is ComboBox comboBox)
                return new ComboBoxAdapter(comboBox);

            if (frameworkElement is ICSharpCode.AvalonEdit.TextEditor editor)
                return new AvalonEditAdapter(editor);

            if (frameworkElement is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        var result = GetTextboxFromFrameworkElement(fe);
                        if (result != null)
                            return result;
                    }
                }
            }

            if (frameworkElement is System.Windows.Controls.Grid grid)
            {
                foreach (UIElement child in grid.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        var result = GetTextboxFromFrameworkElement(fe);
                        if (result != null)
                            return result;
                    }
                }
            }

            throw new InvalidOperationException("TextBox-like element could not be found in the FrameworkElement.");
        }

        private static string ResolveInitialDirectory(
            object target,
            FlowBloxOpenFromFileSystemAttribute attribute)
        {
            if (target == null || string.IsNullOrWhiteSpace(attribute?.InitialDirectoryMethod))
                return string.Empty;

            var method = target.GetType().GetMethod(
                attribute.InitialDirectoryMethod,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException(
                    $"Initial-directory method '{attribute.InitialDirectoryMethod}' not found on type '{target.GetType().FullName}'.");
            if (method.ReturnType != typeof(string) || method.GetParameters().Length != 0)
                throw new InvalidOperationException(
                    $"Initial-directory method '{attribute.InitialDirectoryMethod}' must return string and have no parameters.");

            return method.Invoke(target, null) as string ?? string.Empty;
        }

        private static void ApplyFileDialogInitialPath(
            Microsoft.Win32.OpenFileDialog dialog,
            string currentValue,
            string initialDirectory)
        {
            if (dialog == null)
                return;

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            var resolvedValue = FlowBloxFieldHelper.ReplaceFieldsInString(currentValue);
            if (string.IsNullOrWhiteSpace(resolvedValue))
                return;

            try
            {
                if (Directory.Exists(resolvedValue))
                {
                    if (string.IsNullOrWhiteSpace(dialog.InitialDirectory))
                        dialog.InitialDirectory = resolvedValue;
                    return;
                }

                var directory = Path.GetDirectoryName(resolvedValue);
                if (string.IsNullOrWhiteSpace(dialog.InitialDirectory) &&
                    !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    dialog.InitialDirectory = directory;

                var fileName = Path.GetFileName(resolvedValue);
                if (!string.IsNullOrWhiteSpace(fileName))
                    dialog.FileName = fileName;
            }
            catch (Exception e)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("An error occurred while determining the directory and filename from the current value.", e);
            }
        }

        private static void ApplyFolderDialogInitialPath(
            System.Windows.Forms.FolderBrowserDialog dialog,
            string currentValue,
            string initialDirectory)
        {
            if (dialog == null)
                return;

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.SelectedPath = initialDirectory;
                return;
            }

            var resolvedValue = FlowBloxFieldHelper.ReplaceFieldsInString(currentValue);
            if (string.IsNullOrWhiteSpace(resolvedValue))
                return;

            try
            {
                if (Directory.Exists(resolvedValue))
                {
                    dialog.SelectedPath = resolvedValue;
                    return;
                }

                var directory = Path.GetDirectoryName(resolvedValue);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    dialog.SelectedPath = directory;
            }
            catch (Exception e)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("An error occurred while determining the directory from the current value.", e);
            }
        }

        private static string ReplacePathPrefixesWithKnownPlaceholders(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
                return selectedPath;

            var candidates = new List<(string DirectoryPath, string Placeholder)>();

            var activeProject = FlowBloxProjectManager.Instance.ActiveProject;
            if (activeProject != null)
            {
                candidates.Add((activeProject.ProjectInputDirectory, FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, "InputDirectory")));
                candidates.Add((activeProject.ProjectOutputDirectory, FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, "OutputDirectory")));
                candidates.Add((activeProject.ProjectDirectory, FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, "Directory")));
            }

            var options = FlowBloxOptions.GetOptionInstance();
            AddOptionPathCandidate(OptionsConstants.PathsInputDir);
            AddOptionPathCandidate(OptionsConstants.PathsOutputDir);
            AddOptionPathCandidate(OptionsConstants.AIOnnxQAModelRootDirectory);
            AddOptionPathCandidate(OptionsConstants.AIOnnxGenAIModelRootDirectory);

            void AddOptionPathCandidate(string optionName)
            {
                if (options.OptionCollection.TryGetValue(optionName, out var option))
                {
                    candidates.Add((
                        FlowBloxFieldHelper.ReplaceFieldsInString(option.Value),
                        FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Options, optionName)));
                }
            }

            var normalizedSelectedPath = IOUtil.NormalizePath(selectedPath, trimTrailingDirectorySeparator: true);
            if (string.IsNullOrWhiteSpace(normalizedSelectedPath))
                return selectedPath;

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = IOUtil.NormalizePath(candidate.DirectoryPath, trimTrailingDirectorySeparator: true);
                if (string.IsNullOrWhiteSpace(normalizedCandidate))
                    continue;

                if (normalizedSelectedPath.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    return candidate.Placeholder;

                var candidateWithSeparator = IOUtil.EnsureTrailingDirectorySeparator(normalizedCandidate);
                if (normalizedSelectedPath.StartsWith(candidateWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = normalizedSelectedPath.Substring(candidateWithSeparator.Length);
                    return string.IsNullOrWhiteSpace(suffix)
                        ? candidate.Placeholder
                        : $"{candidate.Placeholder}\\{suffix}";
                }
            }

            return selectedPath;
        }

    }
}
