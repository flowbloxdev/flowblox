using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows.Media;

namespace FlowBlox.UICore.Factory.PropertyView.Colorizer
{
    public sealed class DefaultValueColorizer : DocumentColorizingTransformer
    {
        private readonly Func<string> _getDefaultValue;

        public DefaultValueColorizer(Func<string> getDefaultValue)
        {
            _getDefaultValue = getDefaultValue;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var defaultValue = _getDefaultValue();
            if (string.IsNullOrEmpty(defaultValue)
                || !string.Equals(CurrentContext.Document.Text, defaultValue, StringComparison.Ordinal))
                return;

            ChangeLinePart(line.Offset, line.EndOffset,
                element => element.TextRunProperties.SetForegroundBrush(Brushes.SlateGray));
        }
    }
}
