using System.Windows;
using System.Windows.Data;
using FlowBlox.Core.Util.FlowBlocks;

namespace FlowBlox.UICore.Utilities
{
    /// <summary>
    /// Reports edits after a binding writes back to the model. Loading or refreshing
    /// an editor's displayed value does not report a change.
    /// </summary>
    public static class PropertyEditorBinding
    {
        public static void Bind(
            FrameworkElement editor,
            DependencyProperty editorProperty,
            Binding binding,
            object target,
            string propertyName)
        {
            // Suggestion editors share their binding with a separate selector.
            // A binding becomes immutable after its first attachment.
            if (!binding.NotifyOnSourceUpdated)
                binding.NotifyOnSourceUpdated = true;

            editor.SourceUpdated += (_, args) =>
            {
                if (ReferenceEquals(args.TargetObject, editor) && args.Property == editorProperty)
                    FlowBloxComponentHelper.RaisePropertyChanged(target, propertyName);
            };
            editor.SetBinding(editorProperty, binding);
        }
    }
}
