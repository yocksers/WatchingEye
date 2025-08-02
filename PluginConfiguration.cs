using MediaBrowser.Model.Plugins;
using System;
using System.Collections.Generic;

namespace WatchingEye
{
    public class LimitedUser
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int WatchTimeLimitMinutes { get; set; } = 120;
        public bool IsEnabled { get; set; } = true;
    }

    public class UserWatchData
    {
        public string UserId { get; set; } = string.Empty;
        public long WatchedTimeTicks { get; set; }
    }

    public class WatchTimePersistenceData
    {
        public List<UserWatchData> UserWatchTimes { get; set; } = new List<UserWatchData>();
        public DateTime LastResetTime { get; set; }
    }

    public enum ResetIntervalType
    {
        Minutes,
        Daily,
        Weekly
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

        public ResetIntervalType WatchTimeResetType { get; set; } = ResetIntervalType.Minutes;
        public int WatchTimeResetIntervalMinutes { get; set; } = 1440;
        public int WatchTimeResetTimeOfDayHours { get; set; } = 3;
        public DayOfWeek WatchTimeResetDayOfWeek { get; set; } = DayOfWeek.Sunday;


        public PluginConfiguration()
        {
            LimitedUsers = new List<LimitedUser>();
        }
    }
}
