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

        public double ResetTimeOfDayHours { get; set; } = 3;
        public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Sunday;
        public int MonthlyResetDay { get; set; } = 1;
        public int YearlyResetMonth { get; set; } = 1;
        public int YearlyResetDay { get; set; } = 1;

        public bool EnableThresholdNotifications { get; set; } = false;
        public string NotificationThresholds { get; set; } = "80,95";

        public bool EnableTimeWindow { get; set; } = false;
        public double WatchWindowStartHour { get; set; } = 0;
        public double WatchWindowEndHour { get; set; } = 23.5;
        public List<int> AllowedDays { get; set; } = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
    }

    public class UserWatchData
    {
        public string UserId { get; set; } = string.Empty;

        public long WatchedTimeTicksDaily { get; set; }
        public long TimeCreditTicksDaily { get; set; }
        public long WatchedTimeTicksWeekly { get; set; }
        public long TimeCreditTicksWeekly { get; set; }
        public long WatchedTimeTicksMonthly { get; set; }
        public long TimeCreditTicksMonthly { get; set; }
        public long WatchedTimeTicksYearly { get; set; }
        public long TimeCreditTicksYearly { get; set; }

        public DateTime LastDailyReset { get; set; }
        public DateTime LastWeeklyReset { get; set; }
        public DateTime LastMonthlyReset { get; set; }
        public DateTime LastYearlyReset { get; set; }

        public DateTime TimeOutUntil { get; set; }

        // Legacy properties for migration
        public long WatchedTimeTicks { get; set; }
        public DateTime LastResetTime { get; set; }
    }


    public class WatchTimePersistenceData
    {
        public List<UserWatchData> UserWatchTimes { get; set; } = new List<UserWatchData>();
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ConfigurationVersion { get; set; } = Guid.NewGuid().ToString();
        public bool EnableTranscodeWarning { get; set; } = true;
        public bool NotifyOnContainerChange { get; set; } = false;
        public string MessageText { get; set; } = "This video is being transcoded. Reason: {reason}";
        public string MessageTextClientLimitation { get; set; } = "This video is transcoding because your device doesn't support the format ({reason}). For a better experience, try a different client like Emby Theater or a modern web browser.";
        public string MessageTextBandwidthLimitation { get; set; } = "This video is transcoding due to your current quality settings ({reason}). For a better experience, try choosing a higher quality setting.";
        public int MaxNotifications { get; set; } = 1;
        public bool NotifyOnAudioOnlyTranscode { get; set; } = true;
        public int InitialDelaySeconds { get; set; } = 2;
        public int DelayBetweenMessagesSeconds { get; set; } = 5;

        public bool EnableTranscodeBlocking { get; set; } = false;
        public string BlockedTranscodeFormats { get; set; } = "mkv,ts,avi";

        public bool EnableResolutionBlocking { get; set; } = false;
        public int MaxTranscodingResolution { get; set; } = 2160;
        public string MessageTextResolutionBlocked { get; set; } = "Transcoding video with a resolution of {height}p is not permitted. The maximum allowed resolution is {max}p.";

        public bool NotifyOnDirectPlay { get; set; } = false;
        public string DirectPlayMessageText { get; set; } = "Direct Play active. Enjoy the best possible quality!";

        public bool EnablePlaybackStartNotification { get; set; } = false;
        public string PlaybackStartMessageText { get; set; } = "Enjoy the show!";
        public int PlaybackStartInitialDelaySeconds { get; set; } = 1;
        public int PlaybackStartMaxNotifications { get; set; } = 1;
        public int PlaybackStartDelayBetweenMessagesSeconds { get; set; } = 5;

        public bool EnableConfirmationButtonOnTranscodeWarning { get; set; } = false;
        public bool EnableConfirmationButtonOnTranscodeBlock { get; set; } = false;
        public bool EnableConfirmationButtonOnDirectPlay { get; set; } = false;
        public bool EnableConfirmationButtonOnPlaybackStart { get; set; } = false;
        public bool EnableConfirmationButtonOnResolutionBlock { get; set; } = false;
        public bool EnableConfirmationButtonOnWatchTimeLimit { get; set; } = false;
        public List<string> ExcludedUserIds { get; set; } = new List<string>();
        public List<string> ExcludedClients { get; set; } = new List<string>();
        public List<string> ExcludedLibraryIds { get; set; } = new List<string>();

        public bool EnableWatchTimeLimiter { get; set; } = false;
        public bool EnableWatchLimitNotifications { get; set; } = false;
        public List<LimitedUser> LimitedUsers { get; set; } = new List<LimitedUser>();
        public string WatchTimeLimitMessageText { get; set; } = "You have reached your watch time limit. Playback is now disabled until the timer resets.";
        public string TimeWindowBlockedMessageText { get; set; } = "Playback is not allowed at this time. Please try again during your allowed watch window.";
        public string TimeOutMessageText { get; set; } = "You have been placed in a temporary time-out. Playback is disabled for the next {duration}.";
        public string ApiKey { get; set; } = "";
        public bool EnablePublicApi { get; set; } = false;
        public bool EnableExternalWebServer { get; set; } = false;
        public int ExternalWebServerPort { get; set; } = 9988;
        public string ExternalWebServerPassword { get; set; } = "";


        public PluginConfiguration()
        {
            LimitedUsers = new List<LimitedUser>();
            ExcludedUserIds = new List<string>();
            ExcludedClients = new List<string>();
            ExcludedLibraryIds = new List<string>();
        }
    }
}