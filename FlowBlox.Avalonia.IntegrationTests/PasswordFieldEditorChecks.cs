using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckPasswordFieldEditorChangesAsync(Window window, StackPanel content, Action<bool, string> check)
    {
        content.Children.Clear();
        foreach (var initialValue in new[] { null, string.Empty, "password-editor-check" })
        {
            var field = new FieldElement(FieldTypes.Text)
            {
                Name = "PasswordCheck",
                UserField = true,
                UserFieldType = UserFieldTypes.Input,
                IsPassword = true,
                StringValue = initialValue
            };
            var valueChanges = 0;
            field.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FieldElement.StringValue))
                    valueChanges++;
            };
            var context = new PropertyControlBindingContext(field);
            var model = new PropertyControlResolver(window).Resolve(
                typeof(FieldElement).GetProperty(nameof(FieldElement.StringValue)), field, false, null, context);
            model.AllowHasChangesUpdate = true;
            content.Children.Add(model.Control);
            await Task.Delay(100);
            window.UpdateLayout();
            check(!model.HasChanges && valueChanges == 0,
                $"Opening a password field does not write to the model (empty: {string.IsNullOrEmpty(initialValue)}, changes: {valueChanges})");
            var editor = MenuVisuals<PasswordValueEditor>(model.Control).Single();
            ((ToggleButton)editor.FindName("MaskButton")).IsChecked = false;
            await Task.Delay(50);
            check(!model.HasChanges && valueChanges == 0,
                "Revealing a password leaves property change tracking clean");
            content.Children.Remove(model.Control);
            content.Children.Add(model.Control);
            await Task.Delay(100);
            check(!model.HasChanges && valueChanges == 0,
                "Reopening a password editor leaves property change tracking clean");
            var refreshedEditor = MenuVisuals<PasswordValueEditor>(model.Control).Single();
            ((PasswordBox)refreshedEditor.FindName("MaskedEditor")).Password = "edited-password-check";
            check(model.HasChanges && field.StringValue == "edited-password-check",
                "Editing a password still updates the model and reports unsaved changes");
            content.Children.Remove(model.Control);
        }
    }
}
