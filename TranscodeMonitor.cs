using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace WatchingEye
{
    public static class TranscodeMonitor
    {
        private static ISessionManager? _sessionManager;
        private static ILogger? _logger;
        private static ILibraryManager? _libraryManager;
        private static bool _isRunning;

        private static List<string>? _excludedLibraryPathsCache;
        private static string _cachedConfigVersion = string.Empty;
        private static readonly object _cacheLock = new object();

        private static readonly ConcurrentDictionary<string, int> _transcodeNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _directPlayNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _playbackStartNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, bool> _notificationLoopActive = new();
        private static readonly ConcurrentDictionary<string, bool> _sessionsBeingBlocked = new();

        public static void Start(ISessionManager sessionManager, ILogger logger, ILibraryManager libraryManager)
        {
            if (_isRunning)
                return;

            _sessionManager = sessionManager;
            _logger = logger;
            _libraryManager = libraryManager;
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
                var sessionId = session.Id;

                // Robustly clean up all notification tracking for the session that just ended.
                // This ensures that if the user restarts playback for the same item, notifications will trigger again.
                // This method is preferred over relying on `e.Item` which can sometimes be null.
                var transcodeKeys = _transcodeNotificationsSent.Keys.Where(k => k.StartsWith($"{sessionId}_")).ToList();
                foreach (var key in transcodeKeys)
                {
                    _transcodeNotificationsSent.TryRemove(key, out _);
                }

                var directPlayKeys = _directPlayNotificationsSent.Keys.Where(k => k.StartsWith($"{sessionId}_")).ToList();
                foreach (var key in directPlayKeys)
                {
                    _directPlayNotificationsSent.TryRemove(key, out _);
                }

                var playbackStartKeys = _playbackStartNotificationsSent.Keys.Where(k => k.StartsWith($"{sessionId}_")).ToList();
                foreach (var key in playbackStartKeys)
                {
                    _playbackStartNotificationsSent.TryRemove(key, out _);
                }

                var loopKeys = _notificationLoopActive.Keys.Where(k => k.StartsWith($"{sessionId}_")).ToList();
                foreach (var key in loopKeys)
                {
                    _notificationLoopActive.TryRemove(key, out _);
                }

                WatchTimeManager.OnSessionStopped(sessionId);
                _sessionsBeingBlocked.TryRemove(sessionId, out _);
            }
        }

        private static void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var session = e.Session;

                if (session == null || config == null || _logger == null || _sessionManager == null || _libraryManager == null || string.IsNullOrEmpty(session.UserId) || session.NowPlayingItem == null)
                    return;

                if (config.ExcludedUserIds.Contains(session.UserId)) return;
                if (config.ExcludedClients.Contains(session.Client, StringComparer.OrdinalIgnoreCase)) return;

                List<string> currentExcludedPaths;
                lock (_cacheLock)
                {
                    if (config.ConfigurationVersion != _cachedConfigVersion)
                    {
                        _logger.Info("[TranscodeMonitor] Configuration has changed, rebuilding excluded library path cache.");
                        var newCache = new List<string>();
                        var excludedIds = new HashSet<string>(config.ExcludedLibraryIds);
                        if (excludedIds.Any())
                        {
                            var libraries = _libraryManager.GetVirtualFolders();
                            foreach (var library in libraries)
                            {
                                if (excludedIds.Contains(library.Id.ToString()))
                                {
                                    newCache.AddRange(library.Locations);
                                }
                            }
                        }
                        _excludedLibraryPathsCache = newCache;
                        _cachedConfigVersion = config.ConfigurationVersion;
                    }
                    currentExcludedPaths = _excludedLibraryPathsCache ?? new List<string>();
                }

                if (currentExcludedPaths.Any())
                {
                    var fullItem = _libraryManager.GetItemById(session.NowPlayingItem.Id);
                    if (fullItem != null && !string.IsNullOrEmpty(fullItem.Path))
                    {
                        if (currentExcludedPaths.Any(p => fullItem.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            return;
                        }
                    }
                }

                if (config.EnableWatchTimeLimiter)
                {
                    var blockReason = WatchTimeManager.GetPlaybackBlockReason(session.UserId);
                    if (blockReason != PlaybackBlockReason.Allowed)
                    {
                        _ = Task.Run(() => WatchTimeManager.StopPlaybackForUser(session.UserId, blockReason));
                        return;
                    }
                }

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
                    config.PlaybackStartDelayBetweenMessagesSeconds, config.EnableConfirmationButtonOnPlaybackStart, _playbackStartNotificationsSent);
            }
        }

        private static async Task HandleMediaStatusNotification(SessionInfo session, PluginConfiguration config)
        {
            if (session.TranscodingInfo == null)
            {
                if (config.NotifyOnDirectPlay && !string.IsNullOrWhiteSpace(config.DirectPlayMessageText))
                {
                    await SendNotificationAsync(session, "Direct Play", config.DirectPlayMessageText,
                                                config.InitialDelaySeconds, 1, 0, config.EnableConfirmationButtonOnDirectPlay, _directPlayNotificationsSent);
                }
                return;
            }

            if (config.InitialDelaySeconds > 0)
            {
                await Task.Delay(config.InitialDelaySeconds * 1000).ConfigureAwait(false);
            }

            var currentSession = _sessionManager?.Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (currentSession?.TranscodingInfo == null)
            {
                return;
            }

            if (config.EnableResolutionBlocking && currentSession.NowPlayingItem.Height > config.MaxTranscodingResolution)
            {
                _logger?.Info($"[TranscodeMonitor] Blocking transcode for user '{currentSession.UserName}' on client '{currentSession.Client}'. Resolution '{currentSession.NowPlayingItem.Height}p' is over the limit of '{config.MaxTranscodingResolution}p'.");

                var text = config.MessageTextResolutionBlocked;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = $"Transcoding video with a resolution of {currentSession.NowPlayingItem.Height}p is not permitted. The maximum allowed resolution is {config.MaxTranscodingResolution}p.";
                }

                text = text.Replace("{height}", currentSession.NowPlayingItem.Height.ToString())
                           .Replace("{max}", config.MaxTranscodingResolution.ToString());

                var message = new MessageCommand
                {
                    Header = "Playback Blocked",
                    Text = text,
                    TimeoutMs = null
                };

                if (_sessionManager == null) return;
                _sessionsBeingBlocked.TryAdd(currentSession.Id, true);
                await _sessionManager.SendPlaystateCommand(null, currentSession.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                await _sessionManager.SendMessageCommand(null, currentSession.Id, message, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (config.EnableTranscodeBlocking && !string.IsNullOrWhiteSpace(config.BlockedTranscodeFormats))
            {
                var blockedFormats = new HashSet<string>(config.BlockedTranscodeFormats.Split(',').Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);
                var sourceContainer = currentSession.NowPlayingItem.Container;

                if (!string.IsNullOrEmpty(sourceContainer) && blockedFormats.Contains(sourceContainer))
                {
                    _logger?.Info($"[TranscodeMonitor] Blocking transcode for user '{currentSession.UserName}' on client '{currentSession.Client}'. Source format '{sourceContainer}' is on the blocklist.");

                    var message = new MessageCommand
                    {
                        Header = "Playback Blocked",
                        Text = $"Transcoding from the '{sourceContainer.ToUpper()}' container is not permitted by the server administrator.",
                        TimeoutMs = null
                    };

                    if (_sessionManager == null) return;
                    _sessionsBeingBlocked.TryAdd(currentSession.Id, true);
                    await _sessionManager.SendPlaystateCommand(null, currentSession.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                    await _sessionManager.SendMessageCommand(null, currentSession.Id, message, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            if (!config.EnableTranscodeWarning) return;

            var transcodeInfo = currentSession.TranscodingInfo;

            // Case 1: Pure remux. Video and audio are direct streams.
            if (transcodeInfo.IsVideoDirect && transcodeInfo.IsAudioDirect)
            {
                // If the setting to notify on container changes is off, don't proceed.
                if (!config.NotifyOnContainerChange)
                {
                    _logger?.Info("[TranscodeMonitor] Suppressing notification for container remux as per configuration.");
                    return;
                }
            }
            // Case 2: Audio transcode with direct video (not a pure remux).
            else if (transcodeInfo.IsVideoDirect && !transcodeInfo.IsAudioDirect)
            {
                if (!config.NotifyOnAudioOnlyTranscode)
                {
                    _logger?.Info("[TranscodeMonitor] Suppressing notification for audio-only transcode as per configuration.");
                    return;
                }
            }

            var rawTranscodeReasons = string.Join(", ", currentSession.TranscodingInfo.TranscodeReasons);
            var friendlyReasons = TranscodeReasonParser.Parse(rawTranscodeReasons);

            string messageText;
            var isBandwidthReason = rawTranscodeReasons.Contains("BitrateTooHighInMatrix") || rawTranscodeReasons.Contains("ContainerBitrateExceedsLimit");
            if (isBandwidthReason && !string.IsNullOrWhiteSpace(config.MessageTextBandwidthLimitation))
            {
                messageText = config.MessageTextBandwidthLimitation.Replace("{reason}", friendlyReasons);
            }
            else if (!string.IsNullOrWhiteSpace(config.MessageTextClientLimitation))
            {
                messageText = config.MessageTextClientLimitation.Replace("{reason}", friendlyReasons);
            }
            else
            {
                messageText = config.MessageText.Replace("{reason}", friendlyReasons);
            }

            LogManager.LogTranscode(currentSession, friendlyReasons);

            await SendNotificationAsync(currentSession, "Transcode Warning", messageText, 0,
                config.MaxNotifications, config.DelayBetweenMessagesSeconds, config.EnableConfirmationButtonOnTranscodeWarning, _transcodeNotificationsSent);
        }

        private static async Task SendNotificationAsync(SessionInfo session, string header, string text, int initialDelay, int maxNotifications, int delayBetween, bool useConfirmationButton, ConcurrentDictionary<string, int> sentCounts)
        {
            // If a confirmation button is used, the notification should only ever be sent once.
            if (useConfirmationButton)
            {
                maxNotifications = 1;
            }

            if (_sessionManager == null || session.NowPlayingItem == null) return;

            var notificationKey = $"{session.Id}_{session.NowPlayingItem.Id}";
            var loopKey = $"{notificationKey}_{header}";

            if (!_notificationLoopActive.TryAdd(loopKey, true))
            {
                return;
            }

            try
            {
                var sentCount = sentCounts.GetOrAdd(notificationKey, 0);

                if (sentCount >= maxNotifications)
                {
                    return;
                }

                if (sentCount == 0 && initialDelay > 0)
                {
                    await Task.Delay(initialDelay * 1000).ConfigureAwait(false);
                }

                while (sentCount < maxNotifications)
                {
                    var currentSession = _sessionManager.Sessions.FirstOrDefault(s => s.Id == session.Id);
                    if (currentSession == null || currentSession.PlayState.IsPaused)
                    {
                        break;
                    }

                    var message = new MessageCommand
                    {
                        Header = header,
                        Text = text,
                        TimeoutMs = useConfirmationButton ? null : (int?)7000
                    };

                    await _sessionManager.SendMessageCommand(null, session.Id, message, CancellationToken.None).ConfigureAwait(false);

                    sentCount = sentCounts.AddOrUpdate(notificationKey, 1, (_, count) => count + 1);

                    if (sentCount < maxNotifications && delayBetween > 0)
                    {
                        await Task.Delay(delayBetween * 1000).ConfigureAwait(false);
                    }
                    else // Break the loop if no further delay is needed
                    {
                        break;
                    }
                }
            }
            finally
            {
                _notificationLoopActive.TryRemove(loopKey, out _);
            }
        }
    }
}