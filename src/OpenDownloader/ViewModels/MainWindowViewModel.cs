using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDownloader.Models;
using OpenDownloader.Services;
using OpenDownloader.Services.Aria2;
using System.Collections.ObjectModel;
using Avalonia.Styling;
using Avalonia;
using System.Threading.Tasks;
using System.Linq;
using OpenDownloader.Views;
using Avalonia.Threading;
using System;

namespace OpenDownloader.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAria2Service _aria2Service;
    private readonly TaskListView _taskListView;
    private readonly SettingsView _settingsView;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty]
    private object _currentView = null!;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private string _currentTitleKey = "MenuDownloading";

    partial void OnCurrentTitleKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsSettings));
        
        // Refresh list when switching views if needed
        if (value == "MenuDownloading" || value == "MenuWaiting" || value == "MenuStopped")
        {
            _ = RefreshTaskListAsync();
        }
    }

    public bool IsDownloading => CurrentTitleKey == "MenuDownloading";
    public bool IsWaiting => CurrentTitleKey == "MenuWaiting";
    public bool IsStopped => CurrentTitleKey == "MenuStopped";
    public bool IsSettings => CurrentTitleKey == "MenuSettings";

    [ObservableProperty]
    private ObservableCollection<DownloadTask> _tasks = new();

    [ObservableProperty]
    private bool _isSettingsVisible = false;

    [ObservableProperty]
    private bool _isAddTaskVisible = false;

    [ObservableProperty]
    private string _newTaskUrl = string.Empty;

    [ObservableProperty]
    private string _newTaskName = string.Empty;

    [ObservableProperty]
    private int _newTaskChunks = 4;

    [ObservableProperty]
    private string _newTaskSavePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");

    // Settings Properties
    [ObservableProperty]
    private string _defaultSavePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");

    [ObservableProperty]
    private string _proxyAddress = string.Empty;

    [ObservableProperty]
    private int _proxyPort = 8080;

    [ObservableProperty]
    private int _proxyTypeIndex = 0; // 0: HTTP, 1: SOCKS5

    public ObservableCollection<string> ProxyTypes { get; } = new() { "HTTP", "SOCKS5" };

    [ObservableProperty]
    private string _appVersion = "0.0.0";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public string CheckUpdateButtonKey => IsCheckingForUpdates ? "BtnCheckingUpdate" : "BtnCheckUpdate";

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdateButtonKey));
    }

    // Theme & Language Options
    public record ThemeOption(string Key, string Value);
    public record LanguageOption(string Key, string Value);

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new()
    {
        new ThemeOption("ThemeDark", "Dark"),
        new ThemeOption("ThemeLight", "Light"),
        new ThemeOption("ThemeSystem", "System")
    };

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new()
    {
        new LanguageOption("LanguageSystem", "System"),
        new LanguageOption("English", "en-US"),
        new LanguageOption("中文", "zh-CN")
    };

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (value != null)
        {
            SetTheme(value.Value);
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value != null)
        {
            SetLanguage(value.Value);
            
            // Refresh ThemeOptions to trigger converter update for localized strings
            var currentTheme = SelectedTheme;
            var themes = ThemeOptions.ToList();
            ThemeOptions.Clear();
            foreach (var theme in themes)
            {
                ThemeOptions.Add(theme);
            }
            SelectedTheme = currentTheme;

            // Also Refresh LanguageOptions to translate "System" option
            var currentLang = SelectedLanguage;
            var langs = LanguageOptions.ToList();
            LanguageOptions.Clear();
            foreach (var lang in langs)
            {
                LanguageOptions.Add(lang);
            }
            SelectedLanguage = currentLang;
        }
    }

    public MainWindowViewModel()
    {
        _aria2Service = new Aria2Service();

        AppVersion = AppVersionProvider.GetCurrentVersion();
        
        // Initialize views
        _taskListView = new TaskListView();
        _settingsView = new SettingsView();
        
        ShowDownloading();
        
        // Initialize selections
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == "System") ?? ThemeOptions[2];
        SelectedLanguage = LanguageOptions.FirstOrDefault(l => l.Value == "zh-CN") ?? LanguageOptions[2];

        // Initialize Aria2 and Timer
        _ = InitializeAria2Async();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshTaskListAsync();
        _refreshTimer.Start();
    }

    private async Task InitializeAria2Async()
    {
        try 
        {
            await _aria2Service.InitializeAsync();
            await RefreshTaskListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Aria2 Init Failed: {ex.Message}");
        }
    }

    private async Task RefreshTaskListAsync()
    {
        try
        {
            var allTasks = await _aria2Service.GetGlobalStatusAsync();
            
            // Filter based on current view
            var filteredTasks = allTasks.Where(t => 
            {
                if (IsDownloading) return t.Status == "StatusDownloading" || t.Status == "StatusWaiting" || t.Status == "StatusPaused";
                if (IsWaiting) return t.Status == "StatusWaiting";
                if (IsStopped) return t.Status == "StatusStopped" || t.Status == "StatusError" || t.Status == "StatusCompleted";
                return true;
            }).ToList();

            // Sync list
            // 1. Remove missing
            for (int i = Tasks.Count - 1; i >= 0; i--)
            {
                var existing = Tasks[i];
                if (!filteredTasks.Any(t => t.Id == existing.Id))
                {
                    Tasks.RemoveAt(i);
                }
            }

            // 2. Add or Update
            foreach (var task in filteredTasks)
            {
                var existing = Tasks.FirstOrDefault(t => t.Id == task.Id);
                if (existing == null)
                {
                    Tasks.Add(task);
                }
                else
                {
                    // Update properties
                    if (existing.Status != task.Status) existing.Status = task.Status;
                    if (existing.Progress != task.Progress) existing.Progress = task.Progress;
                    if (existing.DownloadedBytes != task.DownloadedBytes) existing.DownloadedBytes = task.DownloadedBytes;
                    if (existing.TotalBytes != task.TotalBytes) existing.TotalBytes = task.TotalBytes;
                    if (existing.Speed != task.Speed) existing.Speed = task.Speed;
                    if (existing.TimeLeft != task.TimeLeft) existing.TimeLeft = task.TimeLeft;
                    if (existing.Connections != task.Connections) existing.Connections = task.Connections;
                    if (existing.Name != task.Name && task.Name != "Unknown") existing.Name = task.Name;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Refresh Failed: {ex.Message}");
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
        // Keep previous save path or default
        if (string.IsNullOrEmpty(NewTaskSavePath))
        {
            NewTaskSavePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
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
        // ... (existing code) ...
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
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
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
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
        // Remove 'v' prefix if present for clean comparison
        var currentVersion = AppVersion.TrimStart('v');
        
        ReleaseInfo? release = null;
        try
        {
            release = await updateService.CheckForUpdatesAsync(currentVersion);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
        
        if (release != null)
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
            {
                var dialog = new UpdateWindow(release);
                dialog.ShowDialog(mainWindow);
            }
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
            {
                var title = GetString("TitleUpdateCheck");
                var message = GetString("MessageNoUpdates");
                var dialog = new InfoDialog(title, message);
                dialog.ShowDialog(mainWindow);
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
            // Auto-detect filename from URL if empty
            if (string.IsNullOrWhiteSpace(NewTaskName))
            {
                if (Uri.TryCreate(NewTaskUrl, UriKind.Absolute, out var uri))
                {
                    NewTaskName = System.IO.Path.GetFileName(uri.LocalPath);
                }
            }

            await _aria2Service.AddUriAsync(NewTaskUrl, NewTaskName, NewTaskSavePath, NewTaskChunks);
            
            // Close window and clear inputs immediately for responsiveness
            IsAddTaskVisible = false;
            NewTaskUrl = string.Empty;
            NewTaskName = string.Empty;

            // Switch to downloading view if not already there
            if (CurrentTitleKey != "MenuDownloading")
            {
                ShowDownloading();
            }

            // Always refresh after a short delay to ensure aria2 has registered the task
            // and it appears in the list
            await Task.Delay(200);
            await RefreshTaskListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add Task Failed: {ex.Message}");
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
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            // Map system culture to supported languages
            if (culture.Name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
            {
                LocalizationService.SwitchLanguage("zh-CN");
            }
            else
            {
                LocalizationService.SwitchLanguage("en-US");
            }
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
            // Optimistic update for immediate UI response
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusDownloading" || task.Status == "StatusWaiting")
                {
                    task.Status = "StatusPaused";
                    task.Speed = "0 B/s"; // Clear speed display immediately
                }
            }

            // Stop the timer temporarily to avoid conflict updates
            _refreshTimer.Stop();

            await _aria2Service.PauseAllAsync();
            
            // Wait a bit for aria2 to process, then refresh real status
            await Task.Delay(500);
            await RefreshTaskListAsync();
            
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PauseAll Failed: {ex.Message}");
            _refreshTimer.Start();
            await RefreshTaskListAsync(); // Revert on error
        }
    }

    [RelayCommand]
    public async Task ResumeAll()
    {
        try
        {
            // Optimistic update
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusPaused")
                {
                    task.Status = "StatusWaiting"; // Will transition to Downloading
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
            System.Diagnostics.Debug.WriteLine($"ResumeAll Failed: {ex.Message}");
            _refreshTimer.Start();
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task DeleteTask(DownloadTask task)
    {
        if (task != null)
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
            {
                var dialog = new ConfirmDeleteDialog(task.Name);
                var result = await dialog.ShowDialog<bool>(mainWindow);
                
                if (result)
                {
                    await _aria2Service.RemoveAsync(task.Id);
                    
                    if (dialog.DeleteFile && !string.IsNullOrEmpty(task.FilePath))
                    {
                        try
                        {
                            if (System.IO.File.Exists(task.FilePath))
                            {
                                System.IO.File.Delete(task.FilePath);
                            }
                            // Also try to delete .aria2 control file if exists
                            var aria2File = task.FilePath + ".aria2";
                            if (System.IO.File.Exists(aria2File))
                            {
                                System.IO.File.Delete(aria2File);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to delete file: {ex.Message}");
                        }
                    }

                    await RefreshTaskListAsync();
                }
            }
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedTasks()
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
    public async Task ToggleTaskState(DownloadTask task)
    {
        if (task == null) return;

        if (task.Status == "StatusDownloading" || task.Status == "StatusWaiting")
        {
            await _aria2Service.PauseAsync(task.Id);
        }
        else if (task.Status == "StatusPaused")
        {
            await _aria2Service.UnpauseAsync(task.Id);
        }
        // If stopped/error, maybe restart? Aria2 doesn't have simple restart for removed tasks without re-adding.
        // We can implement retry logic later by storing task info.
        
        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowTaskDetails(DownloadTask task)
    {
        if (task != null)
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
            {
                var dialog = new TaskDetailsWindow(task);
                dialog.ShowDialog(mainWindow);
            }
        }
    }

    [RelayCommand]
    public async Task OpenFolder(DownloadTask task)
    {
        if (task == null || string.IsNullOrEmpty(task.FilePath)) return;

        var path = task.FilePath;
        var dir = System.IO.Path.GetDirectoryName(path);
        
        if (System.IO.Directory.Exists(dir))
        {
            try 
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    System.Diagnostics.Process.Start("open", $"-R \"{path}\"");
                }
                else
                {
                    System.Diagnostics.Process.Start("xdg-open", dir);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open folder failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public async Task CopyLink(DownloadTask task)
    {
        if (task == null || string.IsNullOrEmpty(task.Url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
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
            System.Diagnostics.Debug.WriteLine($"Copy link failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ShowAbout()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } mainWindow)
        {
            var dialog = new AboutWindow();
            dialog.ShowDialog(mainWindow);
        }
    }

    [RelayCommand]
    public async Task QuitApp()
    {
        // 1. Shutdown Aria2 gracefully
        await _aria2Service.ShutdownAsync();

        // 2. Close App
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
