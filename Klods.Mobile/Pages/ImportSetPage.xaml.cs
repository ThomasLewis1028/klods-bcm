using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class ImportSetPage : ContentPage
{
    private readonly ApiClient _api;
    private int _currentPage;
    private string _lastQuery = string.Empty;

    public ImportSetPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var query = QueryEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query)) return;

        _lastQuery = query;
        _currentPage = 0;
        await SearchAsync(query, page: 0, append: false);
    }

    private async void OnLoadMoreClicked(object? sender, EventArgs e)
    {
        _currentPage++;
        await SearchAsync(_lastQuery, _currentPage, append: true);
    }

    private async Task SearchAsync(string query, int page, bool append)
    {
        Loader.IsVisible = true;
        LoadMoreButton.IsVisible = false;
        StatusLabel.IsVisible = false;

        var response = await _api.ResolveSetAsync(query, page);

        Loader.IsVisible = false;

        if (response is null)
        {
            StatusLabel.Text = "Search failed. Check your connection and try again.";
            StatusLabel.IsVisible = true;
            if (!append) ResultsList.ItemsSource = null;
            return;
        }

        if (!response.Results.Any())
        {
            StatusLabel.Text = "No sets found.";
            StatusLabel.IsVisible = true;
            if (!append) ResultsList.ItemsSource = null;
            return;
        }

        var items = response.Results
            .Select(c => new CandidateItem(c.SetNum, c.Name, c.Year, c.ImageUrl))
            .ToList();

        if (append && ResultsList.ItemsSource is IEnumerable<CandidateItem> existing)
            items = existing.Concat(items).ToList();

        ResultsList.ItemsSource = items;
        LoadMoreButton.IsVisible = response.HasMore;

        if (response.Resolved)
        {
            StatusLabel.Text = "Set found.";
            StatusLabel.IsVisible = true;
        }
    }

    private async void OnCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: CandidateItem item }) return;

        var action = await DisplayActionSheetAsync(
            $"{item.Name} ({item.SetNum})",
            "Cancel",
            null,
            "Import to catalog",
            "Import and add to my collection");

        if (action is null or "Cancel") return;

        Loader.IsVisible = true;

        var imported = await _api.ImportSetAsync(item.SetNum);
        if (!imported)
        {
            Loader.IsVisible = false;
            await DisplayAlertAsync("Import failed", "Could not import this set. It may already be up to date.", "OK");
            return;
        }

        if (action == "Import and add to my collection")
        {
            var added = await _api.AddOwnedSetAsync(item.SetNum, applyBricks: false);
            Loader.IsVisible = false;

            if (!added)
                await DisplayAlertAsync("Partially done", "Set imported to catalog but could not be added to your collection.", "OK");
            else
                await DisplayAlertAsync("Done", $"{item.Name} imported and added to your collection.", "OK");
        }
        else
        {
            Loader.IsVisible = false;
            await DisplayAlertAsync("Done", $"{item.Name} imported to the catalog.", "OK");
        }

        await Navigation.PopAsync();
    }

    private sealed record CandidateItem(string SetNum, string Name, int Year, string? ImageUrl)
    {
        public string SubtitleLine => $"{SetNum}  ·  {Year}";
    }
}
