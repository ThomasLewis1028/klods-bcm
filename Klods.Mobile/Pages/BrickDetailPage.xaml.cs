using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class BrickDetailPage : ContentPage
{
    private readonly BrickInfo _brick;
    private readonly ApiClient _api;

    public BrickDetailPage(BrickInfo brick, ApiClient api)
    {
        InitializeComponent();
        _brick = brick;
        _api = api;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Title = _brick.PartNum;
        PartNumLabel.Text = $"{_brick.PartNum} · {_brick.ColorId}";
        NameLabel.Text = _brick.Name;
        ColorNameLabel.Text = _brick.ColorName ?? _brick.ColorId;
        RequiredLabel.Text = _brick.Count.ToString("N0");
        SpareLabel.Text = _brick.SpareCount.ToString("N0");
        SetStockLabel.Text = _brick.SetStock.ToString("N0");
        LooseStockLabel.Text = _brick.LooseStock.ToString("N0");

        SetStockLabel.TextColor = _brick.SetStock >= _brick.Count
            ? Color.FromArgb("#22C55E")
            : (Color)Application.Current!.Resources["Gray400"];

        if (_brick.HexColor is { Length: > 0 } hex)
            ColorSwatch.Fill = new SolidColorBrush(
                Color.FromArgb(hex.StartsWith('#') ? hex : "#" + hex));

        if (_brick.PartImgUrl is not null)
        {
            BrickImage.Source = ImageSource.FromUri(new Uri(_brick.PartImgUrl));
            PlaceholderIcon.IsVisible = false;
        }

        if (_brick.BricklinkId is { Length: > 0 })
            BricklinkButton.IsVisible = true;
    }

    private async void OnImageTapped(object? sender, TappedEventArgs e)
    {
        if (_brick.PartImgUrl is null) return;
        await Navigation.PushModalAsync(new ImageViewerPage(_brick.PartImgUrl, _brick.Name));
    }

    private async void OnBricklinkClicked(object? sender, EventArgs e)
    {
        if (_brick.BricklinkId is not { Length: > 0 } id) return;
        await Launcher.OpenAsync($"https://www.bricklink.com/v2/catalog/catalogitem.page?P={Uri.EscapeDataString(id)}");
    }

    public sealed record BrickInfo(
        string PartNum, string ColorId, string Name, string? PartImgUrl,
        string? ColorName, string? HexColor, int Count, int SpareCount,
        int SetStock, int LooseStock, string? BricklinkId);
}
