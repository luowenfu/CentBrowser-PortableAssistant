using System.Globalization;
using System.Windows.Data;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>宽度还没量出来时不要当成 0，否则文件名会被压没。</summary>
public sealed class PositiveWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && width > 0 ? width : double.PositiveInfinity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
