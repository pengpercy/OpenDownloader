using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDownloader.Models;
using OpenDownloader.Services;
using OpenDownloader.Services.Aria2;
using OpenDownloader.Views;

namespace OpenDownloader.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAria2Service _aria2Service;
    private readonly TaskListView _taskListView;
    private readonly SettingsView _settingsView;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, string> _lastStatusByGid = new();
    private bool _isShuttingDown;

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

    public Thickness TitleBarToolbarMargin
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new Thickness(0, 0, 140, 0);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new Thickness(0);
            }

            return new Thickness(0, 0, 20, 0);
        }
    }

    [ObservableProperty]
    private ObservableCollection<DownloadTask> _tasks = new();

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isAddTaskVisible;

    [ObservableProperty]
    private string _newTaskUrl = string.Empty;

    [ObservableProperty]
    private string _newTaskName = string.Empty;

    [ObservableProperty]
    private int _newTaskChunks = 4;

    [ObservableProperty]
    private string _newTaskSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Settings Properties
    [ObservableProperty]
    private string _defaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    [ObservableProperty]
    private string _proxyAddress = string.Empty;

    [ObservableProperty]
    private int _proxyPort = 8080;

    [ObservableProperty]
    private int _proxyTypeIndex; // 0: HTTP, 1: SOCKS5

    public ObservableCollection<string> ProxyTypes { get; } = ["HTTP", "SOCKS5"];

    [ObservableProperty]
    private string _appVersion = "0.0.0";

    public string RepositoryUrl => "https://github.com/pengpercy/OpenDownloader";

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

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new("ThemeDark", "Dark"),
        new("ThemeLight", "Light"),
        new("ThemeSystem", "System")
    ];

    public ObservableCollection<LanguageOption> LanguageOptions { get; } =
    [
        new("LanguageSystem", "System"),
        new("English", "en-US"),
        new("中文", "zh-CN")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null)
        {
            SetTheme(value.Value);
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
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
        _refreshTimer.Tick += async (_, _) => await RefreshTaskListAsync();
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
            Debug.WriteLine($"Aria2 Init Failed: {ex.Message}");
            AppLog.Error(ex, "Aria2 Init Failed");
        }
    }

    private async Task RefreshTaskListAsync()
    {
        try
        {
            var allTasks = await _aria2Service.GetGlobalStatusAsync();

            foreach (var t in allTasks)
            {
                if (_lastStatusByGid.TryGetValue(t.Id, out var prev) && prev != "StatusError" && t.Status == "StatusError")
                {
                    AppLog.Warn($"Download failed: {t.Name} ({t.Id})");
                }

                _lastStatusByGid[t.Id] = t.Status;
            }

            var activeIds = new HashSet<string>(allTasks.Select(t => t.Id));
            var idsToRemove = _lastStatusByGid.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in idsToRemove)
            {
                _lastStatusByGid.Remove(id);
            }
            
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
                if (filteredTasks.All(t => t.Id != existing.Id))
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
            Debug.WriteLine($"Refresh Failed: {ex.Message}");
            AppLog.Error(ex, "Refresh task list failed");
        }
    }
}
