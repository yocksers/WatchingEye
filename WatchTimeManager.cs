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

        private static bool _isRunning = false;
        private static readonly int TimerIntervalSeconds = 5;

        private static ConcurrentDictionary<string, UserWatchData> _userWatchData = new();
        private static readonly ConcurrentDictionary<string, DateTime> _sessionLastUpdate = new();
        private static readonly ConcurrentDictionary<string, bool> _limitReachedNotified = new();

        private static readonly ConcurrentDictionary<string, HashSet<int>> _dailyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _weeklyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _monthlyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _yearlyThresholdsNotified = new();


        public static void Start(ISessionManager sessionManager, ILogger logger, IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
        {
            if (_isRunning) return;

            _sessionManager = sessionManager;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.WatchTime.json");

            LoadWatchTimeData();

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
                if (config == null || _sessionManager == null || _logger == null) return;

                if (!config.EnableWatchTimeLimiter)
                {
                    if (!_userWatchData.IsEmpty) ResetAllWatchTimes();
                    return;
                }

                var limitedUsersMap = config.LimitedUsers.ToDictionary(u => u.UserId, u => u, StringComparer.OrdinalIgnoreCase);
                if (!limitedUsersMap.Any()) return;

                bool dataChanged = false;
                var now = DateTime.Now;

                foreach (var userConfig in limitedUsersMap.Values)
                {
                    dataChanged |= ProcessResetsForUser(userConfig, now);
                }

                var userIdsToClean = _userWatchData.Keys.Except(limitedUsersMap.Keys).ToList();
                foreach (var userIdToClean in userIdsToClean)
                {
                    if (_userWatchData.TryRemove(userIdToClean, out _))
                    {
                        _logger.Info($"[WatchTimeManager] Removed watch time data for user ID {userIdToClean}.");
                        dataChanged = true;
                    }
                }

                var activeLimitedSessions = _sessionManager.Sessions.Where(s =>
                    !string.IsNullOrEmpty(s.UserId) &&
                    limitedUsersMap.TryGetValue(s.UserId, out var user) &&
                    user.IsEnabled &&
                    s.NowPlayingItem != null &&
                    s.PlayState is { IsPaused: false }
                ).ToList();

                var activeUsers = activeLimitedSessions
                    .GroupBy(s => s.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        UserConfig = limitedUsersMap[g.Key],
                        Sessions = g.ToList()
                    });

                foreach (var user in activeUsers)
                {
                    if (user.UserConfig.EnableTimeWindow && IsOutsideTimeWindow(user.UserConfig, now))
                    {
                        StopPlaybackForUser(user.UserId, PlaybackBlockReason.OutsideTimeWindow).GetAwaiter().GetResult();
                        continue;
                    }

                    bool timeWasAdded = false;
                    foreach (var session in user.Sessions)
                    {
                        var lastUpdate = _sessionLastUpdate.GetOrAdd(session.Id, now);
                        var timeToAdd = now - lastUpdate;

                        if (timeToAdd > TimeSpan.Zero)
                        {
                            var userData = _userWatchData.GetOrAdd(user.UserId, new UserWatchData { UserId = user.UserId });
                            userData.WatchedTimeTicksDaily += timeToAdd.Ticks;
                            userData.WatchedTimeTicksWeekly += timeToAdd.Ticks;
                            userData.WatchedTimeTicksMonthly += timeToAdd.Ticks;
                            userData.WatchedTimeTicksYearly += timeToAdd.Ticks;
                            dataChanged = true;
                            timeWasAdded = true;
                        }
                        _sessionLastUpdate.AddOrUpdate(session.Id, now, (k, v) => now);
                    }

                    if (timeWasAdded)
                    {
                        if (GetPlaybackBlockReason(user.UserId) == PlaybackBlockReason.TimeLimitExceeded)
                        {
                            StopPlaybackForUser(user.UserId, PlaybackBlockReason.TimeLimitExceeded).GetAwaiter().GetResult();
                        }
                        else
                        {
                            var userData = _userWatchData.GetOrAdd(user.UserId, new UserWatchData { UserId = user.UserId });
                            var representativeSession = user.Sessions.First();
                            ProcessThresholdNotifications(representativeSession, user.UserConfig, userData);
                        }
                    }
                }

                if (dataChanged)
                {
                    SaveWatchTimeData();
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error during timer tick.", ex);
            }
        }

        private static bool ProcessResetsForUser(LimitedUser userConfig, DateTime now)
        {
            var dataChanged = false;
            var needsOverallReset = false;
            var userData = _userWatchData.GetOrAdd(userConfig.UserId, new UserWatchData { UserId = userConfig.UserId });

            if (userData.LastDailyReset == DateTime.MinValue) userData.LastDailyReset = now;
            if (userData.LastWeeklyReset == DateTime.MinValue) userData.LastWeeklyReset = now;
            if (userData.LastMonthlyReset == DateTime.MinValue) userData.LastMonthlyReset = now;
            if (userData.LastYearlyReset == DateTime.MinValue) userData.LastYearlyReset = now;

            DateTime lastDailyTrigger = now.Date.AddHours(userConfig.ResetTimeOfDayHours);
            if (now < lastDailyTrigger) lastDailyTrigger = lastDailyTrigger.AddDays(-1);
            if (now >= lastDailyTrigger && userData.LastDailyReset < lastDailyTrigger)
            {
                userData.WatchedTimeTicksDaily = 0;
                userData.TimeCreditTicksDaily = 0;
                userData.LastDailyReset = now;
                _dailyThresholdsNotified.TryRemove(userConfig.UserId, out _);
                needsOverallReset = true;
            }

            int daysSinceResetDay = (int)now.DayOfWeek - (int)userConfig.WeeklyResetDay;
            if (daysSinceResetDay < 0) daysSinceResetDay += 7;
            DateTime lastWeeklyTrigger = now.Date.AddDays(-daysSinceResetDay).AddHours(userConfig.ResetTimeOfDayHours);
            if (now >= lastWeeklyTrigger && userData.LastWeeklyReset < lastWeeklyTrigger)
            {
                userData.WatchedTimeTicksWeekly = 0;
                userData.TimeCreditTicksWeekly = 0;
                userData.LastWeeklyReset = now;
                _weeklyThresholdsNotified.TryRemove(userConfig.UserId, out _);
                needsOverallReset = true;
            }

            try
            {
                int resetDay = Math.Min(userConfig.MonthlyResetDay, DateTime.DaysInMonth(now.Year, now.Month));
                DateTime lastMonthlyTrigger = new DateTime(now.Year, now.Month, resetDay).AddHours(userConfig.ResetTimeOfDayHours);
                if (now < lastMonthlyTrigger)
                {
                    var prevMonth = now.AddMonths(-1);
                    resetDay = Math.Min(userConfig.MonthlyResetDay, DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month));
                    lastMonthlyTrigger = new DateTime(prevMonth.Year, prevMonth.Month, resetDay).AddHours(userConfig.ResetTimeOfDayHours);
                }
                if (now >= lastMonthlyTrigger && userData.LastMonthlyReset < lastMonthlyTrigger)
                {
                    userData.WatchedTimeTicksMonthly = 0;
                    userData.TimeCreditTicksMonthly = 0;
                    userData.LastMonthlyReset = now;
                    _monthlyThresholdsNotified.TryRemove(userConfig.UserId, out _);
                    needsOverallReset = true;
                }
            }
            catch (Exception ex) { _logger?.ErrorException("Error calculating monthly reset for user {0}", ex, userConfig.Username); }


            try
            {
                DateTime lastYearlyTrigger = new DateTime(now.Year, userConfig.YearlyResetMonth, userConfig.YearlyResetDay).AddHours(userConfig.ResetTimeOfDayHours);
                if (now < lastYearlyTrigger) lastYearlyTrigger = lastYearlyTrigger.AddYears(-1);
                if (now >= lastYearlyTrigger && userData.LastYearlyReset < lastYearlyTrigger)
                {
                    userData.WatchedTimeTicksYearly = 0;
                    userData.TimeCreditTicksYearly = 0;
                    userData.LastYearlyReset = now;
                    _yearlyThresholdsNotified.TryRemove(userConfig.UserId, out _);
                    needsOverallReset = true;
                }
            }
            catch (Exception ex) { _logger?.ErrorException("Error calculating yearly reset for user {0}", ex, userConfig.Username); }


            if (needsOverallReset)
            {
                _limitReachedNotified.TryRemove(userConfig.UserId, out _);
                dataChanged = true;
                _logger?.Info($"[WatchTimeManager] Performed one or more scheduled resets for user {userConfig.Username}.");
            }
            return dataChanged;
        }

        private static bool IsOutsideTimeWindow(LimitedUser userConfig, DateTime now)
        {
            var startHour = userConfig.WatchWindowStartHour;
            var endHour = userConfig.WatchWindowEndHour;
            var currentHour = now.TimeOfDay.TotalHours;

            if (startHour >= endHour)
                return currentHour >= endHour && currentHour < startHour;
            else
                return currentHour < startHour || currentHour >= endHour;
        }

        private static void ProcessThresholdNotifications(SessionInfo session, LimitedUser userConfig, UserWatchData userData)
        {
            if (!userConfig.EnableThresholdNotifications || string.IsNullOrWhiteSpace(userConfig.NotificationThresholds)) return;

            var thresholds = userConfig.NotificationThresholds.Split(',')
                .Select(s => int.TryParse(s.Trim(), out var p) ? p : -1)
                .Where(p => p > 0 && p < 100).ToList();

            if (!thresholds.Any()) return;

            if (userConfig.EnableDailyLimit && userConfig.DailyLimitMinutes > 0)
                CheckAndSendThresholdNotification(session, "daily", TimeSpan.FromMinutes(userConfig.DailyLimitMinutes).Add(TimeSpan.FromTicks(userData.TimeCreditTicksDaily)), TimeSpan.FromTicks(userData.WatchedTimeTicksDaily), thresholds, _dailyThresholdsNotified);

            if (userConfig.EnableWeeklyLimit && userConfig.WeeklyLimitHours > 0)
                CheckAndSendThresholdNotification(session, "weekly", TimeSpan.FromHours(userConfig.WeeklyLimitHours).Add(TimeSpan.FromTicks(userData.TimeCreditTicksWeekly)), TimeSpan.FromTicks(userData.WatchedTimeTicksWeekly), thresholds, _weeklyThresholdsNotified);

            if (userConfig.EnableMonthlyLimit && userConfig.MonthlyLimitHours > 0)
                CheckAndSendThresholdNotification(session, "monthly", TimeSpan.FromHours(userConfig.MonthlyLimitHours).Add(TimeSpan.FromTicks(userData.TimeCreditTicksMonthly)), TimeSpan.FromTicks(userData.WatchedTimeTicksMonthly), thresholds, _monthlyThresholdsNotified);

            if (userConfig.EnableYearlyLimit && userConfig.YearlyLimitHours > 0)
                CheckAndSendThresholdNotification(session, "yearly", TimeSpan.FromHours(userConfig.YearlyLimitHours).Add(TimeSpan.FromTicks(userData.TimeCreditTicksYearly)), TimeSpan.FromTicks(userData.WatchedTimeTicksYearly), thresholds, _yearlyThresholdsNotified);
        }

        private static void CheckAndSendThresholdNotification(SessionInfo session, string period, TimeSpan limit, TimeSpan watched, List<int> thresholds, ConcurrentDictionary<string, HashSet<int>> notifiedDict)
        {
            if (limit.TotalSeconds <= 0) return;

            var percentageWatched = (watched.TotalSeconds / limit.TotalSeconds) * 100;
            var notifiedForUser = notifiedDict.GetOrAdd(session.UserId, new HashSet<int>());

            foreach (var threshold in thresholds)
            {
                if (percentageWatched >= threshold && !notifiedForUser.Contains(threshold))
                {
                    var message = $"Watch Time Warning: You have used over {threshold}% of your {period} limit.";
                    SendNotificationAsync(session, "Watch Time Warning", message, 10000).GetAwaiter().GetResult();
                    notifiedForUser.Add(threshold);

                    var config = Plugin.Instance?.Configuration;
                    if (config != null && config.EnableWatchLimitNotifications && !string.IsNullOrEmpty(session.UserName))
                    {
                        NotificationService.SendThresholdNotification(session.UserName, period, threshold);
                    }
                }
            }
        }

        public static PlaybackBlockReason GetPlaybackBlockReason(string userId)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableWatchTimeLimiter || string.IsNullOrEmpty(userId)) return PlaybackBlockReason.Allowed;

            var limitedUser = config.LimitedUsers.FirstOrDefault(u => u.UserId == userId);
            if (limitedUser == null || !limitedUser.IsEnabled) return PlaybackBlockReason.Allowed;

            if (limitedUser.EnableTimeWindow && IsOutsideTimeWindow(limitedUser, DateTime.Now))
                return PlaybackBlockReason.OutsideTimeWindow;

            if (_limitReachedNotified.ContainsKey(userId)) return PlaybackBlockReason.TimeLimitExceeded;

            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });

            if (limitedUser.EnableDailyLimit && limitedUser.DailyLimitMinutes > 0 && userData.WatchedTimeTicksDaily >= (TimeSpan.FromMinutes(limitedUser.DailyLimitMinutes).Ticks + userData.TimeCreditTicksDaily))
                return PlaybackBlockReason.TimeLimitExceeded;

            if (limitedUser.EnableWeeklyLimit && limitedUser.WeeklyLimitHours > 0 && userData.WatchedTimeTicksWeekly >= (TimeSpan.FromHours(limitedUser.WeeklyLimitHours).Ticks + userData.TimeCreditTicksWeekly))
                return PlaybackBlockReason.TimeLimitExceeded;

            if (limitedUser.EnableMonthlyLimit && limitedUser.MonthlyLimitHours > 0 && userData.WatchedTimeTicksMonthly >= (TimeSpan.FromHours(limitedUser.MonthlyLimitHours).Ticks + userData.TimeCreditTicksMonthly))
                return PlaybackBlockReason.TimeLimitExceeded;

            if (limitedUser.EnableYearlyLimit && limitedUser.YearlyLimitHours > 0 && userData.WatchedTimeTicksYearly >= (TimeSpan.FromHours(limitedUser.YearlyLimitHours).Ticks + userData.TimeCreditTicksYearly))
                return PlaybackBlockReason.TimeLimitExceeded;

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
                if (reason == PlaybackBlockReason.TimeLimitExceeded && _limitReachedNotified.TryAdd(userId, true))
                {
                    LogManager.LogLimitReached(userId, username, "System");
                }
                return;
            }

            string message;
            string header;
            switch (reason)
            {
                case PlaybackBlockReason.TimeLimitExceeded:
                    message = config.WatchTimeLimitMessageText;
                    header = "Time Limit Reached";
                    if (_limitReachedNotified.TryAdd(userId, true))
                    {
                        var clientNames = string.Join(", ", allUserSessions.Select(s => s.Client).Distinct());
                        LogManager.LogLimitReached(userId, username, clientNames);

                        if (config.EnableWatchLimitNotifications)
                        {
                            NotificationService.SendLimitReachedNotification(username, clientNames);
                        }
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
                await _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                await SendNotificationAsync(session, header, message, 10000);
            }
        }

        private static async Task SendNotificationAsync(SessionInfo session, string header, string text, int? timeoutMs)
        {
            if (_sessionManager == null) return;
            var message = new MessageCommand
            {
                Header = header,
                Text = text,
                TimeoutMs = timeoutMs
            };
            await _sessionManager.SendMessageCommand(null, session.Id, message, CancellationToken.None).ConfigureAwait(false);
        }

        public static List<LimitedUserStatus> GetLimitedUsersStatus()
        {
            var config = Plugin.Instance?.Configuration;
            var statusList = new List<LimitedUserStatus>();
            if (config == null || !config.EnableWatchTimeLimiter) return statusList;

            foreach (var limitedUser in config.LimitedUsers)
            {
                var userData = _userWatchData.GetOrAdd(limitedUser.UserId, new UserWatchData { UserId = limitedUser.UserId });
                statusList.Add(new LimitedUserStatus
                {
                    UserId = limitedUser.UserId,
                    Username = limitedUser.Username,
                    DailyLimitMinutes = limitedUser.DailyLimitMinutes + (int)TimeSpan.FromTicks(userData.TimeCreditTicksDaily).TotalMinutes,
                    WeeklyLimitHours = limitedUser.WeeklyLimitHours + (int)TimeSpan.FromTicks(userData.TimeCreditTicksWeekly).TotalHours,
                    MonthlyLimitHours = limitedUser.MonthlyLimitHours + (int)TimeSpan.FromTicks(userData.TimeCreditTicksMonthly).TotalHours,
                    YearlyLimitHours = limitedUser.YearlyLimitHours + (int)TimeSpan.FromTicks(userData.TimeCreditTicksYearly).TotalHours,
                    SecondsWatchedDaily = TimeSpan.FromTicks(userData.WatchedTimeTicksDaily).TotalSeconds,
                    SecondsWatchedWeekly = TimeSpan.FromTicks(userData.WatchedTimeTicksWeekly).TotalSeconds,
                    SecondsWatchedMonthly = TimeSpan.FromTicks(userData.WatchedTimeTicksMonthly).TotalSeconds,
                    SecondsWatchedYearly = TimeSpan.FromTicks(userData.WatchedTimeTicksYearly).TotalSeconds,
                });
            }
            return statusList;
        }

        public static void ExtendTimeForUser(string userId, int minutesToExtend)
        {
            if (!_userWatchData.TryGetValue(userId, out var userData)) return;

            var timeCreditToAdd = TimeSpan.FromMinutes(minutesToExtend);

            userData.TimeCreditTicksDaily += timeCreditToAdd.Ticks;
            userData.TimeCreditTicksWeekly += timeCreditToAdd.Ticks;
            userData.TimeCreditTicksMonthly += timeCreditToAdd.Ticks;
            userData.TimeCreditTicksYearly += timeCreditToAdd.Ticks;

            _limitReachedNotified.TryRemove(userId, out _);
            SaveWatchTimeData();
        }

        public static void ResetWatchTimeForUser(string userId)
        {
            if (_userWatchData.TryGetValue(userId, out var userData))
            {
                userData.WatchedTimeTicksDaily = 0;
                userData.TimeCreditTicksDaily = 0;
                userData.WatchedTimeTicksWeekly = 0;
                userData.TimeCreditTicksWeekly = 0;
                userData.WatchedTimeTicksMonthly = 0;
                userData.TimeCreditTicksMonthly = 0;
                userData.WatchedTimeTicksYearly = 0;
                userData.TimeCreditTicksYearly = 0;

                _limitReachedNotified.TryRemove(userId, out _);
                _dailyThresholdsNotified.TryRemove(userId, out _);
                _weeklyThresholdsNotified.TryRemove(userId, out _);
                _monthlyThresholdsNotified.TryRemove(userId, out _);
                _yearlyThresholdsNotified.TryRemove(userId, out _);
                SaveWatchTimeData();
            }
        }

        public static void ResetAllWatchTimes()
        {
            foreach (var userId in _userWatchData.Keys)
            {
                ResetWatchTimeForUser(userId);
            }
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
                    _userWatchData = new ConcurrentDictionary<string, UserWatchData>();
                    foreach (var user in data.UserWatchTimes)
                    {
                        if (user.WatchedTimeTicks > 0 && user.WatchedTimeTicksDaily == 0)
                        {
                            user.WatchedTimeTicksDaily = user.WatchedTimeTicks;
                            user.LastDailyReset = user.LastResetTime;
                        }
                        _userWatchData.TryAdd(user.UserId, user);
                    }
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
                var data = new WatchTimePersistenceData { UserWatchTimes = _userWatchData.Values.ToList() };
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