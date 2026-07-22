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
        if (d is TextBlock textBlock)
            RenderHighlighted(textBlock, (e.NewValue as SearchResult)?.Title, (e.NewValue as SearchResult)?.MatchedIndices);
    }

    // --- Generic text + matched-indices highlighting (e.g. M2_Commander's live filter) ---------

    public static readonly DependencyProperty MatchTextProperty =
        DependencyProperty.RegisterAttached(
            "MatchText", typeof(string), typeof(Highlight),
            new PropertyMetadata(null, OnMatchChanged));

    public static void SetMatchText(DependencyObject element, string? value) =>
        element.SetValue(MatchTextProperty, value);

    public static string? GetMatchText(DependencyObject element) =>
        (string?)element.GetValue(MatchTextProperty);

    public static readonly DependencyProperty MatchIndicesProperty =
        DependencyProperty.RegisterAttached(
            "MatchIndices", typeof(IReadOnlyList<int>), typeof(Highlight),
            new PropertyMetadata(null, OnMatchChanged));

    public static void SetMatchIndices(DependencyObject element, IReadOnlyList<int>? value) =>
        element.SetValue(MatchIndicesProperty, value);

    public static IReadOnlyList<int>? GetMatchIndices(DependencyObject element) =>
        (IReadOnlyList<int>?)element.GetValue(MatchIndicesProperty);

    private static void OnMatchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            RenderHighlighted(textBlock, GetMatchText(textBlock), GetMatchIndices(textBlock));
    }

    /// <summary>Renders <paramref name="text"/> into <paramref name="textBlock"/>, accent-bolding matched chars.</summary>
    private static void RenderHighlighted(TextBlock textBlock, string? text, IReadOnlyList<int>? matched)
    {
        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(text))
            return;

        if (matched is null || matched.Count == 0)
        {
            textBlock.Inlines.Add(new Run(text));
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

        for (int i = 0; i < text.Length; i++)
        {
            bool isMatch = matchedSet.Contains(i);
            if (i > 0 && isMatch != bufferMatched)
                Flush();

            bufferMatched = isMatch;
            buffer.Append(text[i]);
        }

        Flush();
    }
}
