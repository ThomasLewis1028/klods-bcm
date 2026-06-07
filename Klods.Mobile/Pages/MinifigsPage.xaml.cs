using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MinifigsPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;
    private bool _isGridView;

    public MinifigsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MinifigsPage(ApiClient api)
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

        var minifigs = await _api.GetMinifigCatalogViewAsync();
        Loader.IsVisible = false;

        if (minifigs is null)
        {
            ErrorLabel.Text = "Could not load the minifig catalog.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            GridRefresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;

        var items = minifigs
            .Select(m => new MinifigItem(
                MinifigId: m.MinifigId,
                Name: m.MinifigName,
                ImgUrl: m.ImgUrl,
                PartCount: m.PartCount))
            .ToList();

        MinifigsList.ItemsSource = items;
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

    private sealed record MinifigItem(string MinifigId, string Name, string? ImgUrl, int PartCount)
    {
        public string SubtitleLine => string.Join(" · ",
            new[] { MinifigId, PartCount > 0 ? $"{PartCount} parts" : null }
                .Where(s => !string.IsNullOrEmpty(s)));
    }
}
