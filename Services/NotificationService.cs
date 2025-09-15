using Emby.Notifications;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Notifications;

namespace WatchingEye
{
    public static class NotificationService
    {
        private static ILogger? _logger;
        private static INotificationManager? _notificationManager;
        private static bool _isRunning = false;

        public static void Start(ILogger logger, INotificationManager notificationManager)
        {
            if (_isRunning) return;

            _logger = logger;
            _notificationManager = notificationManager;
            _isRunning = true;
            _logger.Info("[NotificationService] Started.");
        }

        public static void Stop()
        {
            _isRunning = false;
            _logger?.Info("[NotificationService] Stopped.");
        }

        public static void SendLimitReachedNotification(string username, string clientName)
        {
            if (!_isRunning || _notificationManager == null) return;

            _notificationManager.SendNotification(new Emby.Notifications.NotificationRequest
            {
                Title = "Watching Eye: Time Limit Reached",
                Description = $"User '{username}' has reached their watch time limit. Playback has been stopped.",
            });

            _logger?.Info($"[NotificationService] Sent 'Time Limit Reached' notification for user {username}.");
        }

        public static void SendThresholdNotification(string username, string period, int threshold)
        {
            if (!_isRunning || _notificationManager == null) return;

            _notificationManager.SendNotification(new Emby.Notifications.NotificationRequest
            {
                Title = $"Watching Eye: {threshold}% Time Limit Warning",
                Description = $"User '{username}' has used {threshold}% of their {period} watch time limit.",
            });

            _logger?.Info($"[NotificationService] Sent '{threshold}% threshold' notification for user {username}.");
        }
    }
}