using System.Globalization;
using System.Windows.Data;

using Listly.Models;
using Listly.Services;

namespace Listly.Behaviors;

/// <summary>Formats a <see cref="ResultKind"/> as a short label for the results list.</summary>
public sealed class KindToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ResultKind.Application => Loc.T("kind.app"),
        ResultKind.File => Loc.T("kind.file"),
        ResultKind.Folder => Loc.T("kind.folder"),
        ResultKind.WebSearch => Loc.T("kind.web"),
        ResultKind.Command => Loc.T("kind.command"),
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
