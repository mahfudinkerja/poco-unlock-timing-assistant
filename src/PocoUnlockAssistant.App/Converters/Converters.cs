using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PocoUnlockAssistant.App.ViewModels;

namespace PocoUnlockAssistant.App.Converters;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class StatusKindToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze("#FF37D67A");
    private static readonly Brush Warn = Freeze("#FFFFB020");
    private static readonly Brush Error = Freeze("#FFFF5A50");
    private static readonly Brush Idle = Freeze("#FF8B8B96");
    private static readonly Brush Accent = Freeze("#FFFF4A1C");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusKind kind
            ? kind switch
            {
                StatusKind.Ok => Ok,
                StatusKind.Warn => Warn,
                StatusKind.Error => Error,
                StatusKind.Accent => Accent,
                _ => Idle,
            }
            : Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush Ok = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF37D67A"));
    private static readonly Brush Idle = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B8B96"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Ok : Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
