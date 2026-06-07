using System.Collections.ObjectModel;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class BricksPage : ContentPage
{
    private const int PageSize = 50;

    private readonly ApiClient _api;
    private readonly ObservableCollection<BrickItem> _items = [];

    private bool _loaded;
    private bool _isGridView;
    private bool _hasMore;
    private bool _isLoadingMore;
    private int _currentPage;
    private string? _currentSearch;
    private CancellationTokenSource? _searchCts;

    public BricksPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public BricksPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        BricksList.ItemsSource = _items;
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

        var result = await _api.GetBrickCatalogViewAsync(search: _currentSearch, page: 0, pageSize: PageSize);
        Loader.IsVisible = false;

        if (result is null)
        {
            ErrorLabel.Text = "Could not load the brick catalog.\nCheck your connection and try again.";
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
        foreach (var b in result.Items)
            _items.Add(new BrickItem(
                PartNum: b.PartNum,
                Name: b.Name,
                PartImgUrl: _api.ResolveImageUrl(b.PartImg),
                ColorName: b.ColorName ?? "No colour",
                HexColor: b.HexColor,
                TotalStock: b.TotalStock,
                TotalNeeded: b.TotalNeeded,
                SetCount: b.SetCount));

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_hasMore || _isLoadingMore) return;
        _isLoadingMore = true;

        var result = await _api.GetBrickCatalogViewAsync(search: _currentSearch, page: _currentPage + 1, pageSize: PageSize);
        if (result is not null)
        {
            _currentPage++;
            _hasMore = result.HasMore;
            foreach (var b in result.Items)
                _items.Add(new BrickItem(
                    PartNum: b.PartNum,
                    Name: b.Name,
                    PartImgUrl: _api.ResolveImageUrl(b.PartImg),
                    ColorName: b.ColorName ?? "No colour",
                    HexColor: b.HexColor,
                    TotalStock: b.TotalStock,
                    TotalNeeded: b.TotalNeeded,
                    SetCount: b.SetCount));
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

    private sealed record BrickItem(
        string PartNum, string Name, string? PartImgUrl,
        string ColorName, string? HexColor,
        int TotalStock, int TotalNeeded, int SetCount)
    {
        public SolidColorBrush SwatchBrush =>
            HexColor is not null && Color.TryParse($"#{HexColor}", out var c)
                ? new SolidColorBrush(c)
                : new SolidColorBrush(Colors.Gray);

        public string StatsLabel
        {
            get
            {
                var parts = new List<string> { $"Stock: {TotalStock:N0}" };
                if (TotalNeeded > 0) parts.Add($"Needed: {TotalNeeded:N0}");
                return string.Join("  ·  ", parts);
            }
        }

        public string SetCountLabel => SetCount > 0 ? $"{SetCount} set{(SetCount != 1 ? "s" : "")}" : string.Empty;
    }
}
