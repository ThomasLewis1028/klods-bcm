using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class MySetsPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;
    private ApiClient.MyOwnedSetDto[] _rawSets = [];

    public MySetsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public MySetsPage(ApiClient api)
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

        var sets = await _api.GetMyOwnedSetsAsync();

        Loader.IsVisible = false;

        if (sets is null)
        {
            ErrorLabel.Text = "Could not load your sets.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        _loaded = true;
        _rawSets = sets;
        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        SetsList.ItemsSource = sets
            .Select(s => new SetItem(
                SetId: s.SetId,
                Name: s.Name,
                SetImg: _api.ResolveImageUrl(s.SetImg),
                NumBricks: s.NumBricks,
                ReleaseYear: s.ReleaseYear,
                ThemeName: s.ThemeName,
                Copies: s.Instances.Count,
                TotalMissing: s.Instances.Sum(i => i.MissingPieceCount)))
            .ToList();
    }

    private async void OnSetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SetItem item) return;
        SetsList.SelectedItem = null;
        var raw = _rawSets.FirstOrDefault(s => s.SetId == item.SetId);
        if (raw is null) return;
        await Navigation.PushAsync(new SetDetailPage(raw, _api));
    }

    private sealed record SetItem(
        string SetId, string Name, string? SetImg, int NumBricks,
        int ReleaseYear, string? ThemeName, int Copies, int TotalMissing)
    {
        public string SubtitleLine1 => string.Join(" · ",
            new[] { SetId, ThemeName, ReleaseYear.ToString() }
                .Where(s => !string.IsNullOrEmpty(s)));
        public string PiecesLabel => $"{NumBricks:N0} pieces";
        public string CopiesLabel => Copies == 1 ? "1 copy" : $"{Copies} copies";
        public string MissingLabel => $"⚠ {TotalMissing:N0} missing";
        public bool HasMissing => TotalMissing > 0;
    }
}
