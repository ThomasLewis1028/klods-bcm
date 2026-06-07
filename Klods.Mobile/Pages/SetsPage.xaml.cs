using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class SetsPage : ContentPage
{
    private readonly ApiClient _api;
    private bool _loaded;

    public SetsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public SetsPage(ApiClient api)
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

        var response = await _api.GetSetCatalogViewAsync();
        Loader.IsVisible = false;

        if (response is null)
        {
            ErrorLabel.Text = "Could not load the catalog.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        _loaded = true;
        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        StatsLabel.Text = string.Join("  ·  ", new[]
        {
            $"{response.Sets.Count:N0} sets",
            $"{response.TotalPieces:N0} total pieces",
            $"{response.TotalOwners} collector{(response.TotalOwners != 1 ? "s" : "")}"
        });

        SetsList.ItemsSource = response.Sets
            .Select(s => new SetItem
            {
                SetId = s.SetId,
                Name = s.Name,
                SetImg = _api.ResolveImageUrl(s.SetImg),
                NumBricks = s.NumBricks,
                ReleaseYear = s.ReleaseYear,
                ThemeName = s.ThemeName,
                UserOwnedCount = s.UserOwnedCount
            })
            .ToList();
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: SetItem item }) return;
        item.UserOwnedCount++;
        _ = _api.AddOwnedSetAsync(item.SetId, applyBricks: false);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: SetItem item }) return;
        if (item.UserOwnedCount <= 0) return;
        item.UserOwnedCount--;
        _ = _api.RemoveLastOwnedSetAsync(item.SetId);
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        _loaded = false;
        await Navigation.PushAsync(new ImportSetPage(_api));
    }

    private sealed class SetItem : INotifyPropertyChanged
    {
        public required string SetId     { get; init; }
        public required string Name      { get; init; }
        public string? SetImg            { get; init; }
        public required int NumBricks    { get; init; }
        public required int ReleaseYear  { get; init; }
        public string? ThemeName         { get; init; }

        private int _userOwnedCount;
        public int UserOwnedCount
        {
            get => _userOwnedCount;
            set
            {
                if (_userOwnedCount == value) return;
                _userOwnedCount = value;
                Notify();
                Notify(nameof(IsOwned));
                Notify(nameof(OwnedLabel));
            }
        }

        public bool IsOwned => UserOwnedCount > 0;

        public string SubtitleLine1 => string.Join(" · ",
            new[] { SetId, ThemeName, ReleaseYear.ToString() }
                .Where(s => !string.IsNullOrEmpty(s)));

        public string PiecesLabel => $"{NumBricks:N0} pieces";
        public string OwnedLabel => UserOwnedCount == 1 ? "1 copy owned" : $"{UserOwnedCount} copies owned";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
