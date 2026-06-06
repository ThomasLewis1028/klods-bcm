using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class SetDetailPage : ContentPage
{
    private readonly ApiClient.MyOwnedSetDto _set;
    private readonly ApiClient _api;

    public SetDetailPage(ApiClient.MyOwnedSetDto set, ApiClient api)
    {
        InitializeComponent();
        _set = set;
        _api = api;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Title = _set.SetId;
        SetNameLabel.Text = _set.Name;
        PiecesLabel.Text = $"{_set.NumBricks:N0} pieces";

        var parts = new List<string> { _set.SetId };
        if (_set.ThemeName is { Length: > 0 } theme) parts.Add(theme);
        parts.Add(_set.ReleaseYear.ToString());
        SubtitleLabel.Text = string.Join(" · ", parts);

        var imgUrl = _api.ResolveImageUrl(_set.SetImg);
        if (imgUrl is not null)
        {
            HeroImage.Source = ImageSource.FromUri(new Uri(imgUrl));
            PlaceholderIcon.IsVisible = false;
        }

        BindableLayout.SetItemsSource(CopiesLayout, _set.Instances
            .OrderBy(i => i.SetIndex)
            .Select(i => new CopyItem(i.SetIndex, i.StockCount, i.MissingPieceCount))
            .ToList());
    }

    private async void OnHeroTapped(object? sender, TappedEventArgs e)
    {
        var imgUrl = _api.ResolveImageUrl(_set.SetImg);
        if (imgUrl is null) return;
        await Navigation.PushModalAsync(new ImageViewerPage(imgUrl, _set.Name));
    }

    private async void OnViewBricksClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: int setIndex }) return;
        await Navigation.PushAsync(new BomPage(_set.SetId, setIndex, _set.Name, _api));
    }

    private sealed record CopyItem(int SetIndex, int StockCount, int MissingPieceCount)
    {
        public int Total => StockCount + MissingPieceCount;
        public double Progress => Total == 0 ? 0 : Math.Min(1.0, (double)StockCount / Total);
        public string CopyLabel => $"Copy {SetIndex + 1}";
        public string ProgressLabel => $"{StockCount:N0} / {Total:N0} pieces";
        public string PercentLabel => Total == 0 ? "—" : $"{(int)(Progress * 100)}%";
    }
}
