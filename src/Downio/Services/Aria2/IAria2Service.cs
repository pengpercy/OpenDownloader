using System.Threading.Tasks;
using Downio.Models;
using System.Collections.Generic;

namespace Downio.Services.Aria2;

public interface IAria2Service
{
    Task InitializeAsync();
    Task ShutdownAsync();
    
    // Core RPC methods
    Task<string> AddUriAsync(string url, string filename, string savePath, int split = 4);
    Task ApplyProxyAsync(string proxyType, string proxyAddress, int proxyPort, string proxyUsername, string proxyPassword);
    Task<List<DownloadTask>> GetGlobalStatusAsync();
    Task PauseAsync(string gid);
    Task PauseAllAsync();
    Task UnpauseAsync(string gid);
    Task UnpauseAllAsync();
    Task RemoveAsync(string gid);
}
