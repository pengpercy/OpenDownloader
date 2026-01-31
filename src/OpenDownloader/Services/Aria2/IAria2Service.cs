using System.Threading.Tasks;
using OpenDownloader.Models;
using System.Collections.Generic;

namespace OpenDownloader.Services.Aria2;

public interface IAria2Service
{
    Task InitializeAsync();
    Task ShutdownAsync();
    
    // Core RPC methods
    Task<string> AddUriAsync(string url, string filename, string savePath, int split = 4);
    Task<List<DownloadTask>> GetGlobalStatusAsync();
    Task PauseAsync(string gid);
    Task PauseAllAsync();
    Task UnpauseAsync(string gid);
    Task UnpauseAllAsync();
    Task RemoveAsync(string gid);
}
