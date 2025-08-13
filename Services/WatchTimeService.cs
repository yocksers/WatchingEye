using MediaBrowser.Model.Services;
using System.Collections.Generic;
using System.Linq;
using WatchingEye.Api;
using System;

namespace WatchingEye.Services
{
    [Route(ApiRoutes.ExtendTime, "POST", Summary = "Reduces a user's tracked watch time by a specified amount.")]
    public class ExtendTimeRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
        public int Minutes { get; set; }
    }

    [Route(ApiRoutes.LimitedUsersStatus, "GET", Summary = "Gets the status of all limited users.")]
    public class GetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>> { }

    [Route(ApiRoutes.ToggleUserLimit, "POST", Summary = "Toggles the enabled state of a limited user.")]
    public class ToggleUserLimitRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
    }

    [Route(ApiRoutes.ResetUserTime, "POST", Summary = "Resets the tracked watch time for a single user.")]
    public class ResetUserTimeRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
    }

    [Route(ApiRoutes.ResetAllUsersTime, "POST", Summary = "Resets the tracked watch time for all limited users.")]
    public class ResetAllUsersTimeRequest : IReturnVoid { }

    [Route(ApiRoutes.GetLogEvents, "GET", Summary = "Gets recent logging events from the plugin.")]
    public class GetLogEventsRequest : IReturn<List<LogEntry>> { }

    [Route(ApiRoutes.ClearLogs, "POST", Summary = "Clears all logging events from the plugin.")]
    public class ClearLogsRequest : IReturnVoid { }


    public class LimitedUserStatus
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        public int DailyLimitMinutes { get; set; }
        public int WeeklyLimitHours { get; set; }
        public int MonthlyLimitHours { get; set; }
        public int YearlyLimitHours { get; set; }

        public double SecondsWatchedDaily { get; set; }
        public double SecondsWatchedWeekly { get; set; }
        public double SecondsWatchedMonthly { get; set; }
        public double SecondsWatchedYearly { get; set; }
    }

    public class WatchTimeService : IService
    {
        public void Post(ExtendTimeRequest request)
        {
            if (string.IsNullOrEmpty(request.UserId) || request.Minutes <= 0)
            {
                return;
            }
            WatchTimeManager.ExtendTimeForUser(request.UserId, request.Minutes);
        }

        public object Get(GetLimitedUsersStatusRequest request)
        {
            return WatchTimeManager.GetLimitedUsersStatus();
        }

        public void Post(ToggleUserLimitRequest request)
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return;
            var config = plugin.Configuration;
            if (config == null || string.IsNullOrEmpty(request.UserId)) return;

            var user = config.LimitedUsers.FirstOrDefault(u => u.UserId == request.UserId);
            if (user != null)
            {
                user.IsEnabled = !user.IsEnabled;
                plugin.UpdateConfiguration(config);
            }
        }

        public void Post(ResetUserTimeRequest request)
        {
            if (!string.IsNullOrEmpty(request.UserId))
            {
                WatchTimeManager.ResetWatchTimeForUser(request.UserId);
            }
        }

        public void Post(ResetAllUsersTimeRequest request)
        {
            WatchTimeManager.ResetAllWatchTimes();
        }

        public object Get(GetLogEventsRequest request)
        {
            return LogManager.GetLogEntries();
        }

        public void Post(ClearLogsRequest request)
        {
            LogManager.ClearLogs();
        }
    }
}