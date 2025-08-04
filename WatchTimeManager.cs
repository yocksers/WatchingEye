using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WatchingEye.Services;

namespace WatchingEye
{
    public enum PlaybackBlockReason { Allowed, TimeLimitExceeded, OutsideTimeWindow }

    public static class WatchTimeManager
    {
        private static Timer? _updateTimer;
        private static ISessionManager? _sessionManager;
        private static ILogger? _logger;
        private static IJsonSerializer? _jsonSerializer;
        private static string? _watchTimeDataPath;
        private static string? _lastLimitedUsersJson;

        private static bool _isRunning = false;
        private static readonly int TimerIntervalSeconds = 5;

        private static readonly ConcurrentDictionary<string, TimeSpan> _userWatchTime = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _userLastResetTime = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _userNextResetTime = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _limitReachedNotified = new();
        private static readonly ConcurrentDictionary<string, DateTime> _sessionLastUpdate = new();

        public static void Start(ISessionManager sessionManager, ILogger logger, IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
        {
            if (_isRunning) return;

            _sessionManager = sessionManager;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.WatchTime.json");

            LoadWatchTimeData();
            CalculateAllNextResetTimes();

            var interval = TimeSpan.FromSeconds(TimerIntervalSeconds);
            _updateTimer = new Timer(OnTimerElapsed, null, interval, interval);

            _isRunning = true;
            _logger.Info("[WatchTimeManager] Started.");
        }

        public static void Stop()
        {
            SaveWatchTimeData();
            _updateTimer?.Dispose();
            _updateTimer = null;
            _isRunning = false;
            _logger?.Info("[WatchTimeManager] Stopped.");
        }

        public static void OnSessionStopped(string sessionId)
        {
            _sessionLastUpdate.TryRemove(sessionId, out _);
        }

        private static void OnTimerElapsed(object? state)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || _sessionManager == null || _logger == null || _jsonSerializer == null)
                {
                    return;
                }

                var currentLimitedUsersJson = _jsonSerializer.SerializeToString(config.LimitedUsers);
                if (_lastLimitedUsersJson == null)
                {
                    _lastLimitedUsersJson = currentLimitedUsersJson;
                }
                else if (_lastLimitedUsersJson != currentLimitedUsersJson)
                {
                    _logger.Info("[WatchTimeManager] Limited user configuration changed, recalculating reset times.");
                    CalculateAllNextResetTimes();
                    _lastLimitedUsersJson = currentLimitedUsersJson;
                }

                if (!config.EnableWatchTimeLimiter)
                {
                    return;
                }

                var limitedUsersMap = config.LimitedUsers.ToDictionary(u => u.UserId, u => u, StringComparer.OrdinalIgnoreCase);
                if (!limitedUsersMap.Any())
                {
                    if (!_userWatchTime.IsEmpty) ResetAllWatchTimes();
                    return;
                }

                foreach (var user in limitedUsersMap.Values)
                {
                    if (!_userNextResetTime.ContainsKey(user.UserId))
                    {
                        _logger.Info($"[WatchTimeManager] New limited user '{user.Username}' detected. Calculating initial reset time.");
                        CalculateNextResetTimeForUser(user);
                    }

                    if (user.WatchTimeResetType != ResetIntervalType.Allowance && _userNextResetTime.TryGetValue(user.UserId, out var nextReset) && DateTime.Now >= nextReset)
                    {
                        _logger.Info($"[WatchTimeManager] Resetting watch time for user '{user.Username}' as their scheduled reset time has passed.");
                        ResetWatchTimeForUser(user.UserId);
                        CalculateNextResetTimeForUser(user);
                        _logger.Info($"[WatchTimeManager] New reset time for '{user.Username}' is {_userNextResetTime[user.UserId]}");
                    }
                }

                var userIdsInConfig = limitedUsersMap.Keys;
                var userIdsToClean = _userWatchTime.Keys.Except(userIdsInConfig).ToList();
                bool dataCleaned = false;
                foreach (var userIdToClean in userIdsToClean)
                {
                    if (_userWatchTime.TryRemove(userIdToClean, out _))
                    {
                        _userLastResetTime.TryRemove(userIdToClean, out _);
                        _userNextResetTime.TryRemove(userIdToClean, out _);
                        _logger.Info($"[WatchTimeManager] Removed watch time data for user ID {userIdToClean} as they are no longer in the limited list.");
                        dataCleaned = true;
                    }
                }

                var activeLimitedSessions = _sessionManager.Sessions.Where(s =>
                    !string.IsNullOrEmpty(s.UserId) &&
                    limitedUsersMap.TryGetValue(s.UserId, out var user) &&
                    user.IsEnabled &&
                    s.NowPlayingItem != null &&
                    s.PlayState is { IsPaused: false }
                ).ToList();

                bool dataChanged = false;
                var now = DateTime.Now;
                foreach (var session in activeLimitedSessions)
                {
                    var userConfig = limitedUsersMap[session.UserId];

                    if (userConfig.EnableTimeWindow)
                    {
                        var startHour = userConfig.WatchWindowStartHour;
                        var endHour = userConfig.WatchWindowEndHour;
                        var isOutsideWindow = false;
                        var currentHour = now.TimeOfDay.TotalHours;

                        if (startHour >= endHour)
                        {
                            if (currentHour >= endHour && currentHour < startHour) isOutsideWindow = true;
                        }
                        else
                        {
                            if (currentHour < startHour || currentHour >= endHour) isOutsideWindow = true;
                        }

                        if (isOutsideWindow)
                        {
                            _logger.Info($"[WatchTimeManager] User '{{0}}' ({{1}}) is now outside their allowed watch window. Stopping playback.", session.UserName, session.UserId);
                            StopPlaybackForUser(session.UserId, PlaybackBlockReason.OutsideTimeWindow).GetAwaiter().GetResult();
                            continue;
                        }
                    }

                    var lastUpdate = _sessionLastUpdate.GetOrAdd(session.Id, now);
                    var timeToAdd = now - lastUpdate;

                    if (timeToAdd > TimeSpan.Zero)
                    {
                        var newTotalTime = _userWatchTime.AddOrUpdate(session.UserId, timeToAdd, (_, oldTime) => oldTime.Add(timeToAdd));
                        dataChanged = true;

                        var watchTimeLimit = TimeSpan.FromMinutes(userConfig.WatchTimeLimitMinutes);

                        if (newTotalTime >= watchTimeLimit)
                        {
                            _logger.Info("[WatchTimeManager] User '{0}' ({1}) has exceeded watch time limit of {2:g}. Stopping playback.", session.UserName, session.UserId, watchTimeLimit);
                            StopPlaybackForUser(session.UserId, PlaybackBlockReason.TimeLimitExceeded).GetAwaiter().GetResult();
                        }
                    }
                    _sessionLastUpdate.AddOrUpdate(session.Id, now, (k, v) => now);
                }

                if (dataChanged || dataCleaned)
                {
                    SaveWatchTimeData();
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error during timer tick.", ex);
            }
        }

        public static void ExtendTimeForUser(string userId, int minutesToExtend)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || _logger == null)
            {
                _logger?.Warn("[WatchTimeManager] Could not extend time: plugin config or logger is null.");
                return;
            }

            var limitedUser = config.LimitedUsers.FirstOrDefault(u => u.UserId == userId);
            if (limitedUser == null)
            {
                _logger.Warn("[WatchTimeManager] Attempted to extend time for a user not in the limited list: ID {0}", userId);
                return;
            }

            _logger.Info("[WatchTimeManager] Attempting to extend time for user '{0}' by {1} minutes.", limitedUser.Username, minutesToExtend);

            var timeToSubtract = TimeSpan.FromMinutes(minutesToExtend);
            _userWatchTime.TryGetValue(userId, out var existingTime);
            var newTime = existingTime - timeToSubtract;

            _userWatchTime.AddOrUpdate(userId, newTime, (key, oldTime) => newTime);
            _limitReachedNotified.TryRemove(userId, out _);
            SaveWatchTimeData();
            _logger.Info("[WatchTimeManager] Successfully extended watch time for user '{0}'. Current watched time is now {1}.", limitedUser.Username, newTime);
        }

        public static PlaybackBlockReason GetPlaybackBlockReason(string userId)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableWatchTimeLimiter || string.IsNullOrEmpty(userId)) return PlaybackBlockReason.Allowed;

            var limitedUser = config.LimitedUsers.FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (limitedUser == null || !limitedUser.IsEnabled) return PlaybackBlockReason.Allowed;

            if (limitedUser.EnableTimeWindow)
            {
                var now = DateTime.Now;
                var startHour = limitedUser.WatchWindowStartHour;
                var endHour = limitedUser.WatchWindowEndHour;
                var currentHour = now.TimeOfDay.TotalHours;

                if (startHour >= endHour)
                {
                    if (currentHour < startHour && currentHour >= endHour)
                    {
                        _logger?.Info($"[WatchTimeManager] Playback blocked for {limitedUser.Username}. Current time {now:T} is outside the allowed window ({TimeSpan.FromHours(startHour):hh\\:mm} - {TimeSpan.FromHours(endHour):hh\\:mm}).");
                        return PlaybackBlockReason.OutsideTimeWindow;
                    }
                }
                else
                {
                    if (currentHour < startHour || currentHour >= endHour)
                    {
                        _logger?.Info($"[WatchTimeManager] Playback blocked for {limitedUser.Username}. Current time {now:T} is outside the allowed window ({TimeSpan.FromHours(startHour):hh\\:mm} - {TimeSpan.FromHours(endHour):hh\\:mm}).");
                        return PlaybackBlockReason.OutsideTimeWindow;
                    }
                }
            }

            if (_limitReachedNotified.ContainsKey(userId))
            {
                return PlaybackBlockReason.TimeLimitExceeded;
            }

            var watchTimeLimit = TimeSpan.FromMinutes(limitedUser.WatchTimeLimitMinutes);
            var currentTime = _userWatchTime.GetOrAdd(userId, TimeSpan.Zero);

            if (currentTime >= watchTimeLimit)
            {
                return PlaybackBlockReason.TimeLimitExceeded;
            }

            return PlaybackBlockReason.Allowed;
        }

        public static async Task StopPlaybackForUser(string userId, PlaybackBlockReason reason)
        {
            if (_sessionManager == null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var allUserSessions = _sessionManager.Sessions.Where(s =>
                string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.IsActive
            ).ToList();

            var username = allUserSessions.FirstOrDefault(s => !string.IsNullOrEmpty(s.UserName))?.UserName ?? "Unknown";

            if (!allUserSessions.Any())
            {
                _logger?.Info($"[WatchTimeManager] No active sessions found for user {username} ({userId}) to stop, but user is now blocked.");
                if (reason == PlaybackBlockReason.TimeLimitExceeded)
                {
                    if (_limitReachedNotified.TryAdd(userId, true))
                    {
                        LogManager.LogLimitReached(userId, username, "System");
                    }
                }
                return;
            }

            var clientNames = string.Join(", ", allUserSessions.Select(s => s.Client).Distinct());
            string message;
            string header;

            _logger?.Info($"[WatchTimeManager] Authoritatively stopping ALL playback for user {username} ({userId}). Reason: {reason}");

            switch (reason)
            {
                case PlaybackBlockReason.TimeLimitExceeded:
                    message = config.WatchTimeLimitMessageText;
                    header = "Time Limit Reached";
                    if (_limitReachedNotified.TryAdd(userId, true))
                    {
                        LogManager.LogLimitReached(userId, username, clientNames);
                    }
                    break;
                case PlaybackBlockReason.OutsideTimeWindow:
                    message = config.TimeWindowBlockedMessageText;
                    header = "Playback Not Allowed";
                    break;
                default:
                    return;
            }

            foreach (var session in allUserSessions)
            {
                _logger?.Info($"[WatchTimeManager] Sending termination command to session on client '{session.Client}' (ID: {session.Id}).");
                await _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                await _sessionManager.SendMessageCommand(null, session.Id, new MessageCommand { Header = header, Text = message, TimeoutMs = 10000 }, CancellationToken.None).ConfigureAwait(false);
            }

            _logger?.Info($"[WatchTimeManager] Finished sending termination commands to {allUserSessions.Count} session(s) for user {username}. The user is now blocked from starting new streams.");
        }

        public static List<LimitedUserStatus> GetLimitedUsersStatus()
        {
            var config = Plugin.Instance?.Configuration;
            var statusList = new List<LimitedUserStatus>();

            if (config == null || !config.EnableWatchTimeLimiter)
            {
                return statusList;
            }

            foreach (var limitedUser in config.LimitedUsers)
            {
                var limit = TimeSpan.FromMinutes(limitedUser.WatchTimeLimitMinutes);
                var watched = _userWatchTime.GetOrAdd(limitedUser.UserId, TimeSpan.Zero);
                var remaining = limit > watched ? limit - watched : TimeSpan.Zero;

                statusList.Add(new LimitedUserStatus
                {
                    UserId = limitedUser.UserId,
                    Username = limitedUser.Username,
                    WatchTimeLimitMinutes = limitedUser.WatchTimeLimitMinutes,
                    SecondsWatched = watched.TotalSeconds,
                    SecondsRemaining = remaining.TotalSeconds,
                    IsLimited = true
                });
            }
            return statusList;
        }

        public static void ResetWatchTimeForUser(string userId)
        {
            _userWatchTime.AddOrUpdate(userId, TimeSpan.Zero, (k, v) => TimeSpan.Zero);
            _userLastResetTime.AddOrUpdate(userId, DateTime.Now, (k, v) => DateTime.Now);
            _limitReachedNotified.TryRemove(userId, out _);
            SaveWatchTimeData();
            _logger?.Info($"[WatchTimeManager] Manually reset watch time for user ID {userId}.");
        }

        public static void ResetAllWatchTimes()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            foreach (var user in config.LimitedUsers)
            {
                ResetWatchTimeForUser(user.UserId);
            }
            _logger?.Info("[WatchTimeManager] Manually reset all user watch times.");
        }

        public static void CalculateAllNextResetTimes()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            foreach (var user in config.LimitedUsers)
            {
                CalculateNextResetTimeForUser(user);
            }
            _logger?.Info("[WatchTimeManager] Calculated next reset times for all users.");
        }

        private static void CalculateNextResetTimeForUser(LimitedUser user)
        {
            var now = DateTime.Now;
            var today = now.Date;
            DateTime nextReset;

            switch (user.WatchTimeResetType)
            {
                case ResetIntervalType.Daily:
                    var resetTimeToday = today.AddHours(user.WatchTimeResetTimeOfDayHours);
                    nextReset = resetTimeToday;
                    if (nextReset <= now)
                    {
                        nextReset = resetTimeToday.AddDays(1);
                    }
                    break;

                case ResetIntervalType.Weekly:
                    var daysUntilResetDay = ((int)user.WatchTimeResetDayOfWeek - (int)today.DayOfWeek + 7) % 7;
                    var nextResetDate = today.AddDays(daysUntilResetDay).AddHours(user.WatchTimeResetTimeOfDayHours);
                    nextReset = nextResetDate;
                    if (nextReset <= now)
                    {
                        nextReset = nextResetDate.AddDays(7);
                    }
                    break;

                case ResetIntervalType.Allowance:
                    nextReset = DateTime.MaxValue;
                    break;

                case ResetIntervalType.Minutes:
                default:
                    var lastReset = _userLastResetTime.GetOrAdd(user.UserId, now);
                    int resetMinutes = user.WatchTimeResetIntervalMinutes > 0 ? user.WatchTimeResetIntervalMinutes : 1440;
                    nextReset = lastReset.AddMinutes(resetMinutes);
                    break;
            }
            _userNextResetTime.AddOrUpdate(user.UserId, nextReset, (k, v) => nextReset);
        }

        private static void LoadWatchTimeData()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath) || !File.Exists(_watchTimeDataPath)) return;

            try
            {
                var json = File.ReadAllText(_watchTimeDataPath);
                var data = _jsonSerializer.DeserializeFromString<WatchTimePersistenceData>(json);

                if (data != null)
                {
                    _userWatchTime.Clear();
                    _userLastResetTime.Clear();
                    foreach (var user in data.UserWatchTimes)
                    {
                        _userWatchTime.TryAdd(user.UserId, TimeSpan.FromTicks(user.WatchedTimeTicks));

                        // Ensure loaded times are treated as Local. This handles data from older versions.
                        var lastResetTime = user.LastResetTime == DateTime.MinValue ? DateTime.Now : user.LastResetTime.ToLocalTime();
                        _userLastResetTime.TryAdd(user.UserId, lastResetTime);
                    }
                    _logger?.Info("[WatchTimeManager] Loaded {0} user watch time records from file.", _userWatchTime.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error loading watch time data.", ex);
            }
        }

        private static void SaveWatchTimeData()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath)) return;

            try
            {
                var data = new WatchTimePersistenceData
                {
                    UserWatchTimes = _userWatchTime.Select(kvp => new UserWatchData
                    {
                        UserId = kvp.Key,
                        WatchedTimeTicks = kvp.Value.Ticks,
                        LastResetTime = _userLastResetTime.GetOrAdd(kvp.Key, DateTime.Now)
                    }).ToList()
                };

                var json = _jsonSerializer.SerializeToString(data);
                File.WriteAllText(_watchTimeDataPath, json);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error saving watch time data.", ex);
            }
        }
    }
}