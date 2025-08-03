﻿﻿using MediaBrowser.Model.Services;
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

    [Route(ApiRoutes.EditUserLimit, "POST", Summary = "Edits the watch time limit for a specific user.")]
    public class EditUserLimitRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
        public int WatchTimeLimitMinutes { get; set; }
        public ResetIntervalType WatchTimeResetType { get; set; }
        public int WatchTimeResetIntervalMinutes { get; set; }
        public int WatchTimeResetTimeOfDayHours { get; set; }
        public DayOfWeek WatchTimeResetDayOfWeek { get; set; }
        public bool EnableTimeWindow { get; set; }
        public int WatchWindowStartHour { get; set; }
        public int WatchWindowEndHour { get; set; }
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
        public int WatchTimeLimitMinutes { get; set; }
        public double SecondsWatched { get; set; }
        public double SecondsRemaining { get; set; }
        public bool IsLimited { get; set; }
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

        public void Post(EditUserLimitRequest request)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(request.UserId) || request.WatchTimeLimitMinutes <= 0) return;

            var config = plugin.Configuration;
            var user = config.LimitedUsers.FirstOrDefault(u => u.UserId == request.UserId);
            if (user != null)
            {
                user.WatchTimeLimitMinutes = request.WatchTimeLimitMinutes;
                user.WatchTimeResetType = request.WatchTimeResetType;
                user.WatchTimeResetIntervalMinutes = request.WatchTimeResetIntervalMinutes;
                user.WatchTimeResetTimeOfDayHours = request.WatchTimeResetTimeOfDayHours;
                user.WatchTimeResetDayOfWeek = request.WatchTimeResetDayOfWeek;
                user.EnableTimeWindow = request.EnableTimeWindow;
                user.WatchWindowStartHour = request.WatchWindowStartHour;
                user.WatchWindowEndHour = request.WatchWindowEndHour;
                plugin.UpdateConfiguration(config);

                WatchTimeManager.CalculateAllNextResetTimes();
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