using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using OpenDownloader.Models;

namespace OpenDownloader.Services;

public class NotificationService
{
    public event EventHandler<ToastMessage>? ToastRequested;

    public void ShowNotification(string title, string message, ToastType type = ToastType.Info)
    {
        // Check if main window is active
        if (IsMainWindowActive())
        {
            ToastRequested?.Invoke(this, new ToastMessage { Title = title, Message = message, Type = type });
            return;
        }

        // Otherwise show native notification
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacNotification(title, message);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindowsNotification(title, message);
        }
        // Linux support can be added with notify-send
    }

    private bool IsMainWindowActive()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.IsActive == true;
        }
        return false;
    }

    private void ShowMacNotification(string title, string message)
    {
        try
        {
            // Escape quotes
            var safeTitle = title.Replace("\"", "\\\"");
            var safeMessage = message.Replace("\"", "\\\"");
            
            var script = $"display notification \"{safeMessage}\" with title \"{safeTitle}\"";
            
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification failed: {ex.Message}");
        }
    }

    private void ShowWindowsNotification(string title, string message)
    {
        try
        {
            // Simple fallback: PowerShell script to show a balloon tip using NotifyIcon if possible, 
            // or just rely on the app being in foreground. 
            // Since we don't have a reliable native API without packages, we will try a PowerShell Toast script.
            
            var script = $@"
$code = @'
using System;
using System.Windows.Forms;
public class Toast {{
    public static void Show(string title, string message) {{
        var icon = new NotifyIcon();
        icon.Icon = System.Drawing.SystemIcons.Information;
        icon.Visible = true;
        icon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        icon.Dispose();
    }}
}}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Windows.Forms, System.Drawing
[Toast]::Show('{title}', '{message}')
";
            // Note: This might close immediately if the powershell process exits.
            // A better way is sticking to TrayIcon if we implement it.
        }
        catch
        {
            // Ignore
        }
    }
}
