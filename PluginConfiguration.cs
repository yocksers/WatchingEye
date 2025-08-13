using MediaBrowser.Model.Plugins;
using System;
using System.Collections.Generic;

namespace WatchingEye
{
    public class LimitedUser
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        public bool EnableDailyLimit { get; set; } = true;
        public int DailyLimitMinutes { get; set; } = 120;

        public bool EnableWeeklyLimit { get; set; } = false;
        public int WeeklyLimitHours { get; set; } = 20;

        public bool EnableMonthlyLimit { get; set; } = false;
        public int MonthlyLimitHours { get; set; } = 80;

        public bool EnableYearlyLimit { get; set; } = false;
        public int YearlyLimitHours { get; set; } = 0;

        public double ResetTimeOfDayHours { get; set; } = 3; // e.g., 3.0 for 3:00 AM
        public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Sunday;
        public int MonthlyResetDay { get; set; } = 1;
        public int YearlyResetMonth { get; set; } = 1;
        public int YearlyResetDay { get; set; } = 1;

        public bool EnableThresholdNotifications { get; set; } = false;
        public string NotificationThresholds { get; set; } = "80,95"; // Comma-separated percentages

        public bool EnableTimeWindow { get; set; } = false;
        public double WatchWindowStartHour { get; set; } = 0;
        public double WatchWindowEndHour { get; set; } = 23.5;
    }

    public class UserWatchData
    {
        public string UserId { get; set; } = string.Empty;

        public long WatchedTimeTicksDaily { get; set; }
        public long WatchedTimeTicksWeekly { get; set; }
        public long WatchedTimeTicksMonthly { get; set; }
        public long WatchedTimeTicksYearly { get; set; }

        public DateTime LastDailyReset { get; set; }
        public DateTime LastWeeklyReset { get; set; }
        public DateTime LastMonthlyReset { get; set; }
        public DateTime LastYearlyReset { get; set; }

        public long WatchedTimeTicks { get; set; }
        public DateTime LastResetTime { get; set; }
    }


    public class WatchTimePersistenceData
    {
        public List<UserWatchData> UserWatchTimes { get; set; } = new List<UserWatchData>();
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableTranscodeWarning { get; set; } = true;
        public string MessageText { get; set; } = "This video is being transcoded. Reason: {reason}";
        public int MaxNotifications { get; set; } = 1;
        public bool NotifyOnAudioOnlyTranscode { get; set; } = true;
        public int InitialDelaySeconds { get; set; } = 2;
        public int DelayBetweenMessagesSeconds { get; set; } = 5;

        public bool NotifyOnDirectPlay { get; set; } = false;
        public string DirectPlayMessageText { get; set; } = "Direct Play active. Enjoy the best possible quality!";

        public bool EnablePlaybackStartNotification { get; set; } = false;
        public string PlaybackStartMessageText { get; set; } = "Enjoy the show!";
        public int PlaybackStartInitialDelaySeconds { get; set; } = 1;
        public int PlaybackStartMaxNotifications { get; set; } = 1;
        public int PlaybackStartDelayBetweenMessagesSeconds { get; set; } = 5;

        public bool EnableConfirmationButton { get; set; } = false;
        public string ExcludedUserNames { get; set; } = string.Empty;
        public string ExcludedClients { get; set; } = string.Empty;

        public bool EnableWatchTimeLimiter { get; set; } = false;
        public List<LimitedUser> LimitedUsers { get; set; } = new List<LimitedUser>();
        public string WatchTimeLimitMessageText { get; set; } = "You have reached your watch time limit. Playback is now disabled until the timer resets.";
        public string TimeWindowBlockedMessageText { get; set; } = "Playback is not allowed at this time. Please try again during your allowed watch window.";

        public PluginConfiguration()
        {
            LimitedUsers = new List<LimitedUser>();
        }
    }
}