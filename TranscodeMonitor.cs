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
        private static Timer? _sessionCheckTimer;

        private static List<string>? _excludedLibraryPathsCache;
        private static string _cachedConfigVersion = string.Empty;
        private static readonly object _cacheLock = new object();

        private static readonly ConcurrentDictionary<string, int> _transcodeNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _directPlayNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, int> _playbackStartNotificationsSent = new();
        private static readonly ConcurrentDictionary<string, bool> _notificationLoopActive = new();
        private static readonly ConcurrentDictionary<string, bool> _sessionsBeingBlocked = new();
        private static readonly ConcurrentDictionary<string, DateTime> _sessionPauseStartTime = new();

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
            _sessionPauseStartTime.Clear();

            _sessionCheckTimer = new Timer(OnSessionCheckTimerElapsed, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            _isRunning = true;
        }

        public static void Stop()
        {
            if (!_isRunning || _sessionManager == null)
                return;

            _sessionCheckTimer?.Dispose();
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _transcodeNotificationsSent.Clear();
            _directPlayNotificationsSent.Clear();
            _playbackStartNotificationsSent.Clear();
            _sessionPauseStartTime.Clear();

            _isRunning = false;
        }

        private static async void OnSessionCheckTimerElapsed(object? state)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || _sessionManager == null || _logger == null || _libraryManager == null)
                return;

            if (!config.EnablePausedStreamTimeout || config.PausedStreamTimeoutMinutes <= 0)
            {
                if (!_sessionPauseStartTime.IsEmpty)
                {
                    _sessionPauseStartTime.Clear();
                }
                return;
            }

            var sessions = _sessionManager.Sessions.ToList();
            var now = DateTime.UtcNow;
            var timeout = TimeSpan.FromMinutes(config.PausedStreamTimeoutMinutes);

            foreach (var session in sessions)
            {
                if (IsSessionExcluded(session, config))
                {
                    _sessionPauseStartTime.TryRemove(session.Id, out _);
                    continue;
                }

                if (session.PlayState.IsPaused)
                {
                    var pauseStartTime = _sessionPauseStartTime.GetOrAdd(session.Id, now);
                    var pausedDuration = now - pauseStartTime;

                    if (pausedDuration >= timeout)
                    {
                        _logger.Info($"[TranscodeMonitor] Stopping session for user '{session.UserName}' on client '{session.Client}' due to exceeding pause limit of {config.PausedStreamTimeoutMinutes} minutes.");

                        if (_sessionPauseStartTime.TryRemove(session.Id, out _))
                        {
                            await InAppNotificationService.SendNotificationAsync(session.Id, "Playback Stopped", config.PausedStreamTimeoutMessage, config.EnableConfirmationButtonOnPausedStreamTimeout).ConfigureAwait(false);
                            await _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    _sessionPauseStartTime.TryRemove(session.Id, out _);
                }
            }

            var currentSessionIds = new HashSet<string>(sessions.Select(s => s.Id));
            var pausedSessionIds = _sessionPauseStartTime.Keys.ToList();
            foreach (var pausedId in pausedSessionIds)
            {
                if (!currentSessionIds.Contains(pausedId))
                {
                    _sessionPauseStartTime.TryRemove(pausedId, out _);
                }
            }
        }

        private static bool IsSessionExcluded(SessionInfo session, PluginConfiguration config)
        {
            if (session == null || config == null || _logger == null || _sessionManager == null || _libraryManager == null || string.IsNullOrEmpty(session.UserId) || session.NowPlayingItem == null)
                return true;

            if (config.ExcludedUserIds.Contains(session.UserId)) return true;
            if (config.ExcludedClients.Contains(session.Client, StringComparer.OrdinalIgnoreCase)) return true;

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
                        return true;
                    }
                }
            }

            return false;
        }

        private static void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            var session = e.Session;
            if (session != null)
            {
                var sessionId = session.Id;
                _sessionPauseStartTime.TryRemove(sessionId, out _);

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

        private static bool IsOutsideLibraryTimeWindow(LibraryTimeRestriction restriction, DateTime now)
        {
            if (restriction.AllowedDays != null && restriction.AllowedDays.Count < 7)
            {
                if (!restriction.AllowedDays.Contains((int)now.DayOfWeek))
                {
                    return true;
                }
            }

            var startHour = restriction.StartTime;
            var endHour = restriction.EndTime;
            var currentHour = now.TimeOfDay.TotalHours;

            if (startHour >= endHour)
            {
                return currentHour >= endHour && currentHour < startHour;
            }
            else
            {
                return currentHour < startHour || currentHour >= endHour;
            }
        }

        private static void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var session = e.Session;

                if (session == null || config == null)
                    return;

                if (IsSessionExcluded(session, config)) return;

                if (config.LibraryTimeRestrictions.Any(l => l.IsEnabled))
                {
                    var fullItem = _libraryManager?.GetItemById(session.NowPlayingItem.Id);
                    if (fullItem != null && !string.IsNullOrEmpty(fullItem.Path) && _libraryManager != null)
                    {
                        string? libraryId = null;
                        foreach (var library in _libraryManager.GetVirtualFolders())
                        {
                            if (library.Locations.Any(loc => fullItem.Path.StartsWith(loc, StringComparison.OrdinalIgnoreCase)))
                            {
                                libraryId = library.Id.ToString();
                                break;
                            }
                        }

                        if (libraryId != null)
                        {
                            var restriction = config.LibraryTimeRestrictions.FirstOrDefault(r => r.LibraryId == libraryId && r.IsEnabled);
                            if (restriction != null && IsOutsideLibraryTimeWindow(restriction, DateTime.Now))
                            {
                                _logger?.Info($"[TranscodeMonitor] Blocking playback for user '{session.UserName}' from library '{restriction.LibraryName}' due to time restriction.");
                                if (_sessionManager != null)
                                {
                                    _ = InAppNotificationService.SendNotificationAsync(session.Id, "Playback Not Allowed", restriction.BlockMessage, false).ConfigureAwait(false);
                                    _ = _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                                }
                                return;
                            }
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
                await SendNotificationLoopAsync(session, "Playback Started", config.PlaybackStartMessageText,
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
                    await SendNotificationLoopAsync(session, "Direct Play", config.DirectPlayMessageText,
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

            int actualHeight = 0;
            if (currentSession.NowPlayingItem?.MediaStreams != null)
            {
                var videoStream = currentSession.NowPlayingItem.MediaStreams.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
                if (videoStream != null)
                {
                    actualHeight = videoStream.Height ?? 0;
                }
            }

            if (actualHeight == 0)
            {
                actualHeight = currentSession.NowPlayingItem?.Height ?? 0;
            }

            if (config.EnableResolutionBlocking && actualHeight > config.MaxTranscodingResolution)
            {
                _logger?.Info($"[TranscodeMonitor] Blocking transcode for user '{currentSession.UserName}' on client '{currentSession.Client}'. Resolution '{actualHeight}p' is over the limit of '{config.MaxTranscodingResolution}p'.");

                var text = config.MessageTextResolutionBlocked;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = $"Transcoding video with a resolution of {actualHeight}p is not permitted. The maximum allowed resolution is {config.MaxTranscodingResolution}p.";
                }

                text = text.Replace("{height}", actualHeight.ToString())
                           .Replace("{max}", config.MaxTranscodingResolution.ToString());

                if (_sessionManager == null) return;
                _sessionsBeingBlocked.TryAdd(currentSession.Id, true);
                await InAppNotificationService.SendNotificationAsync(currentSession.Id, "Playback Blocked", text, config.EnableConfirmationButtonOnResolutionBlock).ConfigureAwait(false);
                await _sessionManager.SendPlaystateCommand(null, currentSession.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (config.EnableTranscodeBlocking && !string.IsNullOrWhiteSpace(config.BlockedTranscodeFormats))
            {
                var blockedFormats = new HashSet<string>(config.BlockedTranscodeFormats.Split(',').Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);
                var sourceContainer = currentSession.NowPlayingItem?.Container;

                if (!string.IsNullOrEmpty(sourceContainer) && blockedFormats.Contains(sourceContainer))
                {
                    _logger?.Info($"[TranscodeMonitor] Blocking transcode for user '{currentSession.UserName}' on client '{currentSession.Client}'. Source format '{sourceContainer}' is on the blocklist.");

                    var text = $"Transcoding from the '{sourceContainer.ToUpper()}' container is not permitted by the server administrator.";

                    if (_sessionManager == null) return;
                    _sessionsBeingBlocked.TryAdd(currentSession.Id, true);
                    await InAppNotificationService.SendNotificationAsync(currentSession.Id, "Playback Blocked", text, config.EnableConfirmationButtonOnTranscodeBlock).ConfigureAwait(false);
                    await _sessionManager.SendPlaystateCommand(null, currentSession.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            if (!config.EnableTranscodeWarning) return;

            var transcodeInfo = currentSession.TranscodingInfo;

            if (transcodeInfo.IsVideoDirect && transcodeInfo.IsAudioDirect)
            {
                if (!config.NotifyOnContainerChange)
                {
                    _logger?.Info("[TranscodeMonitor] Suppressing notification for container remux as per configuration.");
                    return;
                }
            }
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

            await SendNotificationLoopAsync(currentSession, "Transcode Warning", messageText, 0,
                config.MaxNotifications, config.DelayBetweenMessagesSeconds, config.EnableConfirmationButtonOnTranscodeWarning, _transcodeNotificationsSent);
        }

        private static async Task SendNotificationLoopAsync(SessionInfo session, string header, string text, int initialDelay, int maxNotifications, int delayBetween, bool useConfirmationButton, ConcurrentDictionary<string, int> sentCounts)
        {
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

                    await InAppNotificationService.SendNotificationAsync(session.Id, header, text, useConfirmationButton).ConfigureAwait(false);

                    sentCount = sentCounts.AddOrUpdate(notificationKey, 1, (_, count) => count + 1);

                    if (sentCount < maxNotifications && delayBetween > 0)
                    {
                        await Task.Delay(delayBetween * 1000).ConfigureAwait(false);
                    }
                    else
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