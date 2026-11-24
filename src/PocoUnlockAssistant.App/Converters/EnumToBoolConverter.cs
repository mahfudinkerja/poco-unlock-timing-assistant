using System.Globalization;
using System.Windows.Data;

namespace PocoUnlockAssistant.App.Converters;

/// <summary>Binds a radio button to one value of an enum property.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Enum.TryParse(enumType, parameter.ToString(), ignoreCase: true, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
