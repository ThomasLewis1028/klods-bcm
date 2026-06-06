using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class ChangePasswordPage : ContentPage
{
    private readonly ApiClient _api;

    public ChangePasswordPage() : this(ServiceHelper.Get<ApiClient>()) { }

    public ChangePasswordPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        SaveButton.IsEnabled = false;

        var current  = CurrentPasswordEntry.Text ?? string.Empty;
        var next     = NewPasswordEntry.Text ?? string.Empty;
        var confirm  = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
        {
            ShowError("All fields are required.");
            SaveButton.IsEnabled = true;
            return;
        }

        if (next != confirm)
        {
            ShowError("New passwords do not match.");
            SaveButton.IsEnabled = true;
            return;
        }

        var success = await _api.ChangePasswordAsync(current, next);
        SaveButton.IsEnabled = true;

        if (success)
            await Navigation.PopAsync();
        else
            ShowError("Current password is incorrect.");
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
