using FlowBlox.Core.Provider.Placeholders;
using System.Text.RegularExpressions;
using System.Windows;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Views;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Models.DialogService;
using FlowBlox.UICore.Enums;
using FlowBlox.Core.Utilities;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Placeholders.GenerationStrategy;
using FlowBlox.UICore.Models.FieldSelection;


namespace FlowBlox.UICore.Utilities
{
    public static class TextBoxHelper
    {
        public static FieldSelectionWindowArgs CreateFieldSelectionWindowArgs(
            object target,
            FlowBloxFieldSelectionAttribute fieldSelection,
            object parent = null)
        {
            var args = new FieldSelectionWindowArgs
            {
                FlowBlock = target as BaseFlowBlock ?? parent as BaseFlowBlock,
                SelectionMode = FieldSelectionMode.Fields
            };

            if (fieldSelection != null)
            {
                args.IsRequired = fieldSelection.DefaultRequiredValue;
                args.HideRequired = fieldSelection.HideRequiredCheckbox;

                var modes = fieldSelection.AllowedFieldSelectionModes;
                if (!string.IsNullOrWhiteSpace(fieldSelection.AllowedFieldSelectionModesMethod))
                {
                    var resolution = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(
                        target, null, fieldSelection.AllowedFieldSelectionModesMethod);
                    
                    if (resolution?.Method == null || 
                        resolution.Method.GetParameters().Length != 0 || 
                        resolution.Method.Invoke(resolution.InvocationTarget, null) is not FieldSelectionModes dynamicModes)
                    {
                        throw new InvalidOperationException($"Field selection method '{fieldSelection.AllowedFieldSelectionModesMethod}' must be parameterless and return FieldSelectionModes.");
                    }
                        
                    modes = dynamicModes;
                }

                var allowedModes = ConvertAllowedModes(modes);
                if (allowedModes != null)
                    args.AllowedFieldSelectionModes = allowedModes;
            }

            if (args.AllowedFieldSelectionModes != null &&
                args.AllowedFieldSelectionModes.Contains(FieldSelectionMode.GenerationStrategyData))
            {
                args.GenerationStrategyDataElements = FlowBloxGenerationStrategyPlaceholderProvider.GetElements();
            }
            return args;
        }

        public static FieldSelectionWindowResult ShowFieldSelectionDialog(
            FieldSelectionWindowArgs args,
            Window ownerWindow)
        {
            var win = new FieldSelectionWindow(args)
            {
                Owner = ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (win.ShowDialog() != true || win.Result == null)
                return null;

            return win.Result;
        }

        public static void ShowFieldSelectionDialog(
            object target,
            FlowBloxUIAttribute uiAttribute,
            FlowBloxFieldSelectionAttribute fieldSelection,
            ITextBoxLike textBox,
            Window ownerWindow)
        {
            var args = CreateFieldSelectionWindowArgs(target, fieldSelection);
            ShowFieldSelectionDialog(target, args, textBox, ownerWindow);
        }

        public static void ShowFieldSelectionDialog(
            object target,
            FlowBloxUIAttribute uiAttribute,
            ITextBoxLike textBox,
            Window ownerWindow)
        {
            ShowFieldSelectionDialog(target, uiAttribute, fieldSelection: null, textBox, ownerWindow);
        }

        public static void ShowFieldSelectionDialog(
            object target,
            FieldSelectionWindowArgs args,
            ITextBoxLike textBox,
            Window ownerWindow)
        {
            var result = ShowFieldSelectionDialog(args, ownerWindow);
            if (result == null)
                return;

            if (result.SelectionMode == FieldSelectionMode.Fields)
            {
                FlowBlockHelper.ApplyFieldSelectionRequiredOption(target, result.SelectedFields, result.IsRequired);
                ApplyFieldElementsToTextBox(result.SelectedFields, textBox);
            }
            else if (result.SelectionMode == FieldSelectionMode.ProjectProperties)
            {
                ApplyProjectPropertyElementsToTextBox(result.SelectedProjectProperties, textBox);
            }
            else
            {
                if (result.SelectionMode == FieldSelectionMode.Vault)
                    ApplyFieldDefinitionsToTextBox(result.SelectedVaultReferences, textBox);
                else if (result.SelectionMode == FieldSelectionMode.Options)
                    ApplyOptionElementsToTextBox(result.SelectedOptions, textBox);
                else if (result.SelectionMode == FieldSelectionMode.InputFiles)
                    ApplyInputFileElementsToTextBox(result.SelectedInputFiles, textBox);
                else
                    ApplyGenerationStrategyDataElementsToTextBox(result.SelectedGenerationStrategyData, textBox);
            }
        }

        private static FieldSelectionMode[] ConvertAllowedModes(FieldSelectionModes allowedModes)
        {
            if (allowedModes == FieldSelectionModes.Default)
                return null;

            var list = new List<FieldSelectionMode>();
            if (allowedModes.HasFlag(FieldSelectionModes.Fields))
                list.Add(FieldSelectionMode.Fields);
            if (allowedModes.HasFlag(FieldSelectionModes.ProjectProperties))
                list.Add(FieldSelectionMode.ProjectProperties);
            if (allowedModes.HasFlag(FieldSelectionModes.Options))
                list.Add(FieldSelectionMode.Options);
            if (allowedModes.HasFlag(FieldSelectionModes.InputFiles))
                list.Add(FieldSelectionMode.InputFiles);
            if (allowedModes.HasFlag(FieldSelectionModes.GenerationStrategyData))
                list.Add(FieldSelectionMode.GenerationStrategyData);

            if (allowedModes.HasFlag(FieldSelectionModes.Vault))
                list.Add(FieldSelectionMode.Vault);

            return list.ToArray();
        }

        public static void ApplyGenerationStrategyDataElementsToTextBox(IEnumerable<FlowBloxGenerationStrategyPlaceholderElement> generationStrategyDataElements, ITextBoxLike textBox)
        {
            var defs = (generationStrategyDataElements ?? Enumerable.Empty<FlowBloxGenerationStrategyPlaceholderElement>())
                .Select(x => x?.Placeholder)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!);

            ApplyFieldDefinitionsToTextBox(defs, textBox);
        }

        public static void ApplyInputFileElementsToTextBox(IEnumerable<FlowBloxInputFilePlaceholderElement> inputFileElements, ITextBoxLike textBox)
        {
            var defs = (inputFileElements ?? Enumerable.Empty<FlowBloxInputFilePlaceholderElement>())
                .Select(x => x?.Placeholder)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!);

            ApplyFieldDefinitionsToTextBox(defs, textBox);
        }

        public static void ApplyOptionElementsToTextBox(IEnumerable<OptionElement> optionElements, ITextBoxLike textBox)
        {
            var defs = (optionElements ?? Enumerable.Empty<OptionElement>())
                .Select(o => o.Name)
                .Select(name => FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Options, name));

            ApplyFieldDefinitionsToTextBox(defs, textBox);
        }

        public static void ApplyProjectPropertyElementsToTextBox(IEnumerable<FlowBloxProjectPropertyElement> projectPropertyElements, ITextBoxLike textBox)
        {
            var defs = (projectPropertyElements ?? Enumerable.Empty<FlowBloxProjectPropertyElement>())
                .Select(p => p?.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, key));

            ApplyFieldDefinitionsToTextBox(defs, textBox);
        }

        public static void ApplyFieldElementsToTextBox(IEnumerable<FieldElement> fieldElements, ITextBoxLike textBox)
        {
            var defs = fieldElements.Select(f => f.FullyQualifiedName);
            ApplyFieldDefinitionsToTextBox(defs, textBox);
        }

        public static void ApplyFieldDefinitionsToTextBox(IEnumerable<string> fieldDefinitions, ITextBoxLike textBox)
        {
            foreach (var fieldDefinition in fieldDefinitions)
            {
                var selectionStart = textBox.SelectionStart;
                var selectionLength = textBox.SelectionLength;

                if (selectionLength > 0)
                {
                    textBox.Text = textBox.Text.Remove(selectionStart, selectionLength);
                    textBox.Text = textBox.Text.Insert(selectionStart, fieldDefinition);
                }
                else
                {
                    textBox.Text = textBox.Text.Insert(selectionStart, fieldDefinition);
                }

                // Set caret to the end of inserted text
                textBox.SelectionStart = selectionStart + fieldDefinition.Length;
                textBox.SelectionLength = 0;
            }
        }

        /// <summary>
        /// Opens the toolbox dialog to select a snippet and inserts it into the TextBox.
        /// </summary>
        public static void ShowToolboxDialog(ITextBoxLike textBox, string? toolboxCategory, Window owner)
        {
            var dialog = new ToolboxWindow(true, toolboxCategory)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (dialog.ShowDialog() == true)
            {
                textBox.Text = dialog.SelectedToolboxElement.Content;
            }
        }

        /// <summary>
        /// Opens the toolbox dialog for editing a regular expression inside a selected parameter (%%param%%).
        /// </summary>
        public static void ShowEditValueDialogForRegex(ITextBoxLike textBox, string parameterLabel, Window window)
        {
            string parameterName = GetSelectedParameter(textBox);
            if (!string.IsNullOrEmpty(parameterName))
            {
                var dialogService = FlowBloxServiceLocator.Instance.GetService<IDialogService>();
                var editValueResult = dialogService.InvokeEditValue(new EditValueRequest()
                {
                    IsRegex = true,
                    IsMultiline = false,
                    ParameterName = parameterName,
                    EditMode = EditMode.Developer
                }, window);

                if (editValueResult.Success)
                {
                    string regexValue = editValueResult.IsMaskedRegexString
                        ? RegexUtil.EscapeRegexValue(editValueResult.Value)
                        : editValueResult.Value;

                    string updatedText = textBox.Text.Remove(textBox.SelectionStart, 0);
                    updatedText = updatedText.Insert(textBox.SelectionStart, regexValue);
                    textBox.Text = updatedText;
                    textBox.SelectionStart += regexValue.Length;
                }
            }
        }

        /// <summary>
        /// Registers a click handler on the TextBox to detect and edit regex placeholders (%%parameter%%).
        /// </summary>
        public static void RegisterRegexOnParameterSelectedAction(ITextBoxLike textBox, Window window)
        {
            textBox.AddClickHandler((sender, args) =>
            {
                string parameterName = GetSelectedParameter(textBox);
                if (!string.IsNullOrEmpty(parameterName))
                {
                    ShowEditValueDialogForRegex(textBox, parameterName, window);
                    args.Handled = true;
                }
            });
        }

        /// <summary>
        /// Registers a click handler for field selection insertion when a parameter is selected.
        /// </summary>
        public static void RegisterOnParameterSelectedAction(object target, ITextBoxLike textBox, Window window)
        {
            textBox.AddClickHandler((sender, args) =>
            {
                string parameterName = GetSelectedParameter(textBox);
                if (!string.IsNullOrEmpty(parameterName))
                {
                    var dialogService = FlowBloxServiceLocator.Instance.GetService<IDialogService>();
                    var insertTextOrFieldResult = dialogService.InvokeInsertTextOrField(target as BaseFlowBlock, parameterName, window);

                    if (insertTextOrFieldResult.Success)
                    {
                        string insertedValue = insertTextOrFieldResult.InsertedValue;
                        var parameterNameIndex = textBox.Text.IndexOf(parameterName);
                        textBox.Text = textBox.Text.Replace(parameterName, insertedValue);
                        if (!string.IsNullOrEmpty(insertedValue))
                            textBox.SelectionStart = parameterNameIndex + insertedValue.Length;
                        else
                            textBox.SelectionStart = parameterNameIndex;

                        if (insertTextOrFieldResult.SelectedField != null)
                        {
                            FlowBlockHelper.ApplyFieldSelectionRequiredOption(target, [insertTextOrFieldResult.SelectedField], insertTextOrFieldResult.IsSelectedFieldRequired);
                        }
                    }
                    args.Handled = true;
                }
            });
        }

        /// <summary>
        /// Extracts the selected %%parameter%% placeholder in the TextBox, if any.
        /// </summary>
        public static string GetSelectedParameter(ITextBoxLike textBox)
        {
            var regex = new Regex("%%.*?%%");
            var matches = regex.Matches(textBox.Text);

            foreach (Match match in matches)
            {
                if (textBox.SelectionStart >= match.Index &&
                    textBox.SelectionStart <= match.Index + match.Length)
                {
                    return match.Value;
                }
            }

            return string.Empty;
        }
    }
}
