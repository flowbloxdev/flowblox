using System.Globalization;
using System.Windows.Data;
using FlowBlox.Core.Util;

namespace FlowBlox.UICore.Converters;

public sealed class CompactCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CompactCountFormatter.Format(value is long count ? count : 0, culture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
