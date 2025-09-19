using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WatchingEye
{
    public static class TranscodeMonitor
    {
        private static ISessionManager? _sessionManager;
        private static ILogger? _logger;
        private static bool _isRunning;

        private static readonly ConcurrentDictionary<string, int> _transcodeNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _directPlayNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _playbackStartNotificationsSent = new();

        public static void Start(ISessionManager sessionManager, ILogger logger)
        {
            if (_isRunning)
                return;

            _sessionManager = sessionManager;
            _logger = logger;
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;

            _transcodeNotificationsSent.Clear();
            _directPlayNotificationsSent.Clear();
            _playbackStartNotificationsSent.Clear();

            _isRunning = true;
        }

        public static void Stop()
        {
            if (!_isRunning || _sessionManager == null)
                return;

            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _transcodeNotificationsSent.Clear();
            _directPlayNotificationsSent.Clear();
            _playbackStartNotificationsSent.Clear();

            _isRunning = false;
        }

        private static void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            var session = e.Session;
            if (session != null)
            {
                _transcodeNotificationsSent.TryRemove(session.Id, out _);
                _directPlayNotificationsSent.TryRemove(session.Id, out _);
                _playbackStartNotificationsSent.TryRemove(session.Id, out _);

                WatchTimeManager.OnSessionStopped(session.Id);
            }
        }

        private static void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var session = e.Session;

                if (session == null || config == null || _logger == null || string.IsNullOrEmpty(session.UserId))
                    return;

                if (config.EnableWatchTimeLimiter)
                {
                    var blockReason = WatchTimeManager.GetPlaybackBlockReason(session.UserId);
                    if (blockReason != PlaybackBlockReason.Allowed)
                    {
                        _ = Task.Run(() => WatchTimeManager.StopPlaybackForUser(session.UserId, blockReason));
                        return;
                    }
                }

                if (config.ExcludedUserIds.Contains(session.UserId))
                    return;

                if (config.ExcludedClients.Contains(session.Client, StringComparer.OrdinalIgnoreCase))
                    return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandlePlaybackStartNotification(session, config);
                        await HandleMediaStatusNotification(session, config);
                    }
                    catch (Exception taskEx)
                    {
                        _logger?.ErrorException("Error within playback handling task.", taskEx);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("Error in OnPlaybackStart", ex);
            }
        }

        private static async Task HandlePlaybackStartNotification(SessionInfo session, PluginConfiguration config)
        {
            if (config.EnablePlaybackStartNotification && !string.IsNullOrWhiteSpace(config.PlaybackStartMessageText))
            {
                await SendNotificationAsync(session, "Playback Started", config.PlaybackStartMessageText,
                    config.PlaybackStartInitialDelaySeconds, config.PlaybackStartMaxNotifications,
                    config.PlaybackStartDelayBetweenMessagesSeconds, config.EnableConfirmationButton, _playbackStartNotificationsSent);
            }
        }

        private static async Task HandleMediaStatusNotification(SessionInfo session, PluginConfiguration config)
        {
            if (session.TranscodingInfo == null)
            {
                if (config.NotifyOnDirectPlay && !string.IsNullOrWhiteSpace(config.DirectPlayMessageText))
                {
                    await SendNotificationAsync(session, "Direct Play", config.DirectPlayMessageText,
                                                config.InitialDelaySeconds, 1, 0, config.EnableConfirmationButton, _directPlayNotificationsSent);
                }
                return;
            }

            if (!config.EnableTranscodeWarning) return;

            if (!config.NotifyOnAudioOnlyTranscode && session.TranscodingInfo.IsVideoDirect && !session.TranscodingInfo.IsAudioDirect)
                return;

            var rawTranscodeReasons = string.Join(", ", session.TranscodingInfo.TranscodeReasons);
            var friendlyReasons = TranscodeReasonParser.Parse(rawTranscodeReasons);

            string message;
            if (rawTranscodeReasons.Contains("BitrateTooHighInMatrix") && !string.IsNullOrWhiteSpace(config.MessageTextBandwidthLimitation))
            {
                message = config.MessageTextBandwidthLimitation.Replace("{reason}", friendlyReasons);
            }
            else if (!string.IsNullOrWhiteSpace(config.MessageTextClientLimitation))
            {
                message = config.MessageTextClientLimitation.Replace("{reason}", friendlyReasons);
            }
            else
            {
                message = config.MessageText.Replace("{reason}", friendlyReasons);
            }

            LogManager.LogTranscode(session, friendlyReasons);

            await SendNotificationAsync(session, "Transcode Warning", message, config.InitialDelaySeconds,
                config.MaxNotifications, config.DelayBetweenMessagesSeconds, config.EnableConfirmationButton, _transcodeNotificationsSent);
        }

        private static async Task SendNotificationAsync(SessionInfo session, string header, string text, int initialDelay, int maxNotifications, int delayBetween, bool useConfirmationButton, ConcurrentDictionary<string, int> sentCounts)
        {
            if (_sessionManager == null) return;

            var sentCount = sentCounts.GetOrAdd(session.Id, 0);
            if (sentCount >= maxNotifications) return;

            if (initialDelay > 0)
            {
                await Task.Delay(initialDelay * 1000).ConfigureAwait(false);
            }

            var message = new MessageCommand
            {
                Header = header,
                Text = text,
                TimeoutMs = useConfirmationButton ? null : (int?)7000
            };

            await _sessionManager.SendMessageCommand(null, session.Id, message, CancellationToken.None).ConfigureAwait(false);

            sentCounts.AddOrUpdate(session.Id, 1, (_, count) => count + 1);

            if (delayBetween > 0 && sentCounts.GetOrAdd(session.Id, 0) < maxNotifications)
            {
                await Task.Delay(delayBetween * 1000).ConfigureAwait(false);
            }
        }
    }
}