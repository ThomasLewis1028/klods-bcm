using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _auth;

    public LoginPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ServerEntry.Text = _auth.ServerUrl ?? string.Empty;

        if (await _auth.IsAuthenticatedAsync())
            NavigateToShell();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        LoginButton.IsEnabled = false;

        var server   = ServerEntry.Text?.Trim() ?? string.Empty;
        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrEmpty(server))
        {
            ShowError("Server URL is required.");
            LoginButton.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Username and password are required.");
            LoginButton.IsEnabled = true;
            return;
        }

        try
        {
            var success = await _auth.LoginAsync(server, username, password);
            if (success)
                NavigateToShell();
            else
                ShowError("Invalid username or password.");
        }
        catch
        {
            ShowError("Could not reach the server. Check the URL and your connection.");
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void NavigateToShell()
    {
        Application.Current!.Windows[0].Page =
            IPlatformApplication.Current!.Services.GetRequiredService<AppShell>();
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
