using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Listly.Models;
using Listly.Services;

namespace Listly.ViewModels;

public sealed class SearchViewModel : INotifyPropertyChanged
{
    private readonly SearchEngine _engine;
    private CancellationTokenSource? _cts;

    private string _query = string.Empty;
    private int _selectedIndex = -1;
    private string _status = string.Empty;

    public SearchViewModel(SearchEngine engine)
    {
        _engine = engine;
    }

    public ObservableCollection<SearchResult> Results { get; } = new();

    public string Query
    {
        get => _query;
        set
        {
            if (_query == value)
                return;

            _query = value;
            OnPropertyChanged();
            _ = SearchAsync();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
        }
    }

    public SearchResult? SelectedResult =>
        _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex] : null;

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;

        int next = _selectedIndex + delta;
        if (next < 0)
            next = Results.Count - 1;
        else if (next >= Results.Count)
            next = 0;

        SelectedIndex = next;
    }

    /// <summary>Populates the list without a query (recent / frequent items).</summary>
    public void ShowInitial()
    {
        var results = _engine.Search(string.Empty);
        Replace(results);
    }

    /// <summary>Resets the query and clears results without launching a search.</summary>
    public void Clear()
    {
        _cts?.Cancel();
        _query = string.Empty;
        OnPropertyChanged(nameof(Query));
        Results.Clear();
        SelectedIndex = -1;
    }

    private async Task SearchAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        var query = _query;

        try
        {
            await Task.Delay(90, token);
            var results = await Task.Run(() => _engine.Search(query), token);
            if (token.IsCancellationRequested)
                return;

            Replace(results);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query.
        }
    }

    private void Replace(List<SearchResult> results)
    {
        Results.Clear();
        foreach (var result in results)
            Results.Add(result);

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
