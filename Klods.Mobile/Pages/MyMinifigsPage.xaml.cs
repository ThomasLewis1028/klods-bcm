using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MyMinifigsPage : ContentPage
{
    private const int PageSize = 50;

    private readonly ApiClient _api;
    private readonly ObservableCollection<MinifigItem> _items = [];

    private bool _loaded;
    private bool _isGridView;
    private bool _hasMore;
    private bool _isLoadingMore;
    private int _currentPage;
    private string? _currentSearch;
    private CancellationTokenSource? _searchCts;

    public MyMinifigsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MyMinifigsPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        MinifigsList.ItemsSource = _items;
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

        var result = await _api.GetMyMinifigsAsync(search: _currentSearch, page: 0, pageSize: PageSize);
        Loader.IsVisible = false;

        if (result is null)
        {
            ErrorLabel.Text = "Could not load your minifigs.\nCheck your connection and try again.";
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
        foreach (var m in result.Items)
            _items.Add(ToItem(m));

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_hasMore || _isLoadingMore) return;
        _isLoadingMore = true;

        var result = await _api.GetMyMinifigsAsync(search: _currentSearch, page: _currentPage + 1, pageSize: PageSize);
        if (result is not null)
        {
            _currentPage++;
            _hasMore = result.HasMore;
            foreach (var m in result.Items)
                _items.Add(ToItem(m));
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

    private async void OnMinifigTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: MinifigItem item }) return;
        _loaded = false;
        await Navigation.PushAsync(new MinifigDetailPage(
            new ApiClient.MyMinifigDto(
                MinifigId:    item.MinifigId,
                MinifigName:  item.Name,
                ImgUrl:       item.ImgUrl,
                Stock:        item.Stock,
                UserNeeded:   item.UserNeeded,
                UserSetCount: item.UserSetCount,
                PartCount:    item.PartCount),
            _api));
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: MinifigItem item }) return;
        item.Stock++;
        _ = _api.UpdateMinifigStockAsync(item.MinifigId, item.Stock);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: MinifigItem item }) return;
        if (item.Stock <= 0) return;
        item.Stock--;
        _ = _api.UpdateMinifigStockAsync(item.MinifigId, item.Stock);
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

    private static MinifigItem ToItem(ApiClient.MyMinifigDto m) => new()
    {
        MinifigId    = m.MinifigId,
        Name         = m.MinifigName,
        ImgUrl       = m.ImgUrl,
        Stock        = m.Stock,
        UserNeeded   = m.UserNeeded,
        UserSetCount = m.UserSetCount,
        PartCount    = m.PartCount,
    };

    private sealed class MinifigItem : INotifyPropertyChanged
    {
        public required string MinifigId    { get; init; }
        public required string Name         { get; init; }
        public string? ImgUrl               { get; init; }
        public required int UserNeeded      { get; init; }
        public required int UserSetCount    { get; init; }
        public required int PartCount       { get; init; }

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

        public string SubtitleLine => string.Join(" · ",
            new[] { MinifigId, PartCount > 0 ? $"{PartCount} parts" : null, UserSetCount > 0 ? $"{UserSetCount} sets" : null }
                .Where(s => !string.IsNullOrEmpty(s)));

        public string NeededLabel => $"⚠ {UserNeeded} needed";
        public bool HasNeeded => UserNeeded > 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
