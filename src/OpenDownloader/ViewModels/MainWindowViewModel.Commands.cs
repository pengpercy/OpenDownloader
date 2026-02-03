using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDownloader.Models;
using OpenDownloader.Services;
using OpenDownloader.Views;

namespace OpenDownloader.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    public void OpenRepoUrl()
    {
        OpenUrl(RepositoryUrl);
    }

    [RelayCommand]
    public void OpenFeedbackUrl()
    {
        OpenUrl(FeedbackUrl);
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenUrl Failed: {ex.Message}");
            AppLog.Error(ex, $"OpenUrl failed for {url}");
        }
    }

    [RelayCommand]
    public void TogglePane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    [RelayCommand]
    public void ShowDownloading()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuDownloading";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowWaiting()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuWaiting";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowStopped()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuStopped";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public async Task ShowSettings()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        SelectedSettingsSection = SettingsSection.General;

        var window = new SettingsWindow
        {
            DataContext = this
        };

        await window.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public void SelectSettingsSection(SettingsSection section)
    {
        SelectedSettingsSection = section;
    }

    [RelayCommand]
    public void ShowAddTask()
    {
        NewTaskUrl = string.Empty;
        NewTaskName = string.Empty;
        NewTaskChunks = 4;
        if (string.IsNullOrEmpty(NewTaskSavePath))
        {
            NewTaskSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var dialog = new AddTaskWindow(this);
            dialog.ShowDialog(mainWindow);
        }
    }

    [RelayCommand]
    public void CancelAddTask()
    {
        IsAddTaskVisible = false;
        NewTaskUrl = string.Empty;
        NewTaskName = string.Empty;
    }

    [RelayCommand]
    public async Task ChooseSavePath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                NewTaskSavePath = folders[0].Path.LocalPath;
            }
        }
    }

    [RelayCommand]
    public async Task ChooseDefaultSavePath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Default Download Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                DefaultSavePath = folders[0].Path.LocalPath;
            }
        }
    }

    [RelayCommand]
    public async Task CheckForUpdates(object? parameter)
    {
        if (IsCheckingForUpdates) return;
        
        var isFromAbout = parameter?.ToString() == "About";
        
        if (isFromAbout)
        {
            IsUpdateCheckStatusVisible = false;
        }

        IsCheckingForUpdates = true;
        var updateService = new UpdateService();
        var currentVersion = AppVersion.TrimStart('v');

        ReleaseInfo? release = null;
        try
        {
            release = await updateService.CheckForUpdatesAsync(currentVersion);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            AppLog.Error(ex, "Update check failed");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        if (release != null)
        {
            var dialog = new UpdateWindow(release, _settingsService);
            await dialog.ShowDialog(mainWindow);
        }
        else
        {
            if (isFromAbout)
            {
                UpdateCheckStatusText = GetString("MessageNoUpdates");
                UpdateCheckStatusBrush = Brushes.Gray;
                IsUpdateCheckStatusVisible = true;
                
                // Hide after 5 seconds
                _ = Task.Delay(5000).ContinueWith(_ => IsUpdateCheckStatusVisible = false, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                var title = GetString("TitleUpdateCheck");
                var message = GetString("MessageNoUpdates");
                var dialog = new InfoDialog(title, message);
                await dialog.ShowDialog(mainWindow);
            }
        }
    }

    private static string GetString(string key)
    {
        if (Application.Current != null && Application.Current.TryGetResource(key, null, out var resource) && resource is string str)
        {
            return str;
        }
        return key;
    }

    [RelayCommand]
    public async Task StartDownload()
    {
        if (string.IsNullOrWhiteSpace(NewTaskUrl)) return;

        try
        {
            if (string.IsNullOrWhiteSpace(NewTaskName))
            {
                if (Uri.TryCreate(NewTaskUrl, UriKind.Absolute, out var uri))
                {
                    NewTaskName = Path.GetFileName(uri.LocalPath);
                }
            }

            await _aria2Service.AddUriAsync(NewTaskUrl, NewTaskName, NewTaskSavePath, NewTaskChunks);

            IsAddTaskVisible = false;
            NewTaskUrl = string.Empty;
            NewTaskName = string.Empty;

            if (CurrentTitleKey != "MenuDownloading")
            {
                ShowDownloading();
            }

            await Task.Delay(200);
            await RefreshTaskListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Add Task Failed: {ex.Message}");
            AppLog.Error(ex, "Add task failed");
        }
    }

    [RelayCommand]
    public void SetTheme(string theme)
    {
        var app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }
    }

    [RelayCommand]
    public void SetLanguage(string lang)
    {
        if (lang == "System")
        {
            var culture = CultureInfo.CurrentCulture;
            LocalizationService.SwitchLanguage(culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-CN"
                : "en-US");
        }
        else
        {
            LocalizationService.SwitchLanguage(lang);
        }
    }

    [RelayCommand]
    public async Task PauseAll()
    {
        try
        {
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusDownloading" || task.Status == "StatusWaiting")
                {
                    task.Status = "StatusPaused";
                    task.Speed = "0 B/s";
                }
            }

            _refreshTimer.Stop();

            await _aria2Service.PauseAllAsync();

            await Task.Delay(500);
            await RefreshTaskListAsync();

            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PauseAll Failed: {ex.Message}");
            AppLog.Error(ex, "Pause all failed");
            _refreshTimer.Start();
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task ResumeAll()
    {
        try
        {
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusPaused")
                {
                    task.Status = "StatusWaiting";
                }
            }

            _refreshTimer.Stop();

            await _aria2Service.UnpauseAllAsync();

            await Task.Delay(500);
            await RefreshTaskListAsync();

            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ResumeAll Failed: {ex.Message}");
            AppLog.Error(ex, "Resume all failed");
            _refreshTimer.Start();
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task DeleteTask(DownloadTask? task)
    {
        if (task == null) return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        var dialog = new ConfirmDeleteDialog(task.Name);
        var result = await dialog.ShowDialog<bool>(mainWindow);

        if (!result) return;

        await _aria2Service.RemoveAsync(task.Id);

        if (dialog.DeleteFile && !string.IsNullOrEmpty(task.FilePath))
        {
            try
            {
                if (File.Exists(task.FilePath))
                {
                    File.Delete(task.FilePath);
                }
                var aria2File = task.FilePath + ".aria2";
                if (File.Exists(aria2File))
                {
                    File.Delete(aria2File);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete file: {ex.Message}");
                AppLog.Error(ex, $"Failed to delete file for task: {task.Name} ({task.Id})");
            }
        }

        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public void QuitApp()
    {
        _ = ShutdownServicesAsync();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }


    public async Task ShutdownServicesAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            await Task.Run(() => _aria2Service.ShutdownAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Shutdown services failed");
        }
    }
}
