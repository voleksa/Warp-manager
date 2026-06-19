# WARP Manager

A minimal WPF desktop app for managing [Cloudflare WARP](https://1.1.1.1/) split-tunnel exclusions on Windows.

![Screenshot](docs/screenshot.png)

## Features

- Connect / disconnect WARP with one click
- Live status indicator (Connected / Connecting / Disconnected)
- View, add, and remove IP/CIDR and host/domain exclusions
- Auto-refresh every 30 seconds

## Requirements

- Windows 10/11
- [Cloudflare WARP](https://1.1.1.1/) installed at its default path:  
  `C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe`
- .NET 8 SDK (build only; the published exe is self-contained)

## Build & Run

```powershell
# Run from source
dotnet run --project WarpManager.csproj

# Publish a self-contained exe
dotnet publish -c Release -r win-x64 --self-contained
```

## Tech Stack

| | |
|---|---|
| UI framework | WPF / .NET 8 |
| Styling | [ModernWpfUI](https://github.com/Kinnara/ModernWpf) 0.9 |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8 |
| DI | Microsoft.Extensions.DependencyInjection 8 |

## Architecture

MVVM throughout — no business logic in code-behind. All WARP operations go through `WarpCliService`, which shells out to `warp-cli.exe` with a 10-second timeout per call.

```
App.xaml.cs          — DI wiring (singleton: service, VM, window)
Services/            — IWarpCliService / WarpCliService (CLI adapter)
ViewModels/          — MainViewModel, ExclusionItemViewModel
Views/               — MainWindow, AddExclusionDialog
Models/              — WarpResult, ExclusionType
Converters/          — StatusToBrushConverter, BoolToVisibilityConverter
```
