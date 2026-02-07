using System;

#if WINDOWS
using CommunityToolkit.WinUI.Notifications;
#endif

namespace Downio.Services.Notifications;

public static class WindowsToastNotification
{
    public static bool TryShow(string title, string message, string? appLogoPath)
    {
#if !WINDOWS
        return false;
#else
        try
        {
            return TryShowCore(title, message, appLogoPath);
        }
        catch
        {
            return false;
        }
#endif
    }

#if WINDOWS
    private static bool TryShowCore(string title, string message, string? appLogoPath)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return false;

            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (!string.IsNullOrWhiteSpace(appLogoPath))
            {
                var uri = new Uri(appLogoPath, UriKind.Absolute);
                builder.AddAppLogoOverride(uri);
            }

            builder.Show();
            return true;
        }
        catch
        {
            return false;
        }
    }
#endif
}
