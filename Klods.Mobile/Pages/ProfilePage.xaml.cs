using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;
    private readonly ThemeService _theme;

    public ProfilePage() : this(ServiceHelper.Get<ApiClient>(), ServiceHelper.Get<AuthService>(), ServiceHelper.Get<ThemeService>()) { }

    public ProfilePage(ApiClient api, AuthService auth, ThemeService theme)
    {
        InitializeComponent();
        _api = api;
        _auth = auth;
        _theme = theme;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var server = _auth.ActiveServer;
        if (server is not null)
        {
            ServerNameLabel.Text = server.Name;
            ServerUrlLabel.Text = server.Url;
        }

        Loader.IsVisible = true;
        ErrorLabel.IsVisible = false;

        var profileTask = _api.GetMyProfileAsync();
        var loginsTask  = _api.GetLinkedLoginsAsync();
        await Task.WhenAll(profileTask, loginsTask);
        var profile = profileTask.Result;
        var logins  = loginsTask.Result;

        Loader.IsVisible = false;

        if (profile is null)
        {
            ErrorLabel.Text = "Could not load profile.";
            ErrorLabel.IsVisible = true;
            return;
        }

        UsernameLabel.Text = profile.UserName;
        RoleLabel.Text = profile.Role;
        ChangePasswordRow.IsVisible = profile.HasPassword;

        if (!string.IsNullOrEmpty(profile.ProfilePictureUrl))
        {
            AvatarImage.Source = ImageSource.FromUri(new Uri(profile.ProfilePictureUrl));
            AvatarImage.IsVisible = true;
            AvatarIcon.IsVisible = false;
        }

        if (!string.IsNullOrEmpty(profile.PrimaryColor))
        {
            ThemeSwatch.Fill = new SolidColorBrush(Color.FromArgb(profile.PrimaryColor));
            ThemeHexLabel.Text = profile.PrimaryColor.ToUpperInvariant();
        }
        else
        {
            ThemeHexLabel.Text = "Default";
        }

        LinkedLoginsList.ItemsSource = logins ?? [];
    }

    private async void OnChangeThemeTapped(object? sender, TappedEventArgs e)
    {
        var current = ThemeHexLabel.Text == "Default" ? string.Empty : ThemeHexLabel.Text;
        var input = await DisplayPromptAsync(
            "Theme Colour",
            "Enter a hex colour (e.g. #512BD4), or leave blank to reset to default.",
            initialValue: current,
            placeholder: "#512BD4",
            maxLength: 7);

        if (input is null) return;

        var color = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        if (await _api.ChangeThemeAsync(color))
        {
            _theme.Apply(color, _auth.ActiveServer?.Id);

            if (color is not null)
            {
                ThemeSwatch.Fill = new SolidColorBrush(Color.FromArgb(color));
                ThemeHexLabel.Text = color.ToUpperInvariant();
            }
            else
            {
                ThemeSwatch.Fill = new SolidColorBrush((Color)Application.Current!.Resources["Primary"]);
                ThemeHexLabel.Text = "Default";
            }
        }
        else
        {
            await DisplayAlertAsync("Error", "Could not update theme colour.", "OK");
        }
    }

    private async void OnChangePictureTapped(object? sender, TappedEventArgs e)
    {
        var current = AvatarImage.IsVisible ? AvatarImage.Source?.ToString() ?? string.Empty : string.Empty;
        var input = await DisplayPromptAsync(
            "Profile Picture",
            "Enter a URL for your profile picture, or leave blank to remove it.",
            initialValue: current,
            placeholder: "https://example.com/avatar.png",
            keyboard: Keyboard.Url);

        if (input is null) return;

        var url = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        if (await _api.ChangePictureAsync(url))
        {
            if (url is not null)
            {
                AvatarImage.Source = ImageSource.FromUri(new Uri(url));
                AvatarImage.IsVisible = true;
                AvatarIcon.IsVisible = false;
            }
            else
            {
                AvatarImage.IsVisible = false;
                AvatarIcon.IsVisible = true;
            }
        }
        else
        {
            await DisplayAlertAsync("Error", "Could not update profile picture.", "OK");
        }
    }

    private async void OnChangePasswordTapped(object? sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new ChangePasswordPage());
    }

    private async void OnUnlinkClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string provider }) return;

        var confirm = await DisplayAlertAsync(
            "Unlink Account",
            $"Unlink your {provider} account?",
            "Unlink", "Cancel");

        if (!confirm) return;

        if (await _api.UnlinkLoginAsync(provider))
            LinkedLoginsList.ItemsSource = (await _api.GetLinkedLoginsAsync()) ?? [];
        else
            await DisplayAlertAsync("Error", "Could not unlink account.", "OK");
    }

    private void OnLogoutClicked(object? sender, EventArgs e)
    {
        var server = _auth.ActiveServer;
        if (server is not null)
            _auth.Logout(server.Id);

        _theme.Reset();

        Application.Current!.Windows[0].Page =
            new NavigationPage(ServiceHelper.Get<ServerListPage>());
    }
}
