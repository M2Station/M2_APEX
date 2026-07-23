namespace Listly.Services;

/// <summary>
/// Fuzzy subsequence matcher with scoring, tuned for file names and app names.
/// Rewards matches at word boundaries, consecutive runs, prefixes and acronyms
/// (e.g. "vsc" -> "Visual Studio Code").
/// </summary>
public static class FuzzyMatcher
{
    public const double NoMatch = double.NegativeInfinity;

    /// <summary>
    /// Scores <paramref name="text"/> against a pre-lowercased <paramref name="queryLower"/>.
    /// Returns <see cref="NoMatch"/> when the query is not a subsequence of the text.
    /// Allocation-free so it is cheap enough to run across the whole index per keystroke.
    /// </summary>
    public static double Score(string queryLower, string text) => Score(queryLower, text.AsSpan());

    /// <summary>
    /// Span overload used by the file-search hot path so it can score a name slice of the full path
    /// without allocating a substring.
    /// </summary>
    public static double Score(string queryLower, ReadOnlySpan<char> text)
    {
        int qLen = queryLower.Length;
        int tLen = text.Length;

        if (qLen == 0)
            return 0;
        if (qLen > tLen)
            return NoMatch;

        double greedy = GreedyScore(queryLower, text, qLen, tLen);
        double acronym = AcronymScore(queryLower, text, tLen);

        double best = Math.Max(greedy, acronym);
        return best;
    }

    // ASCII fast path for lower-casing one char (the hot loop); falls back to Unicode-correct folding.
    private static char ToLower(char c)
    {
        if ((uint)(c - 'A') <= 'Z' - 'A')
            return (char)(c | 0x20);
        return c < 0x80 ? c : char.ToLowerInvariant(c);
    }

    private static double GreedyScore(string queryLower, ReadOnlySpan<char> text, int qLen, int tLen)
    {
        int qi = 0;
        double score = 0;
        int prevMatch = -2;
        int firstMatch = -1;
        int consecutive = 0;

        for (int ti = 0; ti < tLen && qi < qLen; ti++)
        {
            if (ToLower(text[ti]) != queryLower[qi])
                continue;

            double s = 1.0;

            if (IsBoundary(text, ti))
                s += 0.9;

            if (ti == prevMatch + 1)
            {
                consecutive++;
                s += 0.7 + consecutive * 0.15;
            }
            else
            {
                consecutive = 0;
            }

            if (firstMatch < 0)
                firstMatch = ti;

            score += s;
            prevMatch = ti;
            qi++;
        }

        if (qi < qLen)
            return NoMatch;

        // Earlier and tighter matches rank higher.
        score -= firstMatch * 0.06;
        score -= (tLen - qLen) * 0.02;

        if (tLen >= qLen &&
            text.Slice(0, qLen).Equals(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 2.5; // prefix match
            if (tLen == qLen)
                score += 5.0; // exact match
        }

        return score;
    }

    /// <summary>
    /// Matches the query against the initials of each word (acronym search).
    /// </summary>
    private static double AcronymScore(string queryLower, ReadOnlySpan<char> text, int tLen)
    {
        int qi = 0;
        int matched = 0;
        int firstBoundary = -1;

        for (int ti = 0; ti < tLen && qi < queryLower.Length; ti++)
        {
            if (!IsBoundary(text, ti))
                continue;

            if (ToLower(text[ti]) == queryLower[qi])
            {
                if (firstBoundary < 0)
                    firstBoundary = ti;
                qi++;
                matched++;
            }
            else if (qi > 0)
            {
                // Initials must be consecutive words to count as an acronym.
                return NoMatch;
            }
        }

        if (qi < queryLower.Length)
            return NoMatch;

        double score = 3.0 + matched * 1.2;
        if (firstBoundary == 0)
            score += 1.5;
        return score;
    }

    private static bool IsBoundary(ReadOnlySpan<char> text, int i)
    {
        if (i <= 0)
            return true;

        char prev = text[i - 1];
        char cur = text[i];

        if (prev is ' ' or '_' or '-' or '.' or '/' or '\\' or '(' or '[')
            return true;

        // ASCII fast path (the common case) avoids the Unicode-category lookups of char.IsUpper/…
        if (cur < 0x80 && prev < 0x80)
        {
            if (IsAsciiUpper(cur) && IsAsciiLower(prev))
                return true;
            if (IsAsciiDigit(cur) && !IsAsciiDigit(prev))
                return true;
            return false;
        }

        if (char.IsUpper(cur) && char.IsLower(prev))
            return true;
        if (char.IsDigit(cur) && !char.IsDigit(prev))
            return true;

        return false;
    }

    private static bool IsAsciiUpper(char c) => (uint)(c - 'A') <= 'Z' - 'A';
    private static bool IsAsciiLower(char c) => (uint)(c - 'a') <= 'z' - 'a';
    private static bool IsAsciiDigit(char c) => (uint)(c - '0') <= '9' - '0';

    /// <summary>
    /// Computes the matched character indices for highlighting. Only called for the
    /// small set of results that are actually displayed, so it can allocate freely.
    /// </summary>
    public static IReadOnlyList<int>? GetMatchedIndices(string queryLower, string text)
    {
        if (queryLower.Length == 0)
            return null;

        var indices = new List<int>(queryLower.Length);
        int qi = 0;

        for (int ti = 0; ti < text.Length && qi < queryLower.Length; ti++)
        {
            if (char.ToLowerInvariant(text[ti]) == queryLower[qi])
            {
                indices.Add(ti);
                qi++;
            }
        }

        return qi == queryLower.Length ? indices : null;
    }
}
