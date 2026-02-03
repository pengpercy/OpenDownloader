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
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDownloader.Models;
using OpenDownloader.Services;
using OpenDownloader.Services.Aria2;
using OpenDownloader.Views;
using OpenDownloader.Helpers;

namespace OpenDownloader.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAria2Service _aria2Service;
    private readonly SettingsService _settingsService;
    private readonly AutoStartService _autoStartService;
    private readonly NotificationService _notificationService;
    private readonly TaskListView _taskListView;
    private readonly SettingsView _settingsView;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, string> _lastStatusByGid = new();
    private bool _isShuttingDown;
    private readonly bool _windowControlsOnLeft;

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

    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool IsMacLikeLayout => IsMacOS || (IsLinux && _windowControlsOnLeft);
    public bool IsWindowsLikeLayout => !IsMacLikeLayout;
    public bool IsNotMacOS => !IsMacLikeLayout;

    [ObservableProperty]
    private SettingsSection _selectedSettingsSection = SettingsSection.General;

    public string CurrentSettingsTitleKey => SelectedSettingsSection switch
    {
        SettingsSection.General => "SettingsGeneral",
        SettingsSection.Appearance => "SettingsAppearance",
        SettingsSection.Network => "SettingsNetwork",
        SettingsSection.Advanced => "SettingsAdvanced",
        SettingsSection.About => "SettingsAbout",
        _ => "MenuSettings"
    };

    public bool IsSettingsGeneral => SelectedSettingsSection == SettingsSection.General;
    public bool IsSettingsAppearance => SelectedSettingsSection == SettingsSection.Appearance;
    public bool IsSettingsNetwork => SelectedSettingsSection == SettingsSection.Network;
    public bool IsSettingsAdvanced => SelectedSettingsSection == SettingsSection.Advanced;
    public bool IsSettingsAbout => SelectedSettingsSection == SettingsSection.About;

    partial void OnSelectedSettingsSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(CurrentSettingsTitleKey));
        OnPropertyChanged(nameof(IsSettingsGeneral));
        OnPropertyChanged(nameof(IsSettingsAppearance));
        OnPropertyChanged(nameof(IsSettingsNetwork));
        OnPropertyChanged(nameof(IsSettingsAdvanced));
        OnPropertyChanged(nameof(IsSettingsAbout));
    }

    public Thickness SidebarToggleMargin
    {
        get
        {
            return new Thickness(16, 0, 0, 0);
        }
    }

    [ObservableProperty]
    private Thickness _macToggleMargin = new(76, 6, 0, 0);

    [ObservableProperty]
    private Thickness _titleBarToolsMargin = new(0);

    partial void OnIsPaneOpenChanged(bool value)
    {
        UpdateTitleBarToolsMargin();
    }

    public void UpdateMacTitleBarInsets(double trafficLightsRight, double titleBarHeight)
    {
        var spacing = 8d;
        var toggleSize = 32d;
        var top = Math.Max(0, (titleBarHeight - toggleSize) / 2);
        var left = Math.Max(0, trafficLightsRight + spacing);

        MacToggleMargin = new Thickness(left, top, 0, 0);
        UpdateTitleBarToolsMargin();
    }

    private void UpdateTitleBarToolsMargin()
    {
        if (!IsMacLikeLayout)
        {
            TitleBarToolsMargin = new Thickness(0);
            return;
        }

        if (IsPaneOpen)
        {
            // 当侧边栏展开时，工具栏应紧贴分栏线。
            // 由于 XAML 中 StackPanel 已有 Margin="8,0,0,0"，这里设为 0 即可。
            TitleBarToolsMargin = new Thickness(0);
        }
        else
        {
            // 当侧边栏折叠时，需要避开 Mac 风格的 Toggle 按钮。
            var compactWidth = 64d; // SplitView.CompactPaneLength
            var baseMargin = 8d;    // StackPanel 的基础 Margin
            var toggleWidth = 32d;
            var spacing = 8d;
            
            // Toggle 按钮的右边界位置
            var toggleRightEdge = MacToggleMargin.Left + toggleWidth + spacing;
            // 工具栏在不加额外 Margin 时的起始位置
            var toolbarStartWithoutExtraMargin = compactWidth + baseMargin;
            
            // 计算需要额外偏移的量
            var desiredExtraMargin = Math.Max(0, toggleRightEdge - toolbarStartWithoutExtraMargin);
            TitleBarToolsMargin = new Thickness(desiredExtraMargin, 0, 0, 0);
        }
    }

    [ObservableProperty]
    private ObservableCollection<DownloadTask> _tasks = new();

    [ObservableProperty]
    private DownloadTask? _selectedTask;

    public List<DownloadTask> SelectedTasks { get; private set; } = new();

    public void UpdateSelectedTasks(List<DownloadTask> tasks)
    {
        SelectedTasks = tasks;
        DeleteSelectedTasksCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedTasks))]
    public async Task DeleteSelectedTasks()
    {
        if (SelectedTasks.Count > 0)
        {
            var tasksToDelete = SelectedTasks.ToList(); // Clone list to avoid modification issues during enumeration

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
            {
                var dialog = new ConfirmDeleteDialog(tasksToDelete.Count == 1 ? tasksToDelete[0].Name : $"{tasksToDelete.Count} tasks");
                var result = await dialog.ShowDialog<bool>(mainWindow);
                
                if (result)
                {
                    foreach (var task in tasksToDelete)
                    {
                        await _aria2Service.RemoveAsync(task.Id);
                        
                        if (dialog.DeleteFile && !string.IsNullOrEmpty(task.FilePath))
                        {
                            try
                            {
                                if (File.Exists(task.FilePath)) File.Delete(task.FilePath);
                                var aria2File = task.FilePath + ".aria2";
                                if (File.Exists(aria2File)) File.Delete(aria2File);
                            }
                            catch { /* Ignore delete errors */ }
                        }
                    }
                    
                    SelectedTask = null;
                    await RefreshTaskListAsync();
                }
            }
        }
    }

    private bool CanDeleteSelectedTasks() => SelectedTasks.Count > 0;

    [RelayCommand]
    public async Task RefreshTasks()
    {
        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public async Task ToggleTaskState(DownloadTask? task)
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

        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowTaskDetails(DownloadTask? task)
    {
        if (task == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new TaskDetailsWindow(task);
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public Task OpenFolder(DownloadTask? task)
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
    public async Task CopyLink(DownloadTask? task)
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
    public void ShowAbout()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new AboutWindow
        {
            DataContext = this
        };
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public void ToggleMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            
            window.Activate();
        }
    }

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
    public string FeedbackUrl => "https://github.com/pengpercy/OpenDownloader/issues/new";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _updateCheckStatusText = string.Empty;

    [ObservableProperty]
    private IBrush _updateCheckStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isUpdateCheckStatusVisible;

    public string CheckUpdateButtonKey => IsCheckingForUpdates ? "BtnCheckingUpdate" : "BtnCheckUpdate";

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdateButtonKey));
    }

    // Theme & Language Options
    public record ThemeOption(string Key, string Value);
    public record LanguageOption(string Key, string Value);
    public record AccentModeOption(string Key, string Value);

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

    public ObservableCollection<AccentModeOption> AccentModeOptions { get; } =
    [
        new("LabelFollowSystem", "System"),
        new("LabelCustomAccent", "Custom")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private AccentModeOption? _selectedAccentMode;

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    partial void OnIsAutoStartEnabledChanged(bool value)
    {
        _autoStartService.SetAutoStart(value);
        _settingsService.Settings.AutoStart = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isAutoInstallUpdatesEnabled;

    partial void OnIsAutoInstallUpdatesEnabledChanged(bool value)
    {
        _settingsService.Settings.AutoInstallUpdates = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isAccentFollowSystem = true;

    [ObservableProperty]
    private Color _customAccentColor = Color.Parse("#508252");

    [ObservableProperty]
    private string _customAccentColorHex = "#508252";

    [ObservableProperty]
    private SolidColorBrush _customAccentPreviewBrush = new(Color.Parse("#508252"));

    private bool _isUpdatingAccent;

    partial void OnSelectedAccentModeChanged(AccentModeOption? value)
    {
        if (value == null) return;
        
        var mode = value.Value;
        IsAccentFollowSystem = mode == "System";
        _settingsService.Settings.AccentMode = mode;
        
        if (IsAccentFollowSystem)
        {
            _settingsService.Settings.CustomAccentColor = string.Empty;
        }
        else
        {
            _settingsService.Settings.CustomAccentColor = CustomAccentColorHex;
        }
        
        _settingsService.Save();
        ThemeAccentService.Apply(_settingsService.Settings.AccentMode, _settingsService.Settings.CustomAccentColor);
    }

    partial void OnCustomAccentColorChanged(Color value)
    {
        if (_isUpdatingAccent) return;

        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        
        _isUpdatingAccent = true;
        CustomAccentColorHex = hex;
        CustomAccentPreviewBrush = new SolidColorBrush(value);
        _isUpdatingAccent = false;

        if (IsAccentFollowSystem) return;

        _settingsService.Settings.AccentMode = "Custom";
        _settingsService.Settings.CustomAccentColor = hex;
        _settingsService.Save();
        ThemeAccentService.Apply("Custom", hex);
    }

    partial void OnCustomAccentColorHexChanged(string value)
    {
        if (_isUpdatingAccent) return;

        if (Color.TryParse(value, out var c))
        {
            CustomAccentPreviewBrush = new SolidColorBrush(c);
            _isUpdatingAccent = true;
            CustomAccentColor = c;
            _isUpdatingAccent = false;
        }

        if (IsAccentFollowSystem) return;

        _settingsService.Settings.AccentMode = "Custom";
        _settingsService.Settings.CustomAccentColor = value;
        _settingsService.Save();
        ThemeAccentService.Apply("Custom", value);
    }

    public string EmptyStateSubtitleDownloadingText
    {
        get
        {
            var template = GetString("EmptyStateSubtitleDownloading");
            var shortcut = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "⌘N" : "Ctrl+N";
            return template.Replace("⌘N", shortcut);
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null)
        {
            SetTheme(value.Value);
            _settingsService.Settings.Theme = value.Value;
            _settingsService.Save();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        SetLanguage(value.Value);
        
        _settingsService.Settings.Language = value.Value;
        _settingsService.Save();
        
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

        OnPropertyChanged(nameof(EmptyStateSubtitleDownloadingText));
        OnPropertyChanged(nameof(CheckUpdateButtonKey));
    }

    public MainWindowViewModel()
    {
        _windowControlsOnLeft = DetectWindowControlsOnLeft();
        _settingsService = new SettingsService();
        _autoStartService = new AutoStartService();
        _notificationService = new NotificationService();
        _notificationService.ToastRequested += (s, msg) => ShowToast(msg);
        _aria2Service = new Aria2Service();

        AppVersion = AppVersionProvider.GetCurrentVersion();
        
        // Initialize views
        _taskListView = new TaskListView();
        _settingsView = new SettingsView();
        
        ShowDownloading();
        
        // Initialize selections
        var savedTheme = _settingsService.Settings.Theme;
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == savedTheme) ?? ThemeOptions.FirstOrDefault(t => t.Value == "System") ?? ThemeOptions[2];

        var savedLang = _settingsService.Settings.Language;
        SelectedLanguage = LanguageOptions.FirstOrDefault(l => l.Value == savedLang) ?? LanguageOptions.FirstOrDefault(l => l.Value == "System") ?? LanguageOptions[2];

        IsAutoStartEnabled = _settingsService.Settings.AutoStart;
        IsAutoInstallUpdatesEnabled = _settingsService.Settings.AutoInstallUpdates;

        var savedAccentMode = _settingsService.Settings.AccentMode;
        SelectedAccentMode = AccentModeOptions.FirstOrDefault(a => a.Value == savedAccentMode) ?? AccentModeOptions[0];

        if (!string.IsNullOrWhiteSpace(_settingsService.Settings.DefaultSavePath))
        {
            DefaultSavePath = _settingsService.Settings.DefaultSavePath;
        }
        else
        {
            _settingsService.Settings.DefaultSavePath = DefaultSavePath;
            _settingsService.Save();
        }

        ProxyAddress = _settingsService.Settings.ProxyAddress;
        ProxyPort = _settingsService.Settings.ProxyPort;
        ProxyTypeIndex = string.Equals(_settingsService.Settings.ProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ProxyUsername = _settingsService.Settings.ProxyUsername;
        ProxyPassword = _settingsService.Settings.ProxyPassword;

        IsAccentFollowSystem = !string.Equals(_settingsService.Settings.AccentMode, "Custom", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_settingsService.Settings.CustomAccentColor))
        {
            var hex = _settingsService.Settings.CustomAccentColor;
            if (Color.TryParse(hex, out var c))
            {
                _isUpdatingAccent = true;
                CustomAccentColor = c;
                CustomAccentColorHex = hex;
                CustomAccentPreviewBrush = new SolidColorBrush(c);
                _isUpdatingAccent = false;
            }
        }
        ThemeAccentService.Apply(_settingsService.Settings.AccentMode, _settingsService.Settings.CustomAccentColor);

        // Initialize Aria2 and Timer
        _ = InitializeAria2Async();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshTaskListAsync();
        _refreshTimer.Start();
    }

    private bool DetectWindowControlsOnLeft()
    {
        if (IsMacOS) return true;
        if (!IsLinux) return false;

        try
        {
            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToUpperInvariant() ?? "";
            
            // Check for KDE Plasma
            if (desktop.Contains("KDE") || desktop.Contains("PLASMA"))
            {
                // Try Plasma 6 first
                var outputKde = TryReadProcessStdOut("kreadconfig6", "--file kwinrc --group org.kde.kwin.decoration --key ButtonsOnLeft");
                if (string.IsNullOrEmpty(outputKde))
                {
                    // Fallback to Plasma 5
                    outputKde = TryReadProcessStdOut("kreadconfig5", "--file kwinrc --group org.kde.kwin.decoration --key ButtonsOnLeft");
                }

                if (!string.IsNullOrWhiteSpace(outputKde))
                {
                    // Check if any of the standard window controls (Close, Maximize, Minimize) are on the left
                    // X: Close, A: Maximize, I: Minimize
                    var layout = outputKde.Trim().ToUpperInvariant();
                    if (layout.Contains('X') || layout.Contains('A') || layout.Contains('I'))
                    {
                        return true;
                    }
                }
                
                // If we are in KDE and detected right-side controls (or empty left side), return false.
                return false;
            }

            // Check for Xfce
            if (desktop.Contains("XFCE"))
            {
                var outputXfce = TryReadProcessStdOut("xfconf-query", "-c xfwm4 -p /general/button_layout");
                if (!string.IsNullOrWhiteSpace(outputXfce))
                {
                    // Format is usually "O|HMC" or "CHM|" where | is title
                    // O: Menu, H: Hide/Min, M: Max, C: Close
                    var layout = outputXfce.Trim().ToUpperInvariant();
                    var parts = layout.Split('|');
                    if (parts.Length > 0)
                    {
                        var leftPart = parts[0];
                        if (leftPart.Contains('C') || leftPart.Contains('M') || leftPart.Contains('H'))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            // Fallback to GNOME/GTK detection
            var output = TryReadProcessStdOut("gsettings", "get org.gnome.desktop.wm.preferences button-layout");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var raw = output.Trim().Trim('\'', '"');
                var parts = raw.Split(':', 2);
                var leftPart = parts.Length > 0 ? parts[0] : string.Empty;
                var rightPart = parts.Length > 1 ? parts[1] : string.Empty;

                static bool HasButton(string s)
                {
                    s = s.ToLowerInvariant();
                    return s.Contains("close") || s.Contains("maximize") || s.Contains("minimize");
                }

                if (HasButton(leftPart) && !HasButton(rightPart)) return true;
                if (!HasButton(leftPart) && HasButton(rightPart)) return false;
                if (HasButton(leftPart)) return true;
            }
        }
        catch
        {
            // Ignore errors, default to false (Windows-like)
        }

        return false;
    }

    private static string? TryReadProcessStdOut(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(300);
            return output;
        }
        catch
        {
            return null;
        }
    }

    // Toast Notifications
    [ObservableProperty]
    private ObservableCollection<ToastMessage> _toasts = new();

    private async void ShowToast(ToastMessage message)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Toasts.Add(message);
            // Auto remove after 3 seconds
            await Task.Delay(3000);
            Toasts.Remove(message);
        });
    }

    private async Task InitializeAria2Async()
    {
        try 
        {
            await _aria2Service.InitializeAsync();
            _ = ApplyProxySettingsAsync();
            await RefreshTaskListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Aria2 Init Failed: {ex.Message}");
            AppLog.Error(ex, "Aria2 Init Failed");
        }
    }

    [ObservableProperty]
    private string _proxyUsername = string.Empty;

    [ObservableProperty]
    private string _proxyPassword = string.Empty;

    partial void OnDefaultSavePathChanged(string value)
    {
        _settingsService.Settings.DefaultSavePath = value;
        _settingsService.Save();
    }

    partial void OnProxyAddressChanged(string value)
    {
        _settingsService.Settings.ProxyAddress = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyPortChanged(int value)
    {
        _settingsService.Settings.ProxyPort = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyTypeIndexChanged(int value)
    {
        _settingsService.Settings.ProxyType = value == 1 ? "SOCKS5" : "HTTP";
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyUsernameChanged(string value)
    {
        _settingsService.Settings.ProxyUsername = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyPasswordChanged(string value)
    {
        _settingsService.Settings.ProxyPassword = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    private async Task ApplyProxySettingsAsync()
    {
        try
        {
            var address = ProxyAddress?.Trim() ?? string.Empty;
            var port = ProxyPort;
            var type = ProxyTypeIndex == 1 ? "SOCKS5" : "HTTP";
            var user = ProxyUsername?.Trim() ?? string.Empty;
            var pass = ProxyPassword ?? string.Empty;

            await _aria2Service.ApplyProxyAsync(type, address, port, user, pass);
        }
        catch
        {
        }
    }

    private async Task RefreshTaskListAsync()
    {
        try
        {
            var allTasks = await _aria2Service.GetGlobalStatusAsync();

            foreach (var t in allTasks)
            {
                if (_lastStatusByGid.TryGetValue(t.Id, out var prev))
                {
                    if (prev != "StatusError" && t.Status == "StatusError")
                    {
                        AppLog.Warn($"Download failed: {t.Name} ({t.Id})");
                        _notificationService.ShowNotification(GetString("NotificationDownloadFailed"), t.Name, ToastType.Error);
                    }
                    else if (prev != "StatusCompleted" && t.Status == "StatusCompleted")
                    {
                        _notificationService.ShowNotification(GetString("NotificationDownloadComplete"), t.Name, ToastType.Success);
                    }
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
