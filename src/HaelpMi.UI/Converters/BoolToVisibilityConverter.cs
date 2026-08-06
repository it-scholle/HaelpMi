using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HaelpMi.UI.Converters;

/// <summary>True/false to Visible/Collapsed, for the "NEU" badge (FR-24).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
