using Klods.Mobile.Models;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class ServerListPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ServerStore _store;
    private readonly IServiceProvider _services;
    private readonly ThemeService _theme;

    public ServerListPage(AuthService auth, ServerStore store, IServiceProvider services, ThemeService theme)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
        _services = services;
        _theme = theme;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ServerList.ItemsSource = _store.GetAll();
    }

    private async void OnServerTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ServerProfile server)
            await ConnectAsync(server);
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ServerProfile server })
            await ConnectAsync(server);
    }

    private async Task ConnectAsync(ServerProfile server)
    {
        if (await _auth.TryResumeAsync(server))
        {
            _theme.LoadCached(server.Id);
            NavigateToShell();
            return;
        }

        await Navigation.PushAsync(new AddServerPage(_auth, _store, _services, _theme, server));
    }

    private async void OnAddServerClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new AddServerPage(_auth, _store, _services, _theme, null));
    }

    private void NavigateToShell()
    {
        Application.Current!.Windows[0].Page = _services.GetRequiredService<AppShell>();
    }
}
