using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MinifigsPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;

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

        var minifigs = await _api.GetMinifigCatalogViewAsync();
        Loader.IsVisible = false;

        if (minifigs is null)
        {
            ErrorLabel.Text = "Could not load the minifig catalog.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        MinifigsList.ItemsSource = minifigs
            .Select(m => new MinifigItem(
                MinifigId: m.MinifigId,
                Name: m.MinifigName,
                ImgUrl: m.ImgUrl,
                PartCount: m.PartCount))
            .ToList();
    }

    private sealed record MinifigItem(string MinifigId, string Name, string? ImgUrl, int PartCount)
    {
        public string SubtitleLine => string.Join(" · ",
            new[] { MinifigId, PartCount > 0 ? $"{PartCount} parts" : null }
                .Where(s => !string.IsNullOrEmpty(s)));
    }
}
