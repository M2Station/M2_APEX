using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

using Listly.Models;

namespace Listly.Behaviors;

/// <summary>
/// Attached property that renders a <see cref="SearchResult.Title"/> into a TextBlock,
/// bolding and accent-colouring the characters that matched the query.
/// </summary>
public static class Highlight
{
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.RegisterAttached(
            "Result",
            typeof(SearchResult),
            typeof(Highlight),
            new PropertyMetadata(null, OnResultChanged));

    public static void SetResult(DependencyObject element, SearchResult? value) =>
        element.SetValue(ResultProperty, value);

    public static SearchResult? GetResult(DependencyObject element) =>
        (SearchResult?)element.GetValue(ResultProperty);

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
            return;

        textBlock.Inlines.Clear();

        if (e.NewValue is not SearchResult result)
            return;

        var title = result.Title;
        var matched = result.MatchedIndices;

        if (matched is null || matched.Count == 0)
        {
            textBlock.Inlines.Add(new Run(title));
            return;
        }

        var matchedSet = new HashSet<int>(matched);
        var buffer = new System.Text.StringBuilder();
        bool bufferMatched = false;

        void Flush()
        {
            if (buffer.Length == 0)
                return;

            var run = new Run(buffer.ToString());
            if (bufferMatched)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "ThemeAccentBrush");
                run.FontWeight = FontWeights.SemiBold;
            }

            textBlock.Inlines.Add(run);
            buffer.Clear();
        }

        for (int i = 0; i < title.Length; i++)
        {
            bool isMatch = matchedSet.Contains(i);
            if (i > 0 && isMatch != bufferMatched)
                Flush();

            bufferMatched = isMatch;
            buffer.Append(title[i]);
        }

        Flush();
    }
}
