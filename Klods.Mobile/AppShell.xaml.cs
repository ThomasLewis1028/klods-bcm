using Klods.Mobile.Services;

namespace Klods.Mobile;

public partial class AppShell : Shell
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;
    private readonly ThemeService _theme;
    private bool _ready;
    private bool _themeFetched;

    public AppShell()
        : this(ServiceHelper.Get<ApiClient>(), ServiceHelper.Get<AuthService>(), ServiceHelper.Get<ThemeService>()) { }

    public AppShell(ApiClient api, AuthService auth, ThemeService theme)
    {
        InitializeComponent();
        _api = api;
        _auth = auth;
        _theme = theme;
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _ready = true;

        if (!_themeFetched)
        {
            _themeFetched = true;
            _ = FetchAndApplyThemeAsync();
        }
    }

    private async Task FetchAndApplyThemeAsync()
    {
        var profile = await _api.GetMyProfileAsync();
        if (profile?.PrimaryColor is { Length: > 0 } color)
            _theme.Apply(color, _auth.ActiveServer?.Id);
    }

    // Fires when switching TO a different tab while child pages are pushed.
    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        if (_ready &&
            Navigation.NavigationStack.Count > 1 &&
            args.Source is ShellNavigationSource.ShellItemChanged
                        or ShellNavigationSource.ShellSectionChanged
                        or ShellNavigationSource.ShellContentChanged)
        {
            args.Cancel();
            Dispatcher.Dispatch(async () =>
            {
                await Navigation.PopToRootAsync(animated: false);
                await GoToAsync(args.Target.Location);
            });
            return;
        }

        base.OnNavigating(args);
    }

#if ANDROID
    // MAUI Shell does not fire OnNavigating when the user re-taps the already-active
    // bottom tab, so we hook Android's BottomNavigationView reselection event directly.
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        Dispatcher.Dispatch(() =>
        {
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView
                    is not Android.Views.ViewGroup root) return;

            var bottomNav = FindDescendant<Google.Android.Material.BottomNavigation.BottomNavigationView>(root);
            bottomNav?.SetOnItemReselectedListener(new ReselectedListener(() =>
            {
                if (_ready && Navigation.NavigationStack.Count > 1)
                    Dispatcher.Dispatch(async () => await Navigation.PopToRootAsync(animated: false));
            }));
        });
    }

    private static T? FindDescendant<T>(Android.Views.ViewGroup parent) where T : Android.Views.View
    {
        for (var i = 0; i < parent.ChildCount; i++)
        {
            var child = parent.GetChildAt(i);
            if (child is T match) return match;
            if (child is Android.Views.ViewGroup group)
            {
                var found = FindDescendant<T>(group);
                if (found != null) return found;
            }
        }
        return null;
    }

    private sealed class ReselectedListener : Java.Lang.Object,
        Google.Android.Material.BottomNavigation.BottomNavigationView.IOnItemReselectedListener
    {
        private readonly Action _action;
        public ReselectedListener(Action action) => _action = action;
        public void OnNavigationItemReselected(Android.Views.IMenuItem item) => _action();
    }
#endif
}
