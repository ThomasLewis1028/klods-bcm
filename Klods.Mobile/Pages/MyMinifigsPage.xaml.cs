using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MyMinifigsPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;
    private bool _isGridView;

    public MyMinifigsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MyMinifigsPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
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

        var minifigs = await _api.GetMyMinifigsAsync();
        Loader.IsVisible = false;

        if (minifigs is null)
        {
            ErrorLabel.Text = "Could not load your minifigs.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            GridRefresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;

        var items = minifigs
            .OrderBy(m => m.MinifigName)
            .Select(m => new MinifigItem
            {
                MinifigId  = m.MinifigId,
                Name       = m.MinifigName,
                ImgUrl     = m.ImgUrl,
                Stock      = m.Stock,
                UserNeeded = m.UserNeeded,
                UserSetCount = m.UserSetCount,
                PartCount  = m.PartCount,
            })
            .ToList();

        MinifigsList.ItemsSource = items;
        GridList.ItemsSource = items;

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
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
