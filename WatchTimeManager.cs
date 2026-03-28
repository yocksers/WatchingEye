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
    public enum PlaybackBlockReason { Allowed, TimeLimitExceeded, OutsideTimeWindow, TimedOut }

    public static class WatchTimeManager
    {
        private static Timer? _updateTimer;
        private static ISessionManager? _sessionManager;
        private static ILogger? _logger;
        private static IJsonSerializer? _jsonSerializer;
        private static string? _watchTimeDataPath;

        private static bool _isRunning = false;
        private static readonly int TimerIntervalSeconds = 15;
        private static readonly int DeferredSaveIntervalSeconds = 300;

        private static ConcurrentDictionary<string, UserWatchData> _userWatchData = new();
        private static DateTime _lastTimerFire = DateTime.UtcNow;

        private static bool _isDirty = false;
        private static DateTime _lastSaveTime = DateTime.UtcNow;

        private static readonly ConcurrentDictionary<string, bool> _limitServerNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _dailyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _weeklyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _monthlyThresholdsNotified = new();
        private static readonly ConcurrentDictionary<string, HashSet<int>> _yearlyThresholdsNotified = new();

        private static readonly ConcurrentDictionary<string, List<int>> _parsedThresholdsCache = new();
        private static string _thresholdCacheConfigVersion = string.Empty;

        private static readonly object _saveLock = new object();

        public static void Start(ISessionManager sessionManager, ILogger logger, IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
        {
            if (_isRunning) return;

            _sessionManager = sessionManager;
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.WatchTime.json");

            LoadWatchTimeData();

            _lastTimerFire = DateTime.UtcNow;
            var interval = TimeSpan.FromSeconds(TimerIntervalSeconds);
            _updateTimer = new Timer(OnTimerElapsed, null, interval, interval);

            _isRunning = true;
            _logger.Info("[WatchTimeManager] Started.");
        }

        public static void Stop()
        {
            SaveWatchTimeDataImmediate();
            _updateTimer?.Dispose();
            _updateTimer = null;
            _isRunning = false;
            _logger?.Info("[WatchTimeManager] Stopped.");
        }

        public static void OnSessionStopped(string sessionId)
        {
        }

        private static LimitedUser? GetUserConfig(string userId, PluginConfiguration config)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            if (config.ExcludedUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase)) return null;

            var specificUser = config.LimitedUsers.FirstOrDefault(u => u.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
            if (specificUser != null)
                return specificUser.IsEnabled ? specificUser : null;

            if (config.EnableGlobalLimit)
                return config.GlobalLimitedUser;

            return null;
        }

        private static void OnTimerElapsed(object? state)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var elapsed = nowUtc - _lastTimerFire;
                _lastTimerFire = nowUtc;

                var maxElapsed = TimeSpan.FromSeconds(TimerIntervalSeconds * 2);
                if (elapsed > maxElapsed) elapsed = maxElapsed;

                var config = Plugin.Instance?.Configuration;
                if (config == null || _sessionManager == null || _logger == null) return;

                if (!config.EnableWatchTimeLimiter)
                    return;

                bool dataChanged = false;

                foreach (var userId in _userWatchData.Keys)
                {
                    var userConfig = GetUserConfig(userId, config);
                    if (userConfig != null)
                        dataChanged |= ProcessResetsForUser(userId, userConfig, nowUtc);
                }

                var activeUserIds = _sessionManager.Sessions
                    .Where(s => !string.IsNullOrEmpty(s.UserId) && s.NowPlayingItem != null && s.PlayState is { IsPaused: false })
                    .Select(s => s.UserId)
                    .Distinct()
                    .ToList();

                foreach (var userId in activeUserIds)
                {
                    var userConfig = GetUserConfig(userId, config);
                    if (userConfig == null) continue;

                    var blockReason = GetPlaybackBlockReason(userId);
                    if (blockReason != PlaybackBlockReason.Allowed)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await StopPlaybackForUser(userId, blockReason).ConfigureAwait(false); }
                            catch (Exception ex) { _logger?.ErrorException($"[WatchTimeManager] Error stopping playback for user {userId}.", ex); }
                        });
                        continue;
                    }

                    var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });
                    var ticksToAdd = elapsed.Ticks;

                    userData.WatchedTimeTicksDaily += ticksToAdd;
                    userData.WatchedTimeTicksWeekly += ticksToAdd;
                    userData.WatchedTimeTicksMonthly += ticksToAdd;
                    userData.WatchedTimeTicksYearly += ticksToAdd;
                    dataChanged = true;

                    if (GetPlaybackBlockReason(userId) == PlaybackBlockReason.TimeLimitExceeded)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await StopPlaybackForUser(userId, PlaybackBlockReason.TimeLimitExceeded).ConfigureAwait(false); }
                            catch (Exception ex) { _logger?.ErrorException($"[WatchTimeManager] Error stopping playback for user {userId} (limit exceeded).", ex); }
                        });
                    }
                    else
                    {
                        var session = _sessionManager.Sessions
                            .FirstOrDefault(s => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.NowPlayingItem != null);
                        if (session != null)
                            ProcessThresholdNotifications(session, userConfig, userData);
                    }
                }

                if (dataChanged) _isDirty = true;

                if (_isDirty && (nowUtc - _lastSaveTime).TotalSeconds >= DeferredSaveIntervalSeconds)
                {
                    SaveWatchTimeData();
                    _isDirty = false;
                    _lastSaveTime = nowUtc;
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[WatchTimeManager] Error during timer tick.", ex);
            }
        }

        private static bool ProcessResetsForUser(string userId, LimitedUser userConfig, DateTime nowUtc)
        {
            var dataChanged = false;
            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });
            var localNow = nowUtc.ToLocalTime();

            if (userData.LastDailyReset == DateTime.MinValue) userData.LastDailyReset = nowUtc;
            if (userData.LastWeeklyReset == DateTime.MinValue) userData.LastWeeklyReset = nowUtc;
            if (userData.LastMonthlyReset == DateTime.MinValue) userData.LastMonthlyReset = nowUtc;
            if (userData.LastYearlyReset == DateTime.MinValue) userData.LastYearlyReset = nowUtc;

            if (userConfig.EnableDailyReset)
            {
                var trigger = GetLastDailyResetTrigger(localNow, userConfig.ResetTimeOfDayHours);
                if (nowUtc >= trigger && userData.LastDailyReset < trigger)
                {
                    userData.WatchedTimeTicksDaily = 0;
                    userData.TimeCreditTicksDaily = 0;
                    userData.LastDailyReset = nowUtc;
                    _dailyThresholdsNotified.TryRemove(userId, out _);
                    _limitServerNotified.TryRemove(userId, out _);
                    dataChanged = true;
                    _logger?.Info($"[WatchTimeManager] Daily reset for user {userConfig.Username}.");
                }
            }

            if (userConfig.EnableWeeklyReset)
            {
                var trigger = GetLastWeeklyResetTrigger(localNow, userConfig.WeeklyResetDay, userConfig.ResetTimeOfDayHours);
                if (nowUtc >= trigger && userData.LastWeeklyReset < trigger)
                {
                    userData.WatchedTimeTicksWeekly = 0;
                    userData.TimeCreditTicksWeekly = 0;
                    userData.LastWeeklyReset = nowUtc;
                    _weeklyThresholdsNotified.TryRemove(userId, out _);
                    _limitServerNotified.TryRemove(userId, out _);
                    dataChanged = true;
                    _logger?.Info($"[WatchTimeManager] Weekly reset for user {userConfig.Username}.");
                }
            }

            if (userConfig.EnableMonthlyReset)
            {
                try
                {
                    var trigger = GetLastMonthlyResetTrigger(localNow, userConfig.MonthlyResetDay, userConfig.ResetTimeOfDayHours);
                    if (nowUtc >= trigger && userData.LastMonthlyReset < trigger)
                    {
                        userData.WatchedTimeTicksMonthly = 0;
                        userData.TimeCreditTicksMonthly = 0;
                        userData.LastMonthlyReset = nowUtc;
                        _monthlyThresholdsNotified.TryRemove(userId, out _);
                        _limitServerNotified.TryRemove(userId, out _);
                        dataChanged = true;
                        _logger?.Info($"[WatchTimeManager] Monthly reset for user {userConfig.Username}.");
                    }
                }
                catch (Exception ex) { _logger?.ErrorException($"Error calculating monthly reset for user {userConfig.Username}", ex); }
            }

            if (userConfig.EnableYearlyReset)
            {
                try
                {
                    var trigger = GetLastYearlyResetTrigger(localNow, userConfig.YearlyResetMonth, userConfig.YearlyResetDay, userConfig.ResetTimeOfDayHours);
                    if (nowUtc >= trigger && userData.LastYearlyReset < trigger)
                    {
                        userData.WatchedTimeTicksYearly = 0;
                        userData.TimeCreditTicksYearly = 0;
                        userData.LastYearlyReset = nowUtc;
                        _yearlyThresholdsNotified.TryRemove(userId, out _);
                        _limitServerNotified.TryRemove(userId, out _);
                        dataChanged = true;
                        _logger?.Info($"[WatchTimeManager] Yearly reset for user {userConfig.Username}.");
                    }
                }
                catch (Exception ex) { _logger?.ErrorException($"Error calculating yearly reset for user {userConfig.Username}", ex); }
            }

            return dataChanged;
        }

        private static DateTime GetLastDailyResetTrigger(DateTime localNow, double resetHour)
        {
            var trigger = localNow.Date.AddHours(resetHour);
            if (localNow < trigger) trigger = trigger.AddDays(-1);
            return trigger.ToUniversalTime();
        }

        private static DateTime GetLastWeeklyResetTrigger(DateTime localNow, DayOfWeek resetDay, double resetHour)
        {
            int daysBack = ((int)localNow.DayOfWeek - (int)resetDay + 7) % 7;
            var trigger = localNow.Date.AddDays(-daysBack).AddHours(resetHour);
            return trigger.ToUniversalTime();
        }

        private static DateTime GetLastMonthlyResetTrigger(DateTime localNow, int resetDay, double resetHour)
        {
            int clampedDay = Math.Min(resetDay, DateTime.DaysInMonth(localNow.Year, localNow.Month));
            var trigger = new DateTime(localNow.Year, localNow.Month, clampedDay, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
            if (localNow < trigger)
            {
                var prev = localNow.AddMonths(-1);
                clampedDay = Math.Min(resetDay, DateTime.DaysInMonth(prev.Year, prev.Month));
                trigger = new DateTime(prev.Year, prev.Month, clampedDay, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
            }
            return trigger.ToUniversalTime();
        }

        private static DateTime GetLastYearlyResetTrigger(DateTime localNow, int resetMonth, int resetDay, double resetHour)
        {
            var trigger = new DateTime(localNow.Year, resetMonth, resetDay, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
            if (localNow < trigger) trigger = trigger.AddYears(-1);
            return trigger.ToUniversalTime();
        }

        private static int GetEffectiveDailyLimitMinutes(LimitedUser userConfig)
        {
            if (!userConfig.EnablePerDayLimits || userConfig.PerDayLimits == null || !userConfig.PerDayLimits.Any())
                return userConfig.DailyLimitMinutes;

            var today = DateTime.Now.DayOfWeek;
            var dayOverride = userConfig.PerDayLimits.FirstOrDefault(o => o.Day == today && o.IsEnabled);
            if (dayOverride != null)
                return dayOverride.LimitMinutes;

            // Per-day is enabled but today has no override — fall back to the flat daily limit only if it is also enabled
            return userConfig.EnableDailyLimit ? userConfig.DailyLimitMinutes : 0;
        }

        private static bool IsOutsideTimeWindow(LimitedUser userConfig, DateTime localNow)
        {
            var rule = userConfig.TimeWindows?.FirstOrDefault(w => w.Day == localNow.DayOfWeek);
            if (rule == null || !rule.IsEnabled) return true;

            var current = localNow.TimeOfDay.TotalHours;
            if (rule.StartHour >= rule.EndHour)
                return current >= rule.EndHour && current < rule.StartHour;
            else
                return current < rule.StartHour || current >= rule.EndHour;
        }

        public static PlaybackBlockReason GetPlaybackBlockReason(string userId)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableWatchTimeLimiter || string.IsNullOrEmpty(userId))
                return PlaybackBlockReason.Allowed;

            var userConfig = GetUserConfig(userId, config);
            if (userConfig == null) return PlaybackBlockReason.Allowed;

            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });

            if (userData.TimeOutUntil > DateTime.UtcNow)
                return PlaybackBlockReason.TimedOut;

            if (userConfig.EnableTimeWindows && IsOutsideTimeWindow(userConfig, DateTime.Now))
                return PlaybackBlockReason.OutsideTimeWindow;

            if (userConfig.EnableDailyLimit || userConfig.EnablePerDayLimits)
            {
                var dailyLimitMinutes = GetEffectiveDailyLimitMinutes(userConfig);
                if (dailyLimitMinutes > 0 &&
                    userData.WatchedTimeTicksDaily >= TimeSpan.FromMinutes(dailyLimitMinutes).Ticks + userData.TimeCreditTicksDaily)
                    return PlaybackBlockReason.TimeLimitExceeded;
            }

            if (userConfig.EnableWeeklyLimit && userConfig.WeeklyLimitHours > 0 &&
                userData.WatchedTimeTicksWeekly >= TimeSpan.FromHours(userConfig.WeeklyLimitHours).Ticks + userData.TimeCreditTicksWeekly)
                return PlaybackBlockReason.TimeLimitExceeded;

            if (userConfig.EnableMonthlyLimit && userConfig.MonthlyLimitHours > 0 &&
                userData.WatchedTimeTicksMonthly >= TimeSpan.FromHours(userConfig.MonthlyLimitHours).Ticks + userData.TimeCreditTicksMonthly)
                return PlaybackBlockReason.TimeLimitExceeded;

            if (userConfig.EnableYearlyLimit && userConfig.YearlyLimitHours > 0 &&
                userData.WatchedTimeTicksYearly >= TimeSpan.FromHours(userConfig.YearlyLimitHours).Ticks + userData.TimeCreditTicksYearly)
                return PlaybackBlockReason.TimeLimitExceeded;

            return PlaybackBlockReason.Allowed;
        }

        private static void ProcessThresholdNotifications(SessionInfo session, LimitedUser userConfig, UserWatchData userData)
        {
            if (!userConfig.EnableThresholdNotifications || string.IsNullOrWhiteSpace(userConfig.NotificationThresholds)) return;

            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            if (config.ConfigurationVersion != _thresholdCacheConfigVersion)
            {
                _parsedThresholdsCache.Clear();
                _thresholdCacheConfigVersion = config.ConfigurationVersion;
            }

            var cacheKey = $"{userConfig.UserId}_{userConfig.NotificationThresholds}";
            var thresholds = _parsedThresholdsCache.GetOrAdd(cacheKey, _ =>
                userConfig.NotificationThresholds.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out var p) ? p : -1)
                    .Where(p => p > 0 && p < 100)
                    .ToList());

            if (!thresholds.Any()) return;

            var userId = session.UserId;

            if (userConfig.EnableDailyLimit || userConfig.EnablePerDayLimits)
            {
                var dailyLimitMinutes = GetEffectiveDailyLimitMinutes(userConfig);
                if (dailyLimitMinutes > 0)
                {
                    var limit = TimeSpan.FromMinutes(dailyLimitMinutes) + TimeSpan.FromTicks(userData.TimeCreditTicksDaily);
                    CheckAndSendThresholdNotification(session, userId, "daily", limit, TimeSpan.FromTicks(userData.WatchedTimeTicksDaily), thresholds, _dailyThresholdsNotified);
                }
            }

            if (userConfig.EnableWeeklyLimit && userConfig.WeeklyLimitHours > 0)
            {
                var limit = TimeSpan.FromHours(userConfig.WeeklyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksWeekly);
                CheckAndSendThresholdNotification(session, userId, "weekly", limit, TimeSpan.FromTicks(userData.WatchedTimeTicksWeekly), thresholds, _weeklyThresholdsNotified);
            }

            if (userConfig.EnableMonthlyLimit && userConfig.MonthlyLimitHours > 0)
            {
                var limit = TimeSpan.FromHours(userConfig.MonthlyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksMonthly);
                CheckAndSendThresholdNotification(session, userId, "monthly", limit, TimeSpan.FromTicks(userData.WatchedTimeTicksMonthly), thresholds, _monthlyThresholdsNotified);
            }

            if (userConfig.EnableYearlyLimit && userConfig.YearlyLimitHours > 0)
            {
                var limit = TimeSpan.FromHours(userConfig.YearlyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksYearly);
                CheckAndSendThresholdNotification(session, userId, "yearly", limit, TimeSpan.FromTicks(userData.WatchedTimeTicksYearly), thresholds, _yearlyThresholdsNotified);
            }
        }

        private static void CheckAndSendThresholdNotification(SessionInfo session, string userId, string period, TimeSpan limit, TimeSpan watched, List<int> thresholds, ConcurrentDictionary<string, HashSet<int>> notifiedDict)
        {
            if (limit.TotalSeconds <= 0) return;

            var pct = (watched.TotalSeconds / limit.TotalSeconds) * 100.0;
            var notified = notifiedDict.GetOrAdd(userId, new HashSet<int>());

            foreach (var threshold in thresholds)
            {
                if (pct >= threshold && !notified.Contains(threshold))
                {
                    notified.Add(threshold);
                    var message = $"Watch Time Warning: You have used over {threshold}% of your {period} limit.";
                    _ = Task.Run(async () =>
                    {
                        try { await InAppNotificationService.SendNotificationAsync(session, "Watch Time Warning", message, false).ConfigureAwait(false); }
                        catch (Exception ex) { _logger?.ErrorException("[WatchTimeManager] Error sending threshold notification.", ex); }
                    });

                    var cfg = Plugin.Instance?.Configuration;
                    if (cfg != null && cfg.EnableWatchLimitNotifications && !string.IsNullOrEmpty(session.UserName))
                        ServerNotificationService.SendThresholdNotification(session.UserName, period, threshold);
                }
            }
        }

        private static (string header, string message) BuildBlockMessage(PlaybackBlockReason reason, string userId, PluginConfiguration config)
        {
            switch (reason)
            {
                case PlaybackBlockReason.TimeLimitExceeded:
                    return ("Time Limit Reached", config.WatchTimeLimitMessageText);

                case PlaybackBlockReason.OutsideTimeWindow:
                {
                    var userConfig = GetUserConfig(userId, config);
                    var msg = config.TimeWindowBlockedMessageText;
                    if (userConfig != null)
                    {
                        var rule = userConfig.TimeWindows?.FirstOrDefault(d => d.Day == DateTime.Now.DayOfWeek);
                        if (rule != null)
                        {
                            msg = msg.Replace("{start_time}", FormatTimeFromDouble(rule.StartHour))
                                     .Replace("{end_time}", FormatTimeFromDouble(rule.EndHour));
                        }
                    }
                    return ("Playback Not Allowed", msg);
                }

                case PlaybackBlockReason.TimedOut:
                {
                    var msg = config.TimeOutMessageText;
                    if (_userWatchData.TryGetValue(userId, out var ud))
                        msg = msg.Replace("{duration}", FormatTimeSpan(ud.TimeOutUntil - DateTime.UtcNow));
                    else
                        msg = msg.Replace(" for the next {duration}", string.Empty);
                    return ("Playback Disabled", msg);
                }

                default:
                    return (string.Empty, string.Empty);
            }
        }

        public static async Task StopPlaybackForUser(string userId, PlaybackBlockReason reason)
        {
            if (_sessionManager == null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var sessions = _sessionManager.Sessions
                .Where(s => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.IsActive)
                .ToList();

            var username = sessions.FirstOrDefault(s => !string.IsNullOrEmpty(s.UserName))?.UserName ?? "Unknown";

            if (reason == PlaybackBlockReason.TimeLimitExceeded && _limitServerNotified.TryAdd(userId, true))
            {
                var clients = string.Join(", ", sessions.Select(s => s.Client).Distinct());
                LogManager.LogLimitReached(userId, username, clients.Length > 0 ? clients : "System");
                if (config.EnableWatchLimitNotifications)
                    ServerNotificationService.SendLimitReachedNotification(username, clients);
            }

            if (!sessions.Any()) return;

            var (header, message) = BuildBlockMessage(reason, userId, config);
            if (string.IsNullOrEmpty(header)) return;

            foreach (var session in sessions)
            {
                await _sessionManager.SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
                await InAppNotificationService.SendNotificationAsync(session, header, message, config.EnableConfirmationButtonOnWatchTimeLimit);
            }
        }

        public static async Task SendBlockNotificationToUser(string userId, PlaybackBlockReason reason)
        {
            if (_sessionManager == null) return;
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var sessions = _sessionManager.Sessions
                .Where(s => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.IsActive)
                .ToList();

            if (!sessions.Any()) return;

            var (header, message) = BuildBlockMessage(reason, userId, config);
            if (string.IsNullOrEmpty(header)) return;

            foreach (var session in sessions)
                await InAppNotificationService.SendNotificationAsync(session, header, message, config.EnableConfirmationButtonOnWatchTimeLimit);
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
                    DailyLimitMinutes = limitedUser.DailyLimitMinutes,
                    WeeklyLimitHours = limitedUser.WeeklyLimitHours,
                    MonthlyLimitHours = limitedUser.MonthlyLimitHours,
                    YearlyLimitHours = limitedUser.YearlyLimitHours,
                    SecondsWatchedDaily = TimeSpan.FromTicks(userData.WatchedTimeTicksDaily).TotalSeconds,
                    SecondsWatchedWeekly = TimeSpan.FromTicks(userData.WatchedTimeTicksWeekly).TotalSeconds,
                    SecondsWatchedMonthly = TimeSpan.FromTicks(userData.WatchedTimeTicksMonthly).TotalSeconds,
                    SecondsWatchedYearly = TimeSpan.FromTicks(userData.WatchedTimeTicksYearly).TotalSeconds,
                    CreditSecondsDaily = TimeSpan.FromTicks(userData.TimeCreditTicksDaily).TotalSeconds,
                    CreditSecondsWeekly = TimeSpan.FromTicks(userData.TimeCreditTicksWeekly).TotalSeconds,
                    CreditSecondsMonthly = TimeSpan.FromTicks(userData.TimeCreditTicksMonthly).TotalSeconds,
                    CreditSecondsYearly = TimeSpan.FromTicks(userData.TimeCreditTicksYearly).TotalSeconds,
                    TimeOutUntil = userData.TimeOutUntil,
                });
            }
            return statusList;
        }

        public static void ExtendTimeForUser(string userId, int minutesToExtend)
        {
            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });

            var ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;
            userData.TimeCreditTicksDaily += ticks;
            userData.TimeCreditTicksWeekly += ticks;
            userData.TimeCreditTicksMonthly += ticks;
            userData.TimeCreditTicksYearly += ticks;

            _limitServerNotified.TryRemove(userId, out _);
            SaveWatchTimeDataImmediate();
        }

        public static void ExtendPeriodTimeForUser(string userId, string period, int minutesToExtend)
        {
            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });
            var ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;

            switch (period.ToLowerInvariant())
            {
                case "daily":   userData.TimeCreditTicksDaily   += ticks; break;
                case "weekly":  userData.TimeCreditTicksWeekly  += ticks; break;
                case "monthly": userData.TimeCreditTicksMonthly += ticks; break;
                case "yearly":  userData.TimeCreditTicksYearly  += ticks; break;
                default: return;
            }

            _limitServerNotified.TryRemove(userId, out _);
            SaveWatchTimeDataImmediate();
        }

        public static void ResetPeriodTimeForUser(string userId, string period)
        {
            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });

            switch (period.ToLowerInvariant())
            {
                case "daily":
                    userData.WatchedTimeTicksDaily = 0;
                    userData.TimeCreditTicksDaily = 0;
                    _dailyThresholdsNotified.TryRemove(userId, out _);
                    break;
                case "weekly":
                    userData.WatchedTimeTicksWeekly = 0;
                    userData.TimeCreditTicksWeekly = 0;
                    _weeklyThresholdsNotified.TryRemove(userId, out _);
                    break;
                case "monthly":
                    userData.WatchedTimeTicksMonthly = 0;
                    userData.TimeCreditTicksMonthly = 0;
                    _monthlyThresholdsNotified.TryRemove(userId, out _);
                    break;
                case "yearly":
                    userData.WatchedTimeTicksYearly = 0;
                    userData.TimeCreditTicksYearly = 0;
                    _yearlyThresholdsNotified.TryRemove(userId, out _);
                    break;
                default: return;
            }

            _limitServerNotified.TryRemove(userId, out _);
            SaveWatchTimeDataImmediate();
        }

        public static void TimeOutUser(string userId, int minutes)
        {
            if (minutes <= 0) return;
            var userData = _userWatchData.GetOrAdd(userId, new UserWatchData { UserId = userId });
            userData.TimeOutUntil = DateTime.UtcNow.AddMinutes(minutes);
            SaveWatchTimeDataImmediate();
            _logger?.Info($"[WatchTimeManager] User {userId} has been timed out for {minutes} minutes.");
            _ = Task.Run(async () =>
            {
                try { await StopPlaybackForUser(userId, PlaybackBlockReason.TimedOut).ConfigureAwait(false); }
                catch (Exception ex) { _logger?.ErrorException($"[WatchTimeManager] Error stopping playback during timeout for user {userId}.", ex); }
            });
        }

        public static void ClearTimeOutForUser(string userId)
        {
            if (_userWatchData.TryGetValue(userId, out var userData))
            {
                userData.TimeOutUntil = DateTime.MinValue;
                SaveWatchTimeDataImmediate();
                _logger?.Info($"[WatchTimeManager] Cleared time-out for user {userId}.");
            }
        }

        public static void ResetWatchTimeForUser(string userId)
        {
            if (!_userWatchData.TryGetValue(userId, out var userData)) return;

            userData.WatchedTimeTicksDaily = 0;
            userData.TimeCreditTicksDaily = 0;
            userData.WatchedTimeTicksWeekly = 0;
            userData.TimeCreditTicksWeekly = 0;
            userData.WatchedTimeTicksMonthly = 0;
            userData.TimeCreditTicksMonthly = 0;
            userData.WatchedTimeTicksYearly = 0;
            userData.TimeCreditTicksYearly = 0;

            _limitServerNotified.TryRemove(userId, out _);
            _dailyThresholdsNotified.TryRemove(userId, out _);
            _weeklyThresholdsNotified.TryRemove(userId, out _);
            _monthlyThresholdsNotified.TryRemove(userId, out _);
            _yearlyThresholdsNotified.TryRemove(userId, out _);
            SaveWatchTimeDataImmediate();
        }

        public static void DeleteUserData(string userId)
        {
            _userWatchData.TryRemove(userId, out _);
            _limitServerNotified.TryRemove(userId, out _);
            _dailyThresholdsNotified.TryRemove(userId, out _);
            _weeklyThresholdsNotified.TryRemove(userId, out _);
            _monthlyThresholdsNotified.TryRemove(userId, out _);
            _yearlyThresholdsNotified.TryRemove(userId, out _);
            SaveWatchTimeDataImmediate();
            _logger?.Info($"[WatchTimeManager] Deleted watch data for user {userId}.");
        }

        public static void ResetAllWatchTimes()
        {
            foreach (var userId in _userWatchData.Keys)
                ResetWatchTimeForUser(userId);
        }

        private static string FormatTimeFromDouble(double hour)
        {
            return DateTime.Today.Add(TimeSpan.FromHours(hour)).ToString("t");
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds <= 1) return "a moment";

            var parts = new List<string>();

            if (timeSpan.Hours > 0)
                parts.Add($"{timeSpan.Hours} hour" + (timeSpan.Hours == 1 ? string.Empty : "s"));
            if (timeSpan.Minutes > 0)
                parts.Add($"{timeSpan.Minutes} minute" + (timeSpan.Minutes == 1 ? string.Empty : "s"));
            if (timeSpan.TotalMinutes < 2 && timeSpan.Seconds > 0)
                parts.Add($"{timeSpan.Seconds} second" + (timeSpan.Seconds == 1 ? string.Empty : "s"));

            return string.Join(" and ", parts);
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
                        user.LastDailyReset = DateTime.SpecifyKind(user.LastDailyReset, DateTimeKind.Utc);
                        user.LastWeeklyReset = DateTime.SpecifyKind(user.LastWeeklyReset, DateTimeKind.Utc);
                        user.LastMonthlyReset = DateTime.SpecifyKind(user.LastMonthlyReset, DateTimeKind.Utc);
                        user.LastYearlyReset = DateTime.SpecifyKind(user.LastYearlyReset, DateTimeKind.Utc);
                        user.TimeOutUntil = DateTime.SpecifyKind(user.TimeOutUntil, DateTimeKind.Utc);

                        if (user.WatchedTimeTicks > 0 && user.WatchedTimeTicksDaily == 0)
                        {
                            user.WatchedTimeTicksDaily = user.WatchedTimeTicks;
                            user.LastDailyReset = DateTime.SpecifyKind(user.LastResetTime, DateTimeKind.Utc);
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

            lock (_saveLock)
            {
                try
                {
                    var data = new WatchTimePersistenceData { UserWatchTimes = _userWatchData.Values.ToList() };
                    var json = _jsonSerializer.SerializeToString(data);
                    var tempPath = _watchTimeDataPath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    if (File.Exists(_watchTimeDataPath))
                        File.Replace(tempPath, _watchTimeDataPath, null);
                    else
                        File.Move(tempPath, _watchTimeDataPath);
                }
                catch (Exception ex)
                {
                    _logger?.ErrorException("[WatchTimeManager] Error saving watch time data.", ex);
                }
            }
        }

        private static void SaveWatchTimeDataImmediate()
        {
            SaveWatchTimeData();
            _isDirty = false;
            _lastSaveTime = DateTime.UtcNow;
        }
    }
}