using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WatchingEye
{
    public static class TranscodeReasonParser
    {
        private static readonly Dictionary<string, string> _reasonMap = new Dictionary<string, string>
        {
            // Container
            { "ContainerNotSupported", "the container is not compatible" },

            // Video
            { "VideoCodecNotSupported", "the video format is not supported" },
            { "VideoResolutionNotSupported", "the video resolution is too high" },
            { "VideoBitrateNotSupported", "the video bitrate is too high" },
            { "VideoLevelNotSupported", "the video profile level is not supported" },
            { "VideoProfileNotSupported", "the video profile is not supported" },
            { "AnamorphicVideoNotSupported", "anamorphic video is not supported" },
            { "InterlacedVideoNotSupported", "interlaced video is not supported" },
            { "SecondaryVideoNotSupported", "secondary video tracks are not supported" },
            
            // Audio
            { "AudioCodecNotSupported", "the audio format is not supported" },
            { "AudioChannelsNotSupported", "the number of audio channels is not supported" },
            { "AudioBitrateNotSupported", "the audio bitrate is too high" },
            { "AudioSampleRateNotSupported", "the audio sample rate is not supported" },
            { "AudioProfileNotSupported", "the audio profile is not supported" },
            
            // Subtitle
            { "SubtitleCodecNotSupported", "the subtitle format is not supported" },
            { "SubtitleInBadFormat", "the subtitles are in a bad format" },
            
            // Other
            { "DirectPlayError", "a direct play error occurred" },
            { "VideoRangeNotSupported", "the video's color range (HDR/SDR) is not supported" },
            { "BitrateTooHighInMatrix", "the bitrate is too high for the quality settings" },
            { "RefFramesNotSupported", "the number of reference frames is not supported" }
        };

        public static string Parse(string rawReasons)
        {
            if (string.IsNullOrWhiteSpace(rawReasons))
            {
                return "an unknown reason";
            }

            var reasons = rawReasons.Split(',').Select(r => r.Trim());
            var friendlyReasons = new List<string>();

            foreach (var reason in reasons)
            {
                if (_reasonMap.TryGetValue(reason, out var friendlyReason))
                {
                    friendlyReasons.Add(friendlyReason);
                }
                else
                {
                    friendlyReasons.Add($"'{reason}'");
                }
            }

            if (friendlyReasons.Count == 0)
            {
                return "an unknown reason";
            }

            if (friendlyReasons.Count == 1)
            {
                return friendlyReasons[0];
            }

            var sb = new StringBuilder();
            for (int i = 0; i < friendlyReasons.Count; i++)
            {
                sb.Append(friendlyReasons[i]);
                if (i < friendlyReasons.Count - 2)
                {
                    sb.Append(", ");
                }
                else if (i == friendlyReasons.Count - 2)
                {
                    sb.Append(" and ");
                }
            }

            return sb.ToString();
        }
    }
}