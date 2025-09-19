using MediaBrowser.Model.Services;
using WatchingEye.Services;
using System.Collections.Generic;

namespace WatchingEye.Api
{
    [Route(ApiRoutes.PublicLimitedUsersStatus, "GET", Summary = "Gets the status of all limited users.")]
    public class PublicGetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>> { }

    [Route(ApiRoutes.PublicExtendTime, "POST", Summary = "Adds time credit for a specified user.")]
    public class PublicExtendTimeRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
        public int Minutes { get; set; }
    }

    [Route(ApiRoutes.PublicTimeOutUser, "POST", Summary = "Temporarily blocks a user from playback.")]
    public class PublicTimeOutUserRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
        public int Minutes { get; set; }
    }

    [Route(ApiRoutes.PublicClearTimeOut, "POST", Summary = "Clears an active time-out for a user.")]
    public class PublicClearTimeOutRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
    }

    [Route(ApiRoutes.PublicResetUserTime, "POST", Summary = "Resets the tracked watch time for a single user.")]
    public class PublicResetUserTimeRequest : IReturnVoid
    {
        public string UserId { get; set; } = string.Empty;
    }
}