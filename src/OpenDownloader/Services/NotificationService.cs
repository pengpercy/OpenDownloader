using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using OpenDownloader.Models;

namespace OpenDownloader.Services;

public class NotificationService
{
    public event EventHandler<ToastMessage>? ToastRequested;

    public void ShowNotification(string title, string message, ToastType type = ToastType.Info)
    {
        // Always show in-app Toast notification
        ToastRequested?.Invoke(this, new ToastMessage { Title = title, Message = message, Type = type });

        // Always show native system notification
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacNotification(title, message);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindowsNotification(title, message);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ShowLinuxNotification(title, message);
        }
        // Linux support can be added with notify-send
    }

    private bool IsMainWindowActive()
    {
        if (Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.IsActive == true;
        }

        return false;
    }

// --- Windows 混合实现 (自动适配 Win7 和 Win10+) ---
    private static void ShowWindowsNotification(string title, string message)
    {
        // 获取 Windows 主版本号
        var major = Environment.OSVersion.Version.Major;

        // Windows 10 (Major=10) 及以上使用现代 Toast
        // Windows 7/8 (Major=6) 使用传统气泡
        if (major >= 10)
        {
            SendWindows10Toast(title, message);
        }
        else
        {
            SendWindows7Balloon(title, message);
        }
    }

    // 方案 A: Windows 7 兼容方案 (基于 System.Windows.Forms 动态编译)
    private static void SendWindows7Balloon(string title, string message)
    {
        // 注意：我在最后加了 Start-Sleep -s 5
        // 如果不加等待，PowerShell 进程会立即结束，NotifyIcon 会被瞬间销毁，导致气泡闪退或不显示。
        string psScript = $@"
$code = @'
using System;
using System.Windows.Forms;
using System.Drawing;
public class LegacyToast {{
    public static void Show(string title, string message) {{
        var icon = new NotifyIcon();
        // 使用系统默认信息图标
        icon.Icon = SystemIcons.Information; 
        icon.Visible = true;
        icon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        // 这里不能立刻 Dispose，否则图标消失气泡也会消失
        // 实际逻辑交给 PowerShell 的 Sleep 控制生命周期
    }}
}}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Windows.Forms, System.Drawing
[LegacyToast]::Show('{EscapeForScript(title)}', '{EscapeForScript(message)}')
Start-Sleep -s 5 
";
        RunProcess("powershell", $"-NoProfile -WindowStyle Hidden -Command \"{psScript}\"");
    }

    // 方案 B: Windows 10/11 现代方案 (基于 WinRT XML)
    private static void SendWindows10Toast(string title, string message)
    {
        string psScript = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);
$xml = $template.GetXml();
$textNodes = $template.GetElementsByTagName('text');
$textNodes[0].AppendChild($template.CreateTextNode('{EscapeForScript(title)}')) > $null;
$textNodes[1].AppendChild($template.CreateTextNode('{EscapeForScript(message)}')) > $null;
$toast = [Windows.UI.Notifications.ToastNotification]::new($template);
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('AvaloniaApp').Show($toast);
";
        RunProcess("powershell", $"-NoProfile -WindowStyle Hidden -Command \"{psScript}\"");
    }

    // --- MacOS 实现 ---
    private static void ShowMacNotification(string title, string message)
    {
        string script = $"display notification \"{EscapeForScript(message)}\" with title \"{EscapeForScript(title)}\"";
        RunProcess("osascript", $"-e '{script}'");
    }

    // --- Linux 实现 ---
    private static void ShowLinuxNotification(string title, string message)
    {
        RunProcess("notify-send", $"\"{EscapeForScript(title)}\" \"{EscapeForScript(message)}\"");
    }

    // --- 辅助方法 ---
    private static void RunProcess(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(info);
        // 对于 Win7 气泡，脚本里有 Sleep，所以这里如果不等，C# 主线程不受影响，PowerShell 在后台跑
        // 对于其他平台，命令很快结束
    }

    private static string EscapeForScript(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // 简单的单引号/双引号转义，防止 PowerShell/Bash 语法错误
        // 这里主要针对 PowerShell 的单引号包裹逻辑进行转义
        return input.Replace("'", "''").Replace("\"", "\\\"");
    }
}