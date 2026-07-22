using System.Windows.Markup;

using Listly.Services;

namespace Listly.Behaviors;

/// <summary>
/// XAML markup extension for localized text, e.g. <c>Text="{b:Loc settings.appearance}"</c>.
/// Binds to <see cref="Loc.Strings"/> so the text updates live when the language changes.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Loc.Strings,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
