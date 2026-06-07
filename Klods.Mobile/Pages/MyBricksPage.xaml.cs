using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MyBricksPage : ContentPage
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

    public MyBricksPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MyBricksPage(ApiClient api)
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

        var result = await _api.GetOwnedBricksAsync(search: _currentSearch, page: 0, pageSize: PageSize);
        Loader.IsVisible = false;

        if (result is null)
        {
            ErrorLabel.Text = "Could not load your bricks.\nCheck your connection and try again.";
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
            _items.Add(ToItem(b));

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_hasMore || _isLoadingMore) return;
        _isLoadingMore = true;

        var result = await _api.GetOwnedBricksAsync(search: _currentSearch, page: _currentPage + 1, pageSize: PageSize);
        if (result is not null)
        {
            _currentPage++;
            _hasMore = result.HasMore;
            foreach (var b in result.Items)
                _items.Add(ToItem(b));
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

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        item.Stock++;
        _ = _api.UpdateLooseBrickStockAsync(item.PartNum, item.ColorId, item.Stock);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        if (item.Stock <= 0) return;
        item.Stock--;
        _ = _api.UpdateLooseBrickStockAsync(item.PartNum, item.ColorId, item.Stock);
    }

    private async void OnBrickTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        _loaded = false;
        await Navigation.PushAsync(new GlobalBrickDetailPage(
            new GlobalBrickDetailPage.BrickData(
                PartNum:     item.PartNum,
                ColorId:     item.ColorId,
                Name:        item.Name,
                PartImgUrl:  item.PartImgUrl,
                ColorName:   item.ColorName,
                HexColor:    item.HexColor,
                BricklinkId: item.BricklinkId,
                Stock:       item.Stock),
            _api));
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

    private BrickItem ToItem(ApiClient.MyBrickDto b) => new()
    {
        PartNum     = b.PartNum,
        ColorId     = b.ColorId ?? "",
        Name        = b.Name,
        PartImgUrl  = _api.ResolveImageUrl(b.PartImg),
        ColorName   = b.ColorName ?? b.ColorId ?? b.PartNum,
        HexColor    = b.HexColor,
        BricklinkId = b.BricklinkId,
        Stock       = b.Stock,
    };

    private sealed class BrickItem : INotifyPropertyChanged
    {
        public required string PartNum  { get; init; }
        public required string ColorId  { get; init; }
        public required string Name     { get; init; }
        public string? ColorName        { get; init; }
        public string? HexColor         { get; init; }
        public string? BricklinkId      { get; init; }
        public string? PartImgUrl       { get; init; }

        private int _stock;
        public int Stock
        {
            get => _stock;
            set
            {
                if (_stock == value) return;
                _stock = value;
                Notify();
            }
        }

        public SolidColorBrush SwatchBrush => new(
            HexColor is { Length: > 0 } h
                ? Color.FromArgb(h.StartsWith('#') ? h : "#" + h)
                : Colors.Gray);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
