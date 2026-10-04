using GitLabDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GitLabDesktop;

public partial class App : Application
{
    readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var vm = _services.GetRequiredService<MainViewModel>();
        var window = new Window(_services.GetRequiredService<AppShell>())
        {
            Title = "GitLab Desktop",
            Width = 1400,
            Height = 900,
            MinimumWidth = 900,
            MinimumHeight = 600,
        };

        window.Created += async (_, _) => await vm.InitializeAsync();
        // Like GitHub Desktop, pick up changes made in other tools whenever the window regains focus.
        window.Activated += async (_, _) => await vm.RefreshAsync();
        return window;
    }
}
