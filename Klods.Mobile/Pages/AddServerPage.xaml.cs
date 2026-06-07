using Klods.Mobile.Models;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class AddServerPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ServerStore _store;
    private readonly IServiceProvider _services;
    private readonly ThemeService _theme;
    private readonly ServerProfile? _existing;

    public AddServerPage(AuthService auth, ServerStore store, IServiceProvider services, ThemeService theme, ServerProfile? existing)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
        _services = services;
        _theme = theme;
        _existing = existing;

        if (existing is not null)
        {
            HeadingLabel.Text = existing.Name;
            Title = existing.Name;
            NameEntry.Text = existing.Name;
            UrlEntry.Text = existing.Url;
            UsernameEntry.Text = existing.LastUsername ?? string.Empty;
        }
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        ConnectButton.IsEnabled = false;

        var name     = NameEntry.Text?.Trim() ?? string.Empty;
        var url      = UrlEntry.Text?.Trim().TrimEnd('/') ?? string.Empty;
        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url) ||
            string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("All fields are required.");
            ConnectButton.IsEnabled = true;
            return;
        }

        var server = new ServerProfile(
            Id: _existing?.Id ?? Guid.NewGuid().ToString(),
            Name: name,
            Url: url,
            LastUsername: username);

        try
        {
            var success = await _auth.LoginAsync(server, username, password);
            if (!success)
            {
                ShowError("Invalid username or password.");
                ConnectButton.IsEnabled = true;
                return;
            }

            _store.Upsert(server);
            _theme.LoadCached(server.Id);
            Application.Current!.Windows[0].Page = _services.GetRequiredService<AppShell>();
        }
        catch
        {
            ShowError("Could not reach the server. Check the URL and your connection.");
            ConnectButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
