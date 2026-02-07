using System;

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
            if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return false;

            var builder = new CommunityToolkit.WinUI.Notifications.ToastContentBuilder()
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
#endif
    }
}

