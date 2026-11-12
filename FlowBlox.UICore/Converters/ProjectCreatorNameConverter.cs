using System.Globalization;
using System.Windows.Data;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.UICore.Converters;

public sealed class ProjectCreatorNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not int userId)
            return string.Empty;

        var names = values[1] as IReadOnlyDictionary<int, string>;
        if (names != null && names.TryGetValue(userId, out var name))
            return name;

        var format = FlowBloxResourceUtil.GetLocalizedString("Format_CreatedBy", typeof(Resources.PSProjectsWindow));
        return string.Format(culture, format, userId);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
