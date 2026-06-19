using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarpManager.Models;
using WarpManager.ViewModels;

namespace WarpManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm        = vm;
        DataContext = vm;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await _vm.RefreshCommand.ExecuteAsync(null);
        _timer.Start();

        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;

        bool isIpTab = MainTabs.SelectedIndex == 0;
        AddButton.CommandParameter      = isIpTab ? ExclusionType.Ip : ExclusionType.Host;
        RemoveAllButton.CommandParameter = isIpTab ? ExclusionType.Ip : ExclusionType.Host;
        AddButton.Content               = isIpTab ? "Add IP/CIDR" : "Add Host";
    }
}
