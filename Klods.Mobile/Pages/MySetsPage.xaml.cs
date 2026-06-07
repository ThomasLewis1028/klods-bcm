using System.Collections.ObjectModel;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MySetsPage : ContentPage
{
    private const int PageSize = 50;

    private readonly ApiClient _api;
    private readonly ObservableCollection<SetItem> _items = [];
    private readonly List<ApiClient.MyOwnedSetDto> _rawSets = [];

    private bool _loaded;
    private bool _isGridView;
    private bool _hasMore;
    private bool _isLoadingMore;
    private int _currentPage;
    private string? _currentSearch;
    private CancellationTokenSource? _searchCts;

    public MySetsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MySetsPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        SetsList.ItemsSource = _items;
        GridList.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
            await LoadAsync(firstLoad: true);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync(firstLoad: false);
        Refresher.IsRefreshing = false;
        GridRefresher.IsRefreshing = false;
    }

    private async void OnRetryClicked(object? sender, EventArgs e) =>
        await LoadAsync(firstLoad: true);

    private async Task LoadAsync(bool firstLoad)
    {
        if (firstLoad)
        {
            Loader.IsVisible = true;
            ErrorView.IsVisible = false;
            Refresher.IsVisible = false;
            GridRefresher.IsVisible = false;
        }

        var result = await _api.GetMyOwnedSetsAsync(search: _currentSearch, page: 0, pageSize: PageSize);
        Loader.IsVisible = false;

        if (result is null)
        {
            ErrorLabel.Text = "Could not load your sets.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            GridRefresher.IsVisible = false;
            return;
        }

        _loaded = true;
        _currentPage = 0;
        _hasMore = result.HasMore;
        ErrorView.IsVisible = false;

        _items.Clear();
        _rawSets.Clear();
        foreach (var s in result.Items)
        {
            _rawSets.Add(s);
            _items.Add(ToItem(s));
        }

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_hasMore || _isLoadingMore) return;
        _isLoadingMore = true;

        var result = await _api.GetMyOwnedSetsAsync(search: _currentSearch, page: _currentPage + 1, pageSize: PageSize);
        if (result is not null)
        {
            _currentPage++;
            _hasMore = result.HasMore;
            foreach (var s in result.Items)
            {
                _rawSets.Add(s);
                _items.Add(ToItem(s));
            }
        }

        _isLoadingMore = false;
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        try
        {
            await Task.Delay(350, cts.Token);
            _currentSearch = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue.Trim();
            await LoadAsync(firstLoad: true);
        }
        catch (TaskCanceledException) { }
    }

    private async void OnSetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SetItem item) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        var raw = _rawSets.FirstOrDefault(s => s.SetId == item.SetId);
        if (raw is null) return;
        _loaded = false;
        await Navigation.PushAsync(new SetDetailPage(raw, _api));
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        _loaded = false;
        await Navigation.PushAsync(new ImportSetPage(_api));
    }

    private void OnListViewClicked(object? sender, EventArgs e)
    {
        if (_isGridView)
            SetView(isGrid: false);
    }

    private void OnGridViewClicked(object? sender, EventArgs e)
    {
        if (!_isGridView)
            SetView(isGrid: true);
    }

    private void SetView(bool isGrid)
    {
        _isGridView = isGrid;
        Refresher.IsVisible = !isGrid && _loaded;
        GridRefresher.IsVisible = isGrid && _loaded;
        var primary = (Color)Application.Current!.Resources["Primary"];
        var inactive = (Color)Application.Current!.Resources["Gray400"];
        ListViewBtn.TextColor = isGrid ? inactive : primary;
        GridViewBtn.TextColor = isGrid ? primary : inactive;
    }

    private SetItem ToItem(ApiClient.MyOwnedSetDto s) => new(
        SetId: s.SetId,
        Name: s.Name,
        SetImg: _api.ResolveImageUrl(s.SetImg),
        NumBricks: s.NumBricks,
        ReleaseYear: s.ReleaseYear,
        ThemeName: s.ThemeName,
        Copies: s.Instances.Count,
        TotalMissing: s.Instances.Sum(i => i.MissingPieceCount));

    private sealed record SetItem(
        string SetId, string Name, string? SetImg, int NumBricks,
        int ReleaseYear, string? ThemeName, int Copies, int TotalMissing)
    {
        public string SubtitleLine1 => string.Join(" · ",
            new[] { SetId, ThemeName, ReleaseYear.ToString() }
                .Where(s => !string.IsNullOrEmpty(s)));
        public string PiecesLabel => $"{NumBricks:N0} pieces";
        public string CopiesLabel => Copies == 1 ? "1 copy" : $"{Copies} copies";
        public string MissingLabel => $"⚠ {TotalMissing:N0} missing";
        public bool HasMissing => TotalMissing > 0;
    }
}
