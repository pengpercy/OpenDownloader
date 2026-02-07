namespace Downio.Models;

public class AppSettings
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";
    public bool AutoStart { get; set; } = false;
    public string DefaultSavePath { get; set; } = string.Empty;
    public string AccentMode { get; set; } = "System";
    public string CustomAccentColor { get; set; } = string.Empty;
    public bool ExitOnClose { get; set; } = false;
    public string ProxyType { get; set; } = "HTTP";
    public string ProxyAddress { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUsername { get; set; } = string.Empty;
    public string ProxyPassword { get; set; } = string.Empty;
    public bool AutoInstallUpdates { get; set; } = false;
    public string SkipVersion { get; set; } = string.Empty;
}
