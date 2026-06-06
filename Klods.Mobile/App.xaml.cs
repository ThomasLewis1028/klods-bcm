using Klods.Mobile.Pages;

namespace Klods.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent(); // loads App.xaml resources first
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<LoginPage>()); // resolved after resources are ready
}
