using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Go2HDR.Services;

public class NotificationService
{
    private const string AppId = "Go2HDR.App";

    public NotificationService()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            key.SetValue("DisplayName", "Go2HDR");
        }
        catch { }
    }

    public void ShowUpdateAvailable(string version, string releaseUrl)
    {
        try
        {
            var escapedUrl     = System.Security.SecurityElement.Escape(releaseUrl) ?? releaseUrl;
            var escapedVersion = System.Security.SecurityElement.Escape(version) ?? version;

            var xml = $"""
                <toast activationType="protocol" launch="{escapedUrl}">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>Go2HDR update available</text>
                      <text>Version {escapedVersion} is ready — click to open the release page.</text>
                    </binding>
                  </visual>
                </toast>
                """;

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            notifier.Show(new ToastNotification(doc));
        }
        catch { }
    }
}
