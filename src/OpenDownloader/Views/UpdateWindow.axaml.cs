using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenDownloader.Services;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using System.Globalization;

namespace OpenDownloader.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateViewModel _viewModel;

    public UpdateWindow()
    {
        InitializeComponent();
        _viewModel = new UpdateViewModel();
        DataContext = _viewModel;
    }

    public UpdateWindow(ReleaseInfo release) : this()
    {
        _viewModel.Release = release;
        _viewModel.CurrentVersion = AppVersionProvider.GetCurrentVersion();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        // Change window size when starting download
        this.Width = 480;
        this.Height = 240;
        this.MinWidth = 480;
        this.MinHeight = 240;
        
        // Center window again after resize
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        await _viewModel.StartDownloadAsync(this);
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyUpdateAndRelaunchAsync();
    }
}

public partial class UpdateViewModel : ObservableObject
{
    [ObservableProperty]
    private ReleaseInfo _release = new();

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private bool _isReadyToInstall = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    public string VersionText => Release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Release.TagName : $"v{Release.TagName}";
    public string ReleaseNotes => Release.Body;

    public string UpdateFromToText
    {
        get
        {
            var from = CurrentVersion.StartsWith("v") ? CurrentVersion : $"v{CurrentVersion}";
            var to = VersionText;
            return string.Format(CultureInfo.CurrentCulture, GetString("UpdateFromTo", "Update from {0} to {1}"), from, to);
        }
    }

    partial void OnReleaseChanged(ReleaseInfo value)
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(UpdateFromToText));
    }

    partial void OnCurrentVersionChanged(string value)
    {
        OnPropertyChanged(nameof(UpdateFromToText));
    }

    private string? _downloadedFilePath;

    public async Task StartDownloadAsync(Window window)
    {
        if (IsDownloading) return;

        IsDownloading = true;
        StatusText = GetString("UpdateStatusPreparing", "Preparing...");
        DownloadProgress = 0;

        var updateService = new UpdateService();
        var asset = UpdateAssetSelector.SelectPreferredAsset(Release);
        if (asset is null)
        {
            IsDownloading = false;
            StatusText = GetString("UpdateStatusNoPackage", "No compatible update package found.");
            return;
        }

        var version = Release.TagName.TrimStart('v');
        var updateDir = Path.Combine(Path.GetTempPath(), "OpenDownloader", "updates", version);
        Directory.CreateDirectory(updateDir);
        _downloadedFilePath = Path.Combine(updateDir, asset.Name);

        var progress = new Progress<double>(p =>
        {
            DownloadProgress = p;
            var percent = (int)(p * 100);
            StatusText = string.Format(CultureInfo.CurrentCulture, GetString("UpdateStatusDownloading", "Downloading... {0}%"), percent);
        });

        try
        {
            await updateService.DownloadUpdateAsync(asset.BrowserDownloadUrl, _downloadedFilePath, progress);
            IsReadyToInstall = true;
            StatusText = GetString("LabelUpdateReady", "Update downloaded successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            IsDownloading = false;
            StatusText = GetString("UpdateStatusFailed", "Update failed.");
        }
    }

    public async Task ApplyUpdateAndRelaunchAsync()
    {
        if (string.IsNullOrEmpty(_downloadedFilePath)) return;

        try
        {
            StatusText = GetString("UpdateStatusApplying", "Applying update...");
            var version = Release.TagName.TrimStart('v');
            var applied = await UpdateApplier.TryApplyUpdateAsync(_downloadedFilePath, version);
            if (!applied)
            {
                OpenDownloadedFile(_downloadedFilePath);
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Apply update failed: {ex.Message}");
            StatusText = GetString("UpdateStatusFailed", "Update failed.");
        }
    }

    private static void OpenDownloadedFile(string path)
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                Process.Start("xdg-open", path);
            }
        }
        catch
        {
        }
    }

    private static string GetString(string key, string fallback)
    {
        if (Application.Current != null && Application.Current.TryGetResource(key, null, out var resource) && resource is string str)
        {
            return str;
        }
        return fallback;
    }
}
