using AlUmmahNow.VisualImporterUno.Shell;
using Microsoft.UI.Xaml.Data;
using System;

namespace AlUmmahNow.VisualImporterUno.Converters;

/// <summary>
/// Formats a bound value with the resource string named by ConverterParameter, so a row from a shared model can say
/// "12 videos" / «١٢ فيديو» with the language's digits. Dates are written in the device's zone.
/// </summary>
public sealed class LocFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = parameter as string ?? "{0}";
        object? shown = value switch
        {
            DateTime d => Loc.When(d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d),
            DateTimeOffset o => Loc.When(o.LocalDateTime),
            long l when key.StartsWith("Bytes", StringComparison.Ordinal) => Loc.Bytes(l),
            _ => value,
        };
        return key.StartsWith("Bytes", StringComparison.Ordinal) ? shown ?? "" : Loc.Format(key, shown);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
