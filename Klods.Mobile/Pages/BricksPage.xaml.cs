using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class BricksPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;
    private bool _isGridView;

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

        var result = await _api.GetBrickCatalogViewAsync();
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
        ErrorView.IsVisible = false;

        var items = result.Items
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

        BricksList.ItemsSource = items;
        GridList.ItemsSource = items;

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
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
