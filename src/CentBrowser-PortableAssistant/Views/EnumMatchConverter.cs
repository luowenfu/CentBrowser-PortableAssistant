using System.Globalization;
using System.Windows.Data;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>单选绑定到枚举。取消选中时不写回，避免把另一个选项刚写入的值清掉。</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name && Enum.TryParse(targetType, name, out var parsed)
            ? parsed
            : Binding.DoNothing;
}
