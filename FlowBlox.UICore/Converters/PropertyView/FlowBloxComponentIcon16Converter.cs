using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Utilities;
using SkiaSharp;
using System.Globalization;
using System.Windows.Data;

namespace FlowBlox.UICore.Converters.PropertyView
{
    public sealed class FlowBloxComponentIcon16Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var icon = value as SKImage ?? FlowBloxComponentHelper.GetIcon16(value);
            if (icon == null)
                return null;

            return SkiaWpfImageHelper.ConvertToImageSource(icon);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
