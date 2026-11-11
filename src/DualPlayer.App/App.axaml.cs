using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DualPlayer.App.ViewModels;
using DualPlayer.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.App;

public partial class App : Application
{
    /// <summary>Set by <see cref="Program.Main"/> before the Avalonia lifetime starts.</summary>
    internal static IServiceProvider? Services { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
