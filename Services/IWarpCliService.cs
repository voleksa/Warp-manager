using WarpManager.Models;

namespace WarpManager.Services;

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

public enum WarpStatus { Connected, Connecting, Disconnected, ServiceNotRunning, Unknown }
