using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class SetsPage : ContentPage
{
    private const int PageSize = 50;

    private readonly ApiClient _api;
    private readonly ObservableCollection<SetItem> _items = [];

    private bool _loaded;
    private bool _isGridView;
    private bool _hasMore;
    private bool _isLoadingMore;
    private int _currentPage;
    private string? _currentSearch;
    private CancellationTokenSource? _searchCts;

    public SetsPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public SetsPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        SetsList.ItemsSource = _items;
        GridList.ItemsSource = _items;
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

        var response = await _api.GetSetCatalogViewAsync(search: _currentSearch, page: 0, pageSize: PageSize);
        Loader.IsVisible = false;

        if (response is null)
        {
            ErrorLabel.Text = "Could not load the catalog.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            GridRefresher.IsVisible = false;
            return;
        }

        _loaded = true;
        _currentPage = 0;
        _hasMore = response.HasMore;
        ErrorView.IsVisible = false;

        StatsLabel.Text = string.Join("  ·  ", new[]
        {
            $"{response.TotalPieces:N0} total pieces",
            $"{response.TotalOwners} collector{(response.TotalOwners != 1 ? "s" : "")}"
        });

        _items.Clear();
        foreach (var s in response.Sets)
            _items.Add(ToItem(s));

        Refresher.IsVisible = !_isGridView;
        GridRefresher.IsVisible = _isGridView;
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (!_hasMore || _isLoadingMore) return;
        _isLoadingMore = true;

        var response = await _api.GetSetCatalogViewAsync(search: _currentSearch, page: _currentPage + 1, pageSize: PageSize);
        if (response is not null)
        {
            _currentPage++;
            _hasMore = response.HasMore;
            foreach (var s in response.Sets)
                _items.Add(ToItem(s));
        }

        _isLoadingMore = false;
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        try
        {
            await Task.Delay(350, cts.Token);
            _currentSearch = string.IsNullOrWhiteSpace(e.NewTextValue) ? null : e.NewTextValue.Trim();
            await LoadAsync(firstLoad: true);
        }
        catch (TaskCanceledException) { }
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

    private SetItem ToItem(ApiClient.SetCatalogViewDto s) => new()
    {
        SetId          = s.SetId,
        Name           = s.Name,
        SetImg         = _api.ResolveImageUrl(s.SetImg),
        NumBricks      = s.NumBricks,
        ReleaseYear    = s.ReleaseYear,
        ThemeName      = s.ThemeName,
        UserOwnedCount = s.UserOwnedCount,
    };

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
