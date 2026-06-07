using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MyBricksPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;

    public MyBricksPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MyBricksPage(ApiClient api)
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
        }

        var bricks = await _api.GetOwnedBricksAsync();
        Loader.IsVisible = false;

        if (bricks is null)
        {
            ErrorLabel.Text = "Could not load your bricks.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        BricksList.ItemsSource = bricks
            .OrderBy(b => b.Name)
            .Select(b => new BrickItem
            {
                PartNum     = b.PartNum,
                ColorId     = b.ColorId ?? "",
                Name        = b.Name,
                PartImgUrl  = _api.ResolveImageUrl(b.PartImg),
                ColorName   = b.ColorName ?? b.ColorId ?? b.PartNum,
                HexColor    = b.HexColor,
                BricklinkId = b.BricklinkId,
                Stock       = b.Stock,
            })
            .ToList();
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
        _loaded = false; // reload list on return so stock changes from detail are reflected
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

    private sealed class BrickItem : INotifyPropertyChanged
    {
        public required string PartNum  { get; init; }
        public required string ColorId  { get; init; }
        public required string Name     { get; init; }
        public string? PartImgUrl       { get; init; }
        public string? ColorName        { get; init; }
        public string? HexColor         { get; init; }
        public string? BricklinkId      { get; init; }

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
