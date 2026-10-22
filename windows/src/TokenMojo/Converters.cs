using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TokenMojo;

/// <summary>Collapses an element whose bound value is null, false or an empty list, like the Mac panel's optional sections.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null or false => Visibility.Collapsed,
        ICollection { Count: 0 } => Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
