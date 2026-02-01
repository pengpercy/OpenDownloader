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
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartUpdateAsync(this);
    }
}

public partial class UpdateViewModel : ObservableObject
{
    [ObservableProperty]
    private ReleaseInfo _release = new();

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string VersionText => Release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Release.TagName : $"v{Release.TagName}";
    public string ReleaseNotes => Release.Body;

    partial void OnReleaseChanged(ReleaseInfo value)
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ReleaseNotes));
    }

    public async Task StartUpdateAsync(Window window)
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
        var destPath = Path.Combine(updateDir, asset.Name);

        var progress = new Progress<double>(p =>
        {
            DownloadProgress = p;
            var percent = (int)(p * 100);
            StatusText = string.Format(CultureInfo.CurrentCulture, GetString("UpdateStatusDownloading", "Downloading... {0}%"), percent);
        });

        try
        {
            await updateService.DownloadUpdateAsync(asset.BrowserDownloadUrl, destPath, progress);
            StatusText = GetString("UpdateStatusApplying", "Applying update...");

            var applied = await UpdateApplier.TryApplyUpdateAsync(destPath, version);
            if (!applied)
            {
                OpenDownloadedFile(destPath);
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            IsDownloading = false;
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
