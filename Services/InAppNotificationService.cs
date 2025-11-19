using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WatchingEye
{
    public static class InAppNotificationService
    {
        private static ILogger? _logger;
        private static ISessionManager? _sessionManager;
        private static bool _isRunning = false;

        private const int StandardToastTimeoutMs = 10000;
        private const int LongLivedToastTimeoutMs = 30000; 
        private const int ModalDialogTimeoutMs = 0;      

        public static void Start(ILogger logger, ISessionManager sessionManager)
        {
            if (_isRunning) return;

            _logger = logger;
            _sessionManager = sessionManager;
            _isRunning = true;
            _logger.Info("[InAppNotificationService] Started.");
        }

        public static void Stop()
        {
            _isRunning = false;
            _logger?.Info("[InAppNotificationService] Stopped.");
        }

        public static async Task SendNotificationAsync(string sessionId, string header, string text, bool useConfirmationButton)
        {
            if (!_isRunning || _sessionManager == null || string.IsNullOrEmpty(sessionId)) return;

            var session = _sessionManager.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                await SendNotificationAsync(session, header, text, useConfirmationButton).ConfigureAwait(false);
            }
            else
            {
                _logger?.Warn($"[InAppNotificationService] Could not find session with ID {sessionId} to send notification.");
            }
        }

        public static async Task SendNotificationAsync(SessionInfo session, string header, string text, bool useConfirmationButton)
        {
            if (!_isRunning || _sessionManager == null || session == null) return;

            int timeoutMs;
            if (useConfirmationButton)
            {
                string clientName = (session.Client ?? string.Empty).ToLowerInvariant();

                if (clientName.Contains("android") ||
                    clientName.Contains("firetv") ||
                    clientName.Contains("fire tv") ||
                    clientName.Contains("roku"))
                {
                    timeoutMs = LongLivedToastTimeoutMs;
                }
                else
                {
                    timeoutMs = ModalDialogTimeoutMs;
                }
            }
            else
            {
                timeoutMs = StandardToastTimeoutMs;
            }

            var message = new MessageCommand
            {
                Header = header,
                Text = text,
                TimeoutMs = timeoutMs
            };

            await _sessionManager.SendMessageCommand(null, session.Id, message, CancellationToken.None).ConfigureAwait(false);
        }
    }
}