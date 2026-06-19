using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WarpManager.Services;
using WarpManager.ViewModels;
using WarpManager.Views;

namespace WarpManager;

public partial class App : Application
{
    private readonly ServiceProvider _services;

    public App()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IWarpCliService, WarpCliService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var win = _services.GetRequiredService<MainWindow>();
            win.Show();
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(
                ex.Message + "\n\nPlease install Cloudflare WARP from cloudflareclient.com",
                "WARP not found", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
