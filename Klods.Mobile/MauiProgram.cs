using Klods.Mobile.Pages;
using Klods.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace Klods.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ServerStore>();
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddSingleton<ThemeService>();

        builder.Services.AddTransient<ServerListPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }
}
