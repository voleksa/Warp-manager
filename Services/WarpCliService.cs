using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WarpManager.Models;

namespace WarpManager.Services;

public class WarpCliService : IWarpCliService
{
    private static readonly string CliPath = FindCliPath();

    private static string FindCliPath()
    {
        // 1. Dedicated registry key written by the WARP installer
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Cloudflare\Cloudflare WARP");
            if (key?.GetValue("InstallPath") is string installPath)
            {
                var candidate = Path.Combine(installPath, "warp-cli.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }

        // 2. Windows Uninstall entries (covers both 64-bit and 32-bit installer registrations)
        foreach (var hive in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(hive);
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var entry = key.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string display &&
                        display.Contains("Cloudflare WARP", StringComparison.OrdinalIgnoreCase) &&
                        entry.GetValue("InstallLocation") is string location)
                    {
                        var candidate = Path.Combine(location, "warp-cli.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }
        }

        // 3. Known install paths as a last resort
        string[] fallbacks =
        [
            @"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe",
            @"C:\Program Files (x86)\Cloudflare\Cloudflare WARP\warp-cli.exe",
        ];
        foreach (var path in fallbacks)
            if (File.Exists(path)) return path;

        return fallbacks[0]; // RunAsync will throw FileNotFoundException if missing
    }

    private async Task<WarpResult> RunAsync(params string[] args)
    {
        if (!File.Exists(CliPath))
            throw new FileNotFoundException("warp-cli.exe not found. Is Cloudflare WARP installed?", CliPath);

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

    private static List<string> ParseList(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(l => l.Trim().Split(' ', 2)[0])   // strip trailing metadata like "(CLI exclude)"
              .Where(l => l.Contains('.') || l.Contains(':'))  // skip header lines like "Excluded"
              .ToList();

    public async Task<WarpStatus> GetStatusAsync()
    {
        var r = await RunAsync("status");
        if (!r.Success) return WarpStatus.ServiceNotRunning;
        if (r.Output.Contains("Disconnected", StringComparison.OrdinalIgnoreCase)) return WarpStatus.Disconnected;
        if (r.Output.Contains("Connecting",   StringComparison.OrdinalIgnoreCase)) return WarpStatus.Connecting;
        if (r.Output.Contains("Connected",    StringComparison.OrdinalIgnoreCase)) return WarpStatus.Connected;
        return WarpStatus.Unknown;
    }

    public Task<WarpResult> ConnectAsync()    => RunAsync("connect");
    public Task<WarpResult> DisconnectAsync() => RunAsync("disconnect");

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
}
