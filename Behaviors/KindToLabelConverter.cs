using System.Globalization;
using System.Windows.Data;

using Listly.Models;

namespace Listly.Behaviors;

/// <summary>Formats a <see cref="ResultKind"/> as a short label for the results list.</summary>
public sealed class KindToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ResultKind.Application => "App",
        ResultKind.File => "File",
        ResultKind.Folder => "Folder",
        ResultKind.WebSearch => "Web",
        ResultKind.Command => "Command",
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
