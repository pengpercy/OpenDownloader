using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    public void ShowSettings()
    {
        IsSettingsVisible = true;
        CurrentTitleKey = "MenuSettings";
        CurrentView = _settingsView;
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
        IsAddTaskVisible = true;
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
    public async Task CheckForUpdates()
    {
        if (IsCheckingForUpdates) return;
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
            var dialog = new UpdateWindow(release);
            await dialog.ShowDialog(mainWindow);
        }
        else
        {
            var title = GetString("TitleUpdateCheck");
            var message = GetString("MessageNoUpdates");
            var dialog = new InfoDialog(title, message);
            await dialog.ShowDialog(mainWindow);
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
    private async Task DeleteSelectedTasks()
    {
        if (SelectedTask != null)
        {
            await _aria2Service.RemoveAsync(SelectedTask.Id);
            SelectedTask = null;
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshTasks()
    {
        await RefreshTaskListAsync();
    }

    [ObservableProperty]
    private DownloadTask? _selectedTask;

    [RelayCommand]
    public async Task ToggleTaskState(DownloadTask? task)
    {
        if (task == null) return;

        switch (task.Status)
        {
            case "StatusDownloading":
            case "StatusWaiting":
                await _aria2Service.PauseAsync(task.Id);
                break;
            case "StatusPaused":
                await _aria2Service.UnpauseAsync(task.Id);
                break;
        }

        await RefreshTaskListAsync();
    }

    [RelayCommand]
    private void ShowTaskDetails(DownloadTask? task)
    {
        if (task == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new TaskDetailsWindow(task);
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    private Task OpenFolder(DownloadTask? task)
    {
        if (task == null || string.IsNullOrEmpty(task.FilePath)) return Task.CompletedTask;

        var path = task.FilePath;
        var dir = Path.GetDirectoryName(path);

        if (!Directory.Exists(dir)) return Task.CompletedTask;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"-R \"{path}\"");
            }
            else
            {
                Process.Start("xdg-open", dir);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open folder failed: {ex.Message}");
            AppLog.Error(ex, $"Open folder failed: {task.Name} ({task.Id})");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CopyLink(DownloadTask? task)
    {
        if (task == null || string.IsNullOrEmpty(task.Url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
            {
                var clipboard = mainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(task.Url);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy link failed: {ex.Message}");
            AppLog.Error(ex, $"Copy link failed: {task.Name} ({task.Id})");
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new AboutWindow
        {
            DataContext = this
        };
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    private void QuitApp()
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
