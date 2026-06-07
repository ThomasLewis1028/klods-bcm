using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class BricksPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;

    public BricksPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public BricksPage(ApiClient api)
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

        var bricks = await _api.GetBrickCatalogViewAsync();
        Loader.IsVisible = false;

        if (bricks is null)
        {
            ErrorLabel.Text = "Could not load the brick catalog.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        BricksList.ItemsSource = bricks
            .Select(b => new BrickItem(
                PartNum: b.PartNum,
                Name: b.Name,
                PartImgUrl: _api.ResolveImageUrl(b.PartImg),
                ColorName: b.ColorName ?? "No colour",
                HexColor: b.HexColor,
                TotalStock: b.TotalStock,
                TotalNeeded: b.TotalNeeded,
                SetCount: b.SetCount))
            .ToList();
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
