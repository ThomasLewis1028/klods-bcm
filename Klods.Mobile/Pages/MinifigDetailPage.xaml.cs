using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MinifigDetailPage : ContentPage
{
    private readonly ApiClient.MyMinifigDto _minifig;
    private readonly ApiClient _api;
    private int _stock;
    private bool _partsLoaded;

    public MinifigDetailPage(ApiClient.MyMinifigDto minifig, ApiClient api)
    {
        InitializeComponent();
        _minifig = minifig;
        _api = api;
        _stock = minifig.Stock;

        if (minifig.ImgUrl is not null)
        {
            MinifigImage.Source = minifig.ImgUrl;
            PlaceholderIcon.IsVisible = false;
        }

        MinifigIdLabel.Text = minifig.MinifigId;
        NameLabel.Text = minifig.MinifigName;
        StockLabel.Text = _stock.ToString();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_partsLoaded)
            await LoadPartsAsync();
    }

    private async Task LoadPartsAsync()
    {
        PartsLoader.IsVisible = true;
        PartsErrorLabel.IsVisible = false;

        var parts = await _api.GetMinifigBricksAsync(_minifig.MinifigId);
        PartsLoader.IsVisible = false;

        if (parts is null)
        {
            PartsErrorLabel.Text = "Could not load parts.";
            PartsErrorLabel.IsVisible = true;
            return;
        }

        if (parts.Length == 0)
        {
            PartsErrorLabel.Text = "No parts listed for this minifig.";
            PartsErrorLabel.IsVisible = true;
            return;
        }

        _partsLoaded = true;
        BindableLayout.SetItemsSource(PartsLayout, parts.Select(p => new PartItem
        {
            Name         = p.Name,
            PartImgUrl   = _api.ResolveImageUrl(p.PartImg),
            ColorName    = p.ColorName ?? p.ColorId,
            HexColor     = p.HexColor,
            Quantity     = p.Quantity,
        }).ToList());
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        _stock++;
        StockLabel.Text = _stock.ToString();
        _ = _api.UpdateMinifigStockAsync(_minifig.MinifigId, _stock);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (_stock <= 0) return;
        _stock--;
        StockLabel.Text = _stock.ToString();
        _ = _api.UpdateMinifigStockAsync(_minifig.MinifigId, _stock);
    }

    private async void OnImageTapped(object? sender, TappedEventArgs e)
    {
        if (_minifig.ImgUrl is null) return;
        await Navigation.PushModalAsync(new ImageViewerPage(_minifig.ImgUrl, _minifig.MinifigName));
    }

    private sealed class PartItem
    {
        public required string Name       { get; init; }
        public string? PartImgUrl         { get; init; }
        public required string ColorName  { get; init; }
        public string? HexColor           { get; init; }
        public required int Quantity      { get; init; }

        public SolidColorBrush SwatchBrush => new(
            HexColor is { Length: > 0 } h
                ? Color.FromArgb(h.StartsWith('#') ? h : "#" + h)
                : Colors.Gray);

        public string QuantityLabel => $"×{Quantity}";
    }
}
