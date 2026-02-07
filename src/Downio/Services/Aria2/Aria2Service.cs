using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Downio.Models;

namespace Downio.Services.Aria2;

public class Aria2Service : IAria2Service, IDisposable
{
    private Process? _aria2Process;
    private JsonRpcClient? _rpcClient;
    private const int RpcPort = 16800;
    private const string RpcSecret = "DownioSecret"; // In prod, generate random or user config
    private string _configDir = string.Empty;
    private readonly ConcurrentDictionary<string, int> _splitCache = new();

    public async Task InitializeAsync()
    {
        // 1. Setup Config Directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configDir = Path.Combine(appData, "Downio");
        Directory.CreateDirectory(_configDir);

        var sessionFile = Path.Combine(_configDir, "aria2.session");
        if (!File.Exists(sessionFile))
        {
            File.WriteAllText(sessionFile, "");
        }

        var logFile = Path.Combine(_configDir, "aria2.log");

        // 2. Locate Binary
        var binaryPath = GetBinaryPath();
        if (!File.Exists(binaryPath))
        {
            // Fallback or error
            Debug.WriteLine($"Aria2 binary not found at: {binaryPath}");
            AppLog.Warn($"Aria2 binary not found at: {binaryPath}");
            // Attempt to find in PATH if local binary missing?
            // For now, assume it exists as we packaged it.
        }
        
        // Ensure executable permission on Linux/macOS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Process.Start("chmod", $"+x \"{binaryPath}\"")?.WaitForExit();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"Failed to chmod aria2 binary: {binaryPath}");
            }
        }

        // 3. Start Process
        var args = new List<string>
        {
            "--enable-rpc=true",
            $"--rpc-listen-port={RpcPort}",
            $"--rpc-secret={RpcSecret}",
            "--rpc-allow-origin-all=true",
            "--rpc-listen-all=true", // Listen on all interfaces if needed, usually localhost is fine but 'all' avoids binding issues sometimes
            $"--save-session={sessionFile}",
            $"--input-file={sessionFile}",
            $"--log={logFile}",
            "--log-level=warn",
            "--max-concurrent-downloads=5",
            "--max-connection-per-server=16",
            "--split=16",
            "--min-split-size=1M",
            "--continue=true",
            "--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.93 Safari/537.36"
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try 
        {
            _aria2Process = new Process { StartInfo = startInfo };
            _aria2Process.OutputDataReceived += (_, _) => { };
            _aria2Process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    AppLog.Warn($"aria2: {e.Data}");
                }
            };
            _aria2Process.Start();
            _aria2Process.BeginOutputReadLine();
            _aria2Process.BeginErrorReadLine();
            
            Debug.WriteLine($"Aria2 started. PID: {_aria2Process.Id}");
            AppLog.Info($"Aria2 started. PID: {_aria2Process.Id}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start aria2: {ex.Message}");
            AppLog.Error(ex, "Failed to start aria2");
            throw;
        }

        // 4. Init Client
        _rpcClient = new JsonRpcClient($"http://localhost:{RpcPort}/jsonrpc", RpcSecret);
        
        // Wait for it to be ready?
        await Task.Delay(1000);
    }

    public Task ShutdownAsync()
    {
        if (_rpcClient != null)
        {
            var client = _rpcClient;
            _rpcClient = null;
            _ = TryShutdownRpcAsync(client);
        }

        if (_aria2Process != null && !_aria2Process.HasExited)
        {
            try
            {
                _aria2Process.Kill(entireProcessTree: true);
            }
            catch
            {
                try
                {
                    _aria2Process.Kill();
                }
                catch
                {
                }
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TryShutdownRpcAsync(JsonRpcClient client)
    {
        try
        {
            await client.InvokeAsync<string>("shutdown").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to shutdown aria2 via RPC");
        }
    }

    private string GetBinaryPath()
    {
        // 1. Try to find next to the app executable (for development)
        var basePath = AppContext.BaseDirectory;
        
        // In Debug: .../bin/Debug/net10.0/
        // Assets is copied to .../bin/Debug/net10.0/Assets/Binaries/...
        
        string platform = "";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => "x64" // Fallback
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            platform = "darwin";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            platform = "linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform = "win32";
            if (arch == "x64") arch = "x64"; 
            else arch = "ia32"; // We have ia32 and x64 for win
        }

        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aria2c.exe" : "aria2c";
        
        // 2. Standard published path (Assets/Binaries/...)
        var standardPath = Path.Combine(basePath, "Assets", "Binaries", platform, arch, binaryName);
        if (File.Exists(standardPath)) return standardPath;

        // 3. MacOS App Bundle path adjustment
        // When running in .app bundle, BaseDirectory is .../Contents/MacOS/
        // But our package script puts assets in .../Contents/MacOS/Assets/Binaries/darwin/{arch}/aria2c
        // which matches the standardPath above.
        // However, if we stripped the 'darwin/{arch}' structure in packaging to simplify, we need to check.
        // Let's check a flattened structure too just in case.
        
        // 4. Try relative to executable if running as single file or special layout
        // For macOS packaged app, sometimes we need to be explicit.
        
        // If the path above failed, let's log and return it anyway or try alternatives.
        // On macOS .app, the resources might be in ../Resources? No, we put them in MacOS/Assets.
        
        return standardPath;
    }

    public async Task<string> AddUriAsync(string url, string filename, string savePath, int split = 4, IDictionary<string, string>? extraOptions = null)
    {
        if (_rpcClient == null) return string.Empty;

        var options = new Dictionary<string, string>
        {
            { "dir", savePath },
            { "split", split.ToString() },
            { "max-connection-per-server", "16" }, // Usually we want max connections to be higher or equal to split
            { "min-split-size", "1M" }
        };
        
        if (!string.IsNullOrEmpty(filename))
        {
            options["out"] = filename;
        }
        
        if (extraOptions != null)
        {
            foreach (var kv in extraOptions)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (kv.Value == null) continue;
                options[kv.Key] = kv.Value;
            }
        }

        // Params: [ [urls], options ]
        var gid = await _rpcClient.InvokeAsync<string>("addUri", new[] { url }, options);
        if (!string.IsNullOrWhiteSpace(gid))
        {
            _splitCache[gid] = split;
        }
        return gid ?? string.Empty;
    }

    public async Task ApplyProxyAsync(string proxyType, string proxyAddress, int proxyPort, string proxyUsername, string proxyPassword)
    {
        if (_rpcClient == null) return;

        var options = new Dictionary<string, string>();

        var address = proxyAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address) || proxyPort <= 0)
        {
            options["all-proxy"] = string.Empty;
            options["all-proxy-user"] = string.Empty;
            options["all-proxy-passwd"] = string.Empty;
        }
        else
        {
            var scheme = string.Equals(proxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
            options["all-proxy"] = $"{scheme}://{address}:{proxyPort}";
            options["all-proxy-user"] = proxyUsername?.Trim() ?? string.Empty;
            options["all-proxy-passwd"] = proxyPassword ?? string.Empty;
        }

        try
        {
            await _rpcClient.InvokeAsync<string>("changeGlobalOption", options).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task<List<DownloadTask>> GetGlobalStatusAsync()
    {
        if (_rpcClient == null) return new List<DownloadTask>();

        var tasks = new List<DownloadTask>();

        // We need to fetch Active, Waiting, and Stopped tasks
        // tellActive, tellWaiting, tellStopped
        
        var active = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellActive");
        var waiting = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellWaiting", 0, 100);
        var stopped = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellStopped", 0, 100);

        var gids = Enumerable.Empty<string>();
        if (active != null) gids = gids.Concat(active.Select(s => s.Gid));
        if (waiting != null) gids = gids.Concat(waiting.Select(s => s.Gid));
        if (stopped != null) gids = gids.Concat(stopped.Select(s => s.Gid));
        _ = WarmSplitCacheAsync(gids);

        if (active != null) tasks.AddRange(active.Select(MapToDownloadTask));
        if (waiting != null) tasks.AddRange(waiting.Select(MapToDownloadTask));
        if (stopped != null) tasks.AddRange(stopped.Select(MapToDownloadTask));

        return tasks;
    }

    public async Task PauseAsync(string gid)
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("pause", gid);
    }

    public async Task PauseAllAsync()
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("pauseAll");
    }

    public async Task UnpauseAsync(string gid)
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("unpause", gid);
    }

    public async Task UnpauseAllAsync()
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("unpauseAll");
    }

    public async Task RemoveAsync(string gid)
    {
        if (_rpcClient == null) return;
        // If active/waiting -> remove
        // If stopped/error -> removeDownloadResult
        
        try 
        {
             await _rpcClient.InvokeAsync<string>("remove", gid);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to remove task via aria2.remove, fallback to removeDownloadResult: {gid}");
            // Try removeDownloadResult if remove fails (e.g. task is complete/error)
            await _rpcClient.InvokeAsync<string>("removeDownloadResult", gid);
        }
    }

    private string GetString(string key, string defaultValue)
    {
        if (Application.Current != null && Application.Current.TryFindResource(key, out var resource) && resource is string str)
        {
            return str;
        }
        return defaultValue;
    }

    private DownloadTask MapToDownloadTask(Aria2TaskStatus status)
    {
        long.TryParse(status.TotalLength, out var total);
        long.TryParse(status.CompletedLength, out var completed);
        long.TryParse(status.DownloadSpeed, out var speedVal);

        double progress = total > 0 ? (double)completed / total : 0;
        
        // Map Status
        var taskStatus = status.Status switch
        {
            "active" => "StatusDownloading",
            "waiting" => "StatusWaiting",
            "paused" => "StatusPaused",
            "complete" => "StatusCompleted", // We map this to Stopped in current UI or maybe Completed
            "error" => "StatusError",
            "removed" => "StatusStopped",
            _ => "StatusStopped"
        };

        // Name, Path, Url
        var name = "Unknown";
        var filePath = "";
        var url = "";

        if (status.Files.Any())
        {
            var file = status.Files.First();
            filePath = file.Path;
            if (!string.IsNullOrEmpty(filePath))
            {
                name = Path.GetFileName(filePath);
            }
            
            if (file.Uris.Any())
            {
                url = file.Uris.First().Uri;
            }
        }

        // Time Left
        string timeLeft = "";
        
        if (taskStatus == "StatusPaused" || taskStatus == "StatusWaiting" || taskStatus == "StatusStopped" || taskStatus == "StatusError")
        {
             timeLeft = ""; // No time left for non-active tasks
        }
        else if (speedVal > 0)
        {
            var remaining = total - completed;
            var seconds = remaining / speedVal;
            var ts = TimeSpan.FromSeconds(seconds);
            
            var strMoreThanOneDay = GetString("TimeMoreThanOneDay", "> 1 Day");
            var strDays = GetString("TimeDays", "d");
            var strHours = GetString("TimeHours", "h");
            var strMinutes = GetString("TimeMinutes", "m");
            var strSeconds = GetString("TimeSeconds", "s");

            if (ts.TotalHours >= 24)
            {
                timeLeft = strMoreThanOneDay;
            }
            else
            {
                // Format: mm:ss or hh:mm:ss
                // We need to construct manually to use localized unit strings
                if (ts.TotalHours >= 1)
                {
                    timeLeft = $"{(int)ts.TotalHours}{strHours} {ts.Minutes}{strMinutes} {ts.Seconds}{strSeconds}";
                }
                else
                {
                    timeLeft = $"{ts.Minutes}{strMinutes} {ts.Seconds}{strSeconds}";
                }
            }
        }
        else if (completed == total && total > 0)
        {
            timeLeft = GetString("TimeDone", "Done");
        }
        else
        {
             timeLeft = "--"; // Calculating or stalled
        }

        // Connections
        int.TryParse(status.NumConnections, out var connections);
        if (taskStatus == "StatusDownloading" && connections == 0 && speedVal > 0)
        {
            // Fallback: if downloading but connections report 0, assume at least 1 (e.g. single HTTP stream)
            // or maybe aria2 hasn't updated stats yet.
            // But usually aria2 reports 1 for single connection.
            // If it's HTTP/FTP, numConnections should be valid.
            // If it's BitTorrent, numConnections is peers.
            // Let's trust aria2 but if 0 while downloading, maybe default to 1 for display consistency if speed > 0
            connections = 1;
        }

        var split = connections > 0 ? connections : 1;
        if (_splitCache.TryGetValue(status.Gid, out var cachedSplit) && cachedSplit > 0)
        {
            split = cachedSplit;
        }

        return new DownloadTask
        {
            Id = status.Gid,
            Name = name,
            TotalBytes = total,
            DownloadedBytes = completed,
            Progress = progress,
            Speed = FormatSpeed(speedVal),
            Status = taskStatus,
            TimeLeft = timeLeft,
            Connections = connections,
            Split = split,
            FilePath = filePath,
            Url = url
        };
    }

    private async Task WarmSplitCacheAsync(IEnumerable<string> gids)
    {
        if (_rpcClient == null) return;

        var unique = gids.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
        if (unique.Count == 0) return;

        foreach (var gid in unique)
        {
            if (_splitCache.ContainsKey(gid)) continue;

            try
            {
                var options = await _rpcClient.InvokeAsync<Dictionary<string, string>>("getOption", gid);
                if (options != null && options.TryGetValue("split", out var splitStr) && int.TryParse(splitStr, out var split) && split > 0)
                {
                    _splitCache[gid] = split;
                }
                else
                {
                    _splitCache.TryAdd(gid, 1);
                }
            }
            catch
            {
            }
        }
    }

    private string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / 1024.0 / 1024.0:F1} MB/s";
    }

    private string FormatSpeedWithUnit(long bytesPerSec)
    {
        // Just the number and unit, but logic is same as FormatSpeed currently
        // If we want to separate, we can. For now, FormatSpeed returns "10.5 MB/s"
        // The UI requirement is "↓ 10.8 MB/s". We add the arrow in XAML.
        return FormatSpeed(bytesPerSec);
    }

    public void Dispose()
    {
        try
        {
            if (_aria2Process != null && !_aria2Process.HasExited)
            {
                try
                {
                    _aria2Process.Kill(entireProcessTree: true);
                }
                catch
                {
                    _aria2Process.Kill();
                }
            }
        }
        catch
        {
        }

        _aria2Process?.Dispose();
    }
}

// Helper DTOs for JSON Deserialization
public class Aria2TaskStatus
{
    public string Gid { get; set; } = "";
    public string Status { get; set; } = "";
    public string TotalLength { get; set; } = "0";
    public string CompletedLength { get; set; } = "0";
    public string DownloadSpeed { get; set; } = "0";
    public string NumConnections { get; set; } = "0";
    public List<Aria2File> Files { get; set; } = new();
}

public class Aria2File
{
    public string Path { get; set; } = "";
    public List<Aria2Uri> Uris { get; set; } = new();
}

public class Aria2Uri
{
    public string Uri { get; set; } = "";
    public string Status { get; set; } = "";
}
