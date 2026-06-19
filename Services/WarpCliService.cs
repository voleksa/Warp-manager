using System.Diagnostics;
using System.IO;
using WarpManager.Models;

namespace WarpManager.Services;

public class WarpCliService : IWarpCliService
{
    private const string CliPath =
        @"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe";

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
