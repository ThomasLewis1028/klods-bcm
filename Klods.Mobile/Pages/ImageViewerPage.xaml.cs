namespace Klods.Mobile.Pages;

public partial class ImageViewerPage : ContentPage
{
    private double _currentScale = 1;
    private double _startScale = 1;

    public ImageViewerPage(string imageUrl, string title)
    {
        InitializeComponent();
        Title = title;
        ViewerImage.Source = ImageSource.FromUri(new Uri(imageUrl));
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _startScale = _currentScale;
                break;
            case GestureStatus.Running:
                _currentScale = Math.Max(1, Math.Min(_startScale * e.Scale, 6));
                ViewerImage.Scale = _currentScale;
                break;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        _currentScale = 1;
        ViewerImage.Scale = 1;
    }

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
