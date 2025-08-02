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
    public static class WatchTimeManager
    {
        private static Timer? _updateTimer;
        private static ISessionManager? _sessionManager;
        private static ILogger? _logger;
        private static IJsonSerializer? _jsonSerializer;
        private static string? _watchTimeDataPath;

        private static bool _isRunning = false;
        private static readonly int TimerIntervalSeconds = 10;

        private static readonly ConcurrentDictionary<string, TimeSpan> _userWatchTime = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _limitReachedNotified = new();
        private static DateTime _lastResetTime = DateTime.UtcNow;
        private static DateTime _nextResetTime = DateTime.MaxValue;

        public static void Start(ISessionManager sessionManager, ILogger logger, IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
        {
            if (_isRunning) return;

            _sessionManager = sessionManager;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.WatchTime.json");

            LoadWatchTimeData();
            CalculateNextResetTime();

            var interval = TimeSpan.FromSeconds(TimerIntervalSeconds);
            _updateTimer = new Timer(OnTimerElapsed, null, interval, interval);

            _isRunning = true;
            _logger.Info("[WatchTimeManager] Started. Next reset time: {0}", _nextResetTime.ToLocalTime());
        }

        public static void Stop()
        {
            SaveWatchTimeData();
            _updateTimer?.Dispose();
            _updateTimer = null;
            _isRunning = false;
            _logger?.Info("[WatchTimeManager] Stopped.");
        }

        private static void OnTimerElapsed(object? state)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || _sessionManager == null || _logger == null) return;

                if (!config.EnableWatchTimeLimiter)
                {
                    if (!_userWatchTime.IsEmpty)
                    {
                        ResetAllWatchTimes();
                    }
                    return;
                }

                if (DateTime.UtcNow >= _nextResetTime)
                {
                    _logger.Info("[WatchTimeManager] Resetting all user watch times as scheduled reset time has passed.");
                    ResetAllWatchTimes();
                    CalculateNextResetTime();
                    _logger.Info("[WatchTimeManager] New reset time scheduled for: {0}", _nextResetTime.ToLocalTime());
                }

                var limitedUsersMap = config.LimitedUsers.ToDictionary(u => u.UserId, u => u, StringComparer.OrdinalIgnoreCase);
                if (!limitedUsersMap.Any())
                {
                    if (!_userWatchTime.IsEmpty)
                    {
                        ResetAllWatchTimes();
                    }
                    return;
                }

                var userIdsInConfig = limitedUsersMap.Keys;
                var userIdsToClean = _userWatchTime.Keys.Except(userIdsInConfig).ToList();
                bool dataCleaned = false;
                foreach (var userIdToClean in userIdsToClean)
                {
                    if (_userWatchTime.TryRemove(userIdToClean, out _))
                    {
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
                foreach (var session in activeLimitedSessions)
                {
                    var timeToAdd = TimeSpan.FromSeconds(TimerIntervalSeconds);
                    var newTotalTime = _userWatchTime.AddOrUpdate(session.UserId, timeToAdd, (_, oldTime) => oldTime.Add(timeToAdd));
                    dataChanged = true;

                    var userLimitConfig = limitedUsersMap[session.UserId];
                    var watchTimeLimit = TimeSpan.FromMinutes(userLimitConfig.WatchTimeLimitMinutes);

                    if (newTotalTime >= watchTimeLimit)
                    {
                        _logger.Info("[WatchTimeManager] User '{0}' ({1}) has exceeded watch time limit of {2:g}. Stopping playback.", session.UserName, session.UserId, watchTimeLimit);
                        StopPlaybackForUser(session.UserId).GetAwaiter().GetResult();
                    }
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
            _logger.Info("[WatchTimeManager] User '{0}': Existing watched time: {1}. Time to subtract: {2}. New watched time: {3}", limitedUser.Username, existingTime, timeToSubtract, newTime);

            _userWatchTime.AddOrUpdate(userId, newTime, (key, oldTime) => newTime);

            _limitReachedNotified.TryRemove(userId, out _);

            SaveWatchTimeData();
            _logger.Info("[WatchTimeManager] Successfully extended watch time for user '{0}'. Current watched time is now {1}.", limitedUser.Username, newTime);
        }

        public static bool IsPlaybackAllowed(string userId)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableWatchTimeLimiter || string.IsNullOrEmpty(userId)) return true;

            var limitedUser = config.LimitedUsers.FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (limitedUser == null || !limitedUser.IsEnabled) return true;

            var watchTimeLimit = TimeSpan.FromMinutes(limitedUser.WatchTimeLimitMinutes);
            var currentTime = _userWatchTime.GetOrAdd(userId, TimeSpan.Zero);

            return currentTime < watchTimeLimit;
        }

        public static async Task StopPlaybackForUser(string userId)
        {
            if (_sessionManager == null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var message = config.WatchTimeLimitMessageText;

            var userSessions = _sessionManager.Sessions.Where(s =>
                string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                s.NowPlayingItem != null
            ).ToList();

            foreach (var session in userSessions)
            {
                if (session.PlayState is { IsPaused: false })
                {
                    await _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                }

                if (_limitReachedNotified.TryAdd(userId, true))
                {
                    await _sessionManager.SendMessageCommand(null, session.Id, new MessageCommand { Header = "Time Limit Reached", Text = message, TimeoutMs = 10000 }, CancellationToken.None).ConfigureAwait(false);
                }
            }
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

        private static void ResetAllWatchTimes()
        {
            _userWatchTime.Clear();
            _limitReachedNotified.Clear();
            _lastResetTime = DateTime.UtcNow;
            SaveWatchTimeData();
        }

        private static void CalculateNextResetTime()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                _nextResetTime = DateTime.MaxValue;
                return;
            }

            var now = DateTime.UtcNow;
            _lastResetTime = _userWatchTime.IsEmpty ? now : _lastResetTime;

            switch (config.WatchTimeResetType)
            {
                case ResetIntervalType.Daily:
                    var resetTime = _lastResetTime.Date.AddHours(config.WatchTimeResetTimeOfDayHours);
                    _nextResetTime = resetTime > now ? resetTime : resetTime.AddDays(1);
                    break;

                case ResetIntervalType.Weekly:
                    var today = _lastResetTime.Date;
                    var daysUntilResetDay = ((int)config.WatchTimeResetDayOfWeek - (int)today.DayOfWeek + 7) % 7;
                    var nextResetDate = today.AddDays(daysUntilResetDay).AddHours(config.WatchTimeResetTimeOfDayHours);
                    _nextResetTime = nextResetDate > now ? nextResetDate : nextResetDate.AddDays(7);
                    break;

                case ResetIntervalType.Minutes:
                default:
                    int resetMinutes = config.WatchTimeResetIntervalMinutes > 0 ? config.WatchTimeResetIntervalMinutes : 1440;
                    _nextResetTime = _lastResetTime.AddMinutes(resetMinutes);
                    break;
            }
        }

        private static void LoadWatchTimeData()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath) || !File.Exists(_watchTimeDataPath))
            {
                _lastResetTime = DateTime.UtcNow;
                return;
            }

            try
            {
                var json = File.ReadAllText(_watchTimeDataPath);
                var data = _jsonSerializer.DeserializeFromString<WatchTimePersistenceData>(json);

                if (data != null)
                {
                    _userWatchTime.Clear();
                    foreach (var user in data.UserWatchTimes)
                    {
                        _userWatchTime.TryAdd(user.UserId, TimeSpan.FromTicks(user.WatchedTimeTicks));
                    }
                    _lastResetTime = data.LastResetTime;
                    _logger?.Info("[WatchTimeManager] Loaded {0} user watch time records from file.", _userWatchTime.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error loading watch time data.", ex);
                _lastResetTime = DateTime.UtcNow;
            }
        }

        private static void SaveWatchTimeData()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath)) return;

            try
            {
                var data = new WatchTimePersistenceData
                {
                    LastResetTime = _lastResetTime,
                    UserWatchTimes = _userWatchTime.Select(kvp => new UserWatchData
                    {
                        UserId = kvp.Key,
                        WatchedTimeTicks = kvp.Value.Ticks
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
