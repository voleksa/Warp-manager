using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarpManager.Models;
using WarpManager.Services;
using WarpManager.Views;

namespace WarpManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IWarpCliService _warp;

    [ObservableProperty] private WarpStatus status = WarpStatus.Unknown;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ObservableCollection<ExclusionItemViewModel> ipExclusions  = [];
    [ObservableProperty] private ObservableCollection<ExclusionItemViewModel> hostExclusions = [];

    public bool IsNotBusy => !IsBusy;

    public string StatusText => Status switch
    {
        WarpStatus.Connected         => "Connected",
        WarpStatus.Connecting        => "Connecting...",
        WarpStatus.Disconnected      => "Disconnected",
        WarpStatus.ServiceNotRunning => "Service offline",
        _                            => "Unknown",
    };

    public string ToggleLabel => Status == WarpStatus.Connected ? "Disable WARP" : "Enable WARP";

    public MainViewModel(IWarpCliService warp)
    {
        _warp = warp;
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        ToggleWarpCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        AddExclusionCommand.NotifyCanExecuteChanged();
        RemoveAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusChanged(WarpStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ToggleWarpAsync()
    {
        IsBusy = true;
        try
        {
            WarpResult r = Status == WarpStatus.Connected
                ? await _warp.DisconnectAsync()
                : await _warp.ConnectAsync();

            if (!r.Success)
                ShowError(r.Error);

            await RefreshStatusAsync();

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (Status == WarpStatus.Connecting && DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000);
                await RefreshStatusAsync();
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try   { await RefreshAllAsync(); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task AddExclusionAsync(ExclusionType type)
    {
        var dlg = new AddExclusionDialog(type) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            WarpResult r = type == ExclusionType.Ip
                ? await _warp.AddIpExclusionAsync(dlg.EnteredValue)
                : await _warp.AddHostExclusionAsync(dlg.EnteredValue);

            if (!r.Success)
                ShowError(r.Error);
            else
                await ReloadListAsync(type);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RemoveAllAsync(ExclusionType type)
    {
        var label  = type == ExclusionType.Ip ? "IP/CIDR" : "host";
        var answer = MessageBox.Show(
            $"Remove ALL {label} exclusions?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            WarpResult r = type == ExclusionType.Ip
                ? await _warp.RemoveAllIpExclusionsAsync()
                : await _warp.RemoveAllHostExclusionsAsync();

            if (!r.Success)
                ShowError(r.Error);
            else
                await ReloadListAsync(type);
        }
        finally { IsBusy = false; }
    }

    internal async Task RemoveItemAsync(ExclusionItemViewModel item)
    {
        IsBusy = true;
        try
        {
            WarpResult r = item.Type == ExclusionType.Ip
                ? await _warp.RemoveIpExclusionAsync(item.Value)
                : await _warp.RemoveHostExclusionAsync(item.Value);

            if (!r.Success && !r.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                ShowError(r.Error);

            await ReloadListAsync(item.Type);
        }
        finally { IsBusy = false; }
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try   { await RefreshAllAsync(); }
        finally { IsBusy = false; }
    }

    private async Task RefreshAllAsync()
    {
        await RefreshStatusAsync();
        await ReloadListAsync(ExclusionType.Ip);
        await ReloadListAsync(ExclusionType.Host);
    }

    private async Task RefreshStatusAsync()
    {
        Status = await _warp.GetStatusAsync();
    }

    private async Task ReloadListAsync(ExclusionType type)
    {
        if (type == ExclusionType.Ip)
        {
            var items = await _warp.ListIpExclusionsAsync();
            IpExclusions = new ObservableCollection<ExclusionItemViewModel>(
                items.Select(v => new ExclusionItemViewModel(v, ExclusionType.Ip, this)));
        }
        else
        {
            var items = await _warp.ListHostExclusionsAsync();
            HostExclusions = new ObservableCollection<ExclusionItemViewModel>(
                items.Select(v => new ExclusionItemViewModel(v, ExclusionType.Host, this)));
        }
    }

    private static void ShowError(string message)
    {
        if (message.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("denied",  StringComparison.OrdinalIgnoreCase))
            message += "\n\nTry running as Administrator.";

        MessageBox.Show(message, "WARP Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
