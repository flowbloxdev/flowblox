using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Interfaces;

namespace FlowBlox.UICore.Factory.Adapter
{
    /// <summary>
    /// Replaces the whole value when a password or selection-only editor inserts a reference.
    /// These editors do not expose a text caret to the field selection dialog.
    /// </summary>
    public sealed class PropertyTextBoxAdapter : ITextBoxLike
    {
        private readonly object _target;
        private readonly PropertyInfo _property;
        private readonly FrameworkElement _editor;

        public PropertyTextBoxAdapter(object target, PropertyInfo property, FrameworkElement editor)
        {
            _target = target;
            _property = property;
            _editor = editor;
        }

        public string Text
        {
            get => _property.GetValue(_target) as string ?? string.Empty;
            set
            {
                _property.SetValue(_target, value);
                if (_editor is PasswordBox passwordBox)
                    passwordBox.Password = value;
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }
        }

        public int SelectionStart { get => 0; set { } }
        public int SelectionLength { get => Text.Length; set { } }
        public void Focus() => _editor.Focus();
        public void AddClickHandler(MouseButtonEventHandler handler) => _editor.PreviewMouseLeftButtonUp += handler;
    }
}
