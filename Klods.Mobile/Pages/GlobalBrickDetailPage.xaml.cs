using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class GlobalBrickDetailPage : ContentPage
{
    public sealed record BrickData(
        string PartNum,
        string ColorId,
        string Name,
        string? PartImgUrl,
        string? ColorName,
        string? HexColor,
        string? BricklinkId,
        int Stock);

    private readonly BrickData _brick;
    private readonly ApiClient _api;
    private int _stock;
    private bool _setsLoaded;

    public GlobalBrickDetailPage(BrickData brick, ApiClient api)
    {
        InitializeComponent();
        _brick = brick;
        _api = api;
        _stock = brick.Stock;

        if (brick.PartImgUrl is not null)
        {
            BrickImage.Source = brick.PartImgUrl;
            PlaceholderIcon.IsVisible = false;
        }

        PartNumLabel.Text = $"{brick.PartNum} · {brick.ColorId}";
        NameLabel.Text = brick.Name;
        ColorNameLabel.Text = brick.ColorName ?? brick.ColorId;
        ColorSwatch.Fill = new SolidColorBrush(
            brick.HexColor is { Length: > 0 } h
                ? Color.FromArgb(h.StartsWith('#') ? h : "#" + h)
                : Colors.Gray);

        StockLabel.Text = _stock.ToString();
        BricklinkButton.IsVisible = brick.BricklinkId is not null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_setsLoaded)
            await LoadSetsAsync();
    }

    private async Task LoadSetsAsync()
    {
        SetsLoader.IsVisible = true;
        SetsErrorLabel.IsVisible = false;

        var sets = await _api.GetBrickSetsAsync(_brick.PartNum, _brick.ColorId);
        SetsLoader.IsVisible = false;

        if (sets is null)
        {
            SetsErrorLabel.Text = "Could not load sets.";
            SetsErrorLabel.IsVisible = true;
            return;
        }

        if (sets.Length == 0)
        {
            SetsErrorLabel.Text = "This brick doesn't appear in any catalogued set.";
            SetsErrorLabel.IsVisible = true;
            return;
        }

        _setsLoaded = true;
        BindableLayout.SetItemsSource(SetsLayout, sets.Select(s => new SetItem
        {
            SetName      = s.SetName,
            SetImgUrl    = _api.ResolveImageUrl(s.SetImg),
            RequiresLabel = s.CopiesOwned > 1
                ? $"Requires {s.BrickCount} · {s.CopiesOwned} copies owned"
                : $"Requires {s.BrickCount}",
        }).ToList());
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        _stock++;
        StockLabel.Text = _stock.ToString();
        _ = _api.UpdateLooseBrickStockAsync(_brick.PartNum, _brick.ColorId, _stock);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (_stock <= 0) return;
        _stock--;
        StockLabel.Text = _stock.ToString();
        _ = _api.UpdateLooseBrickStockAsync(_brick.PartNum, _brick.ColorId, _stock);
    }

    private async void OnBricklinkClicked(object? sender, EventArgs e)
    {
        if (_brick.BricklinkId is null) return;
        await Launcher.OpenAsync($"https://www.bricklink.com/v2/catalog/catalogitem.page?P={Uri.EscapeDataString(_brick.BricklinkId)}");
    }

    private async void OnImageTapped(object? sender, TappedEventArgs e)
    {
        if (_brick.PartImgUrl is null) return;
        await Navigation.PushModalAsync(new ImageViewerPage(_brick.PartImgUrl, _brick.Name));
    }

    private sealed class SetItem
    {
        public required string SetName       { get; init; }
        public string? SetImgUrl             { get; init; }
        public required string RequiresLabel { get; init; }
    }
}
