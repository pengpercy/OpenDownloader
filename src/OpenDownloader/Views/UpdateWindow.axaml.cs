using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenDownloader.Services;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

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

    public string VersionText => $"v{Release.TagName}";
    public string ReleaseNotes => Release.Body;

    public async Task StartUpdateAsync(Window window)
    {
        IsDownloading = true;
        var updateService = new UpdateService();
        
        // Determine asset based on OS
        string assetName = "";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            // Prefer .dmg
            assetName = ".dmg";
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            assetName = ".zip"; // Or .exe if we had one
        }
        else
        {
            assetName = ".tar.gz";
        }

        var asset = Release.Assets.FirstOrDefault(a => a.Name.EndsWith(assetName, StringComparison.OrdinalIgnoreCase));
        if (asset == null)
        {
            // Fallback or error
            IsDownloading = false;
            return;
        }

        var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var destPath = Path.Combine(downloadsPath, asset.Name);

        var progress = new Progress<double>(p => DownloadProgress = p);

        try
        {
            await updateService.DownloadUpdateAsync(asset.BrowserDownloadUrl, destPath, progress);
            
            // Open file
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Process.Start("open", destPath);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Process.Start("explorer.exe", destPath);
            }
            else
            {
                Process.Start("xdg-open", destPath);
            }

            // Quit App
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            IsDownloading = false;
        }
    }
}