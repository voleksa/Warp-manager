# Cloudflare WARP Manager — Implementation Plan

## Overview

A lightweight Windows desktop app (.NET 8 / WPF) that wraps `warp-cli.exe` to
expose split-tunnel management that is missing from the WARP 2026.x UI:

- Show current WARP status (connected / disconnected)
- Enable / disable WARP with one click
- Manage **IP/CIDR exclusions** (`warp-cli tunnel ip`)
- Manage **Host/domain exclusions** (`warp-cli tunnel host`)
- Add a new exclusion (auto-routed to the right subcommand)
- Remove individual entries or clear all from either list

---

## Platform

| Item           | Value                                                            |
|----------------|------------------------------------------------------------------|
| OS             | Windows 10 / 11                                                  |
| Runtime        | .NET 8 (LTS)                                                     |
| UI framework   | WPF                                                              |
| warp-cli path  | `C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe`      |
| WARP version   | 2026.4.1390.0 (confirmed GA)                                     |

---

## Tech Stack

| Layer        | Choice                                      | Reason                                              |
|--------------|---------------------------------------------|-----------------------------------------------------|
| Language     | C# 12                                       | First-class .NET; async/await built-in              |
| UI           | WPF (.NET 8)                                | Native Windows look; rich data-binding              |
| MVVM helpers | `CommunityToolkit.Mvvm`                     | Source-generated `[ObservableProperty]`, `[RelayCommand]` |
| Styling      | `ModernWpfUI`                               | Fluent / Windows 11 look, zero effort               |
| CLI IPC      | `System.Diagnostics.Process`                | Spawn `warp-cli.exe`, capture stdout/stderr         |
| Async        | `async` / `await` + `Task.Run`              | Keep UI thread responsive during CLI calls          |
| DI           | `Microsoft.Extensions.DependencyInjection`  | Testable service layer                              |

---

## Cloudflare WARP CLI Reference — version 2026.4.1390.0

> Source: official changelog at developers.cloudflare.com/changelog

The split-tunnel surface is divided into **two independent subcommands**:

```powershell
# ── Status & toggle ────────────────────────────────────────────────
warp-cli status                       # "Status update: Connected" | "Disconnected"
warp-cli connect
warp-cli disconnect

# ── Full routing dump (read-only overview) ─────────────────────────
warp-cli tunnel dump                  # prints all excluded IPs and hosts

# ── IP / CIDR exclusions ───────────────────────────────────────────
warp-cli tunnel ip list
warp-cli tunnel ip add    <cidr>      # e.g. 192.168.1.0/24  or  10.0.0.1
warp-cli tunnel ip remove <cidr>

# ── Host / domain exclusions ───────────────────────────────────────
warp-cli tunnel host list
warp-cli tunnel host add    <domain>  # e.g. internal.corp  or  *.example.com
warp-cli tunnel host remove <domain>
```

Exit code `0` = success. Errors appear on stderr with a non-zero exit code.

> **Note:** `warp-cli split-tunnel exclude ...` (old syntax) does NOT exist
> in 2026.4.1390.0. Using it will return an error.

---

## Project Structure

```
WarpManager/
├── WarpManager.sln
└── WarpManager/
    ├── WarpManager.csproj
    ├── App.xaml / App.xaml.cs
    │
    ├── Models/
    │   ├── WarpResult.cs              # record: bool Success, string Output, string Error
    │   └── ExclusionType.cs           # enum: Ip, Host
    │
    ├── Services/
    │   ├── IWarpCliService.cs
    │   └── WarpCliService.cs          # all Process calls; two separate list methods
    │
    ├── ViewModels/
    │   ├── MainViewModel.cs           # status, toggle, refresh, tab selection
    │   └── ExclusionItemViewModel.cs  # one row; owns Remove command + ExclusionType
    │
    ├── Views/
    │   ├── MainWindow.xaml / .cs
    │   └── AddExclusionDialog.xaml / .cs
    │
    └── Converters/
        ├── StatusToBrushConverter.cs
        └── BoolToVisibilityConverter.cs
```

---

## Models

```csharp
// Models/WarpResult.cs
public record WarpResult(bool Success, string Output, string Error = "")
{
    public static WarpResult Ok(string output)  => new(true, output);
    public static WarpResult Fail(string error) => new(false, string.Empty, error);
}

// Models/ExclusionType.cs
public enum ExclusionType { Ip, Host }
```

---

## Service Layer

```csharp
// Services/IWarpCliService.cs
public interface IWarpCliService
{
    Task<WarpStatus>   GetStatusAsync();
    Task<WarpResult>   ConnectAsync();
    Task<WarpResult>   DisconnectAsync();

    Task<List<string>> ListIpExclusionsAsync();
    Task<WarpResult>   AddIpExclusionAsync(string cidr);
    Task<WarpResult>   RemoveIpExclusionAsync(string cidr);
    Task<WarpResult>   RemoveAllIpExclusionsAsync();

    Task<List<string>> ListHostExclusionsAsync();
    Task<WarpResult>   AddHostExclusionAsync(string host);
    Task<WarpResult>   RemoveHostExclusionAsync(string host);
    Task<WarpResult>   RemoveAllHostExclusionsAsync();
}

public enum WarpStatus { Connected, Disconnected, ServiceNotRunning, Unknown }
```

```csharp
// Services/WarpCliService.cs  — key internals
private const string CliPath =
    @"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe";

// Shared runner — all public methods call this
private async Task<WarpResult> RunAsync(params string[] args)
{
    using var proc = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName               = CliPath,
            Arguments              = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        }
    };

    proc.Start();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
    var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
    await proc.WaitForExitAsync(cts.Token);

    return proc.ExitCode == 0
        ? WarpResult.Ok(stdout.Trim())
        : WarpResult.Fail(stderr.Trim());
}

// Parse a list command output into individual entries
private static List<string> ParseList(string output) =>
    output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
          .Select(l => l.Trim())
          .Where(l => l.Length > 0)
          .ToList();

public async Task<WarpStatus> GetStatusAsync()
{
    var r = await RunAsync("status");
    if (!r.Success) return WarpStatus.ServiceNotRunning;
    if (r.Output.Contains("Connected",    StringComparison.OrdinalIgnoreCase)) return WarpStatus.Connected;
    if (r.Output.Contains("Disconnected", StringComparison.OrdinalIgnoreCase)) return WarpStatus.Disconnected;
    return WarpStatus.Unknown;
}

public async Task<List<string>> ListIpExclusionsAsync()
{
    var r = await RunAsync("tunnel", "ip", "list");
    return r.Success ? ParseList(r.Output) : [];
}

public async Task<List<string>> ListHostExclusionsAsync()
{
    var r = await RunAsync("tunnel", "host", "list");
    return r.Success ? ParseList(r.Output) : [];
}

public Task<WarpResult> AddIpExclusionAsync(string cidr)
    => RunAsync("tunnel", "ip", "add", cidr);

public Task<WarpResult> RemoveIpExclusionAsync(string cidr)
    => RunAsync("tunnel", "ip", "remove", cidr);

public Task<WarpResult> AddHostExclusionAsync(string host)
    => RunAsync("tunnel", "host", "add", host);

public Task<WarpResult> RemoveHostExclusionAsync(string host)
    => RunAsync("tunnel", "host", "remove", host);

public async Task<WarpResult> RemoveAllIpExclusionsAsync()
{
    foreach (var ip in await ListIpExclusionsAsync())
    {
        var r = await RemoveIpExclusionAsync(ip);
        if (!r.Success) return r;
    }
    return WarpResult.Ok("Done");
}

public async Task<WarpResult> RemoveAllHostExclusionsAsync()
{
    foreach (var host in await ListHostExclusionsAsync())
    {
        var r = await RemoveHostExclusionAsync(host);
        if (!r.Success) return r;
    }
    return WarpResult.Ok("Done");
}
```

---

## ViewModels

### `MainViewModel.cs`

```csharp
[ObservableProperty] WarpStatus status;
[ObservableProperty] bool       isBusy;

// Separate observable collections for the two tabs
[ObservableProperty] ObservableCollection<ExclusionItemViewModel> ipExclusions;
[ObservableProperty] ObservableCollection<ExclusionItemViewModel> hostExclusions;

[RelayCommand(CanExecute = nameof(IsNotBusy))] Task ToggleWarpAsync();
[RelayCommand(CanExecute = nameof(IsNotBusy))] Task RefreshAsync();
[RelayCommand(CanExecute = nameof(IsNotBusy))] Task AddExclusionAsync(ExclusionType type);
[RelayCommand(CanExecute = nameof(IsNotBusy))] Task RemoveAllAsync(ExclusionType type);

// Called by child ExclusionItemViewModel
internal Task RemoveItemAsync(ExclusionItemViewModel item);
```

### `ExclusionItemViewModel.cs`

```csharp
public partial class ExclusionItemViewModel : ObservableObject
{
    public string        Value { get; }
    public ExclusionType Type  { get; }     // Ip | Host

    private readonly MainViewModel _parent;

    [RelayCommand]
    Task RemoveAsync() => _parent.RemoveItemAsync(this);
}
```

---

## UI Layout

### MainWindow.xaml

```
┌──────────────────────────────────────────────────┐
│  🔒 WARP Manager                     [─][□][✕]  │
├──────────────────────────────────────────────────┤
│  ●  Connected                 [ ↺ Refresh ]     │
│     [      Disable WARP      ]                   │
├──────────────────────────────────────────────────┤
│  ┌──────────────────┬──────────────────────────┐ │
│  │  IP / CIDR  (2)  │    Hosts / Domains  (3)  │ │  ← TabControl
│  └──────────────────┴──────────────────────────┘ │
│  ┌──────────────────────────────────────────┐    │
│  │ 192.168.1.0/24                  [Remove] │    │
│  │ 10.0.0.0/8                      [Remove] │    │
│  └──────────────────────────────────────────┘    │
│                                                  │
│  [ Add IP/CIDR ]             [ Remove All ]     │
│  ░░░░░ (spinner — visible only when IsBusy)      │
└──────────────────────────────────────────────────┘
```

- Two tabs: **IP / CIDR** and **Hosts / Domains** — tab headers show entry count
- `[Add ...]` label changes with active tab ("Add IP/CIDR" vs "Add Host")
- `[Remove All]` is scoped to the active tab
- Status dot: `Ellipse` Fill bound via `StatusToBrushConverter` (Green / Red / Grey)
- All action buttons gated by `!IsBusy`
- `ProgressBar IsIndeterminate` spinner visible when `IsBusy`
- Auto-refresh every 30 s via `DispatcherTimer`

### AddExclusionDialog.xaml

```
┌─────────────────────────────────────────────┐
│  Add IP / CIDR  (or "Add Host")             │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ e.g.  192.168.1.0/24               │   │  (or internal.corp)
│  └─────────────────────────────────────┘   │
│  ⚠ Invalid format                          │  (visible on bad input)
│                                             │
│  [ Cancel ]                   [  Add  ]   │
└─────────────────────────────────────────────┘
```

Dialog is opened with `ExclusionType` already known from the active tab.
`[Add]` is disabled until input passes the relevant validator.

---

## Input Validation

```csharp
// IP/CIDR check — uses BCL IPNetwork
public static bool IsValidIpOrCidr(string input)
{
    try { IPNetwork.Parse(input); return true; }
    catch { return false; }
}

// Host/domain check — bare domain or wildcard prefix
private static readonly Regex HostRegex =
    new(@"^(\*\.)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}$|^localhost$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

public static bool IsValidHost(string input) => HostRegex.IsMatch(input);

public static (bool ok, string error) Validate(ExclusionType type, string input) =>
    type == ExclusionType.Ip
        ? (IsValidIpOrCidr(input), "Must be a valid IP or CIDR (e.g. 192.168.1.0/24)")
        : (IsValidHost(input),     "Must be a valid domain (e.g. internal.corp)");
```

---

## Implementation Phases

### Phase 1 — Service Layer (Day 1)

- [ ] Create solution and WPF project (`dotnet new wpf`)
- [ ] Add NuGet: `CommunityToolkit.Mvvm`, `ModernWpfUI`, `Microsoft.Extensions.DependencyInjection`
- [ ] Implement `WarpResult` and `ExclusionType` models
- [ ] Implement `WarpCliService` — all CLI calls with 10 s timeout
- [ ] Write console smoke-test (`TestCli.cs`) that exercises every method
- [ ] Handle `FileNotFoundException` (warp-cli not found at startup)

### Phase 2 — ViewModels (Day 2)

- [ ] Implement `ExclusionItemViewModel` with `RemoveCommand`
- [ ] Implement `MainViewModel` — all commands, dual-list, `IsBusy` gating
- [ ] Wire `IWarpCliService` via DI in `App.xaml.cs`
- [ ] Unit-test ViewModels with xUnit + Moq mock of `IWarpCliService`

### Phase 3 — Main UI (Day 3)

- [ ] Build `MainWindow.xaml` with `TabControl` (IP tab + Host tab)
- [ ] Implement converters (`StatusToBrushConverter`, `BoolToVisibilityConverter`)
- [ ] Bind all controls to `MainViewModel`
- [ ] Tab header shows live count: `IP / CIDR (2)`
- [ ] `[Add]` button label and `AddExclusionCommand` parameter update with active tab
- [ ] `DispatcherTimer` auto-refresh every 30 s

### Phase 4 — Dialog & Polish (Day 4)

- [ ] Build `AddExclusionDialog.xaml` — type-aware title and validator
- [ ] Inline `⚠` validation feedback; `[Add]` disabled on bad input
- [ ] Confirmation `MessageBox` before Remove All
- [ ] Error `MessageBox` when `WarpResult.Success == false`
- [ ] Degraded state when daemon not running (grey out, show banner)
- [ ] App icon + window title

---

## Error Handling Matrix

| Scenario                      | Detection                                | UI Response                                     |
|-------------------------------|------------------------------------------|-------------------------------------------------|
| `warp-cli.exe` not found      | `FileNotFoundException` in `RunAsync`    | Startup error dialog with WARP download link    |
| Daemon not running            | Non-zero exit on `status`                | Status = "Service offline"; all buttons disabled|
| CLI call times out (> 10 s)   | `OperationCanceledException`             | MessageBox "Operation timed out"                |
| Duplicate IP/host             | Non-zero exit; stderr parsed             | Inline warning in dialog                        |
| Remove non-existent entry     | Non-zero exit                            | Silent list refresh (already gone)              |
| Insufficient privileges       | stderr contains "Access" / "denied"      | MessageBox "Try running as Administrator"       |

---

## `WarpManager.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Assets\warp.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm"                    Version="8.*" />
    <PackageReference Include="ModernWpfUI"                              Version="0.9.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.*" />
  </ItemGroup>
</Project>
```

---

## File Checklist

- [ ] `WarpManager.sln`
- [ ] `WarpManager/WarpManager.csproj`
- [ ] `Models/WarpResult.cs`
- [ ] `Models/ExclusionType.cs`
- [ ] `Services/IWarpCliService.cs`
- [ ] `Services/WarpCliService.cs`
- [ ] `ViewModels/MainViewModel.cs`
- [ ] `ViewModels/ExclusionItemViewModel.cs`
- [ ] `Views/MainWindow.xaml` + `.cs`
- [ ] `Views/AddExclusionDialog.xaml` + `.cs`
- [ ] `Converters/StatusToBrushConverter.cs`
- [ ] `Converters/BoolToVisibilityConverter.cs`
- [ ] `App.xaml` + `App.xaml.cs`
- [ ] `WarpManager.Tests/` (xUnit + Moq)
