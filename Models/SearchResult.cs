namespace Listly.Models;

/// <summary>The category a <see cref="SearchResult"/> belongs to.</summary>
public enum ResultKind
{
    Application,
    File,
    Folder,
    WebSearch,
    Command
}

/// <summary>A single row shown in the search results list.</summary>
public sealed class SearchResult
{
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    /// <summary>Full path for files/folders/apps, URL for web, or command id.</summary>
    public required string Path { get; init; }

    public ResultKind Kind { get; init; }

    /// <summary>Segoe MDL2 Assets glyph shown on the left of the row.</summary>
    public string Glyph { get; init; } = "\uE8A5";

    /// <summary>Ranking score; higher is better.</summary>
    public double Score { get; set; }

    /// <summary>Indices into <see cref="Title"/> that matched the query.</summary>
    public IReadOnlyList<int>? MatchedIndices { get; set; }

    /// <summary>Optional argument (web query text, command payload, etc.).</summary>
    public string? Argument { get; init; }

    /// <summary>Custom activation logic (used by commands).</summary>
    public Action? Activate { get; init; }
}
