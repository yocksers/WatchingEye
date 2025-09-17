namespace WatchingEye.Api
{
    internal static class ApiRoutes
    {
        public const string ExtendTime = "/WatchingEye/ExtendTime";
        public const string LimitedUsersStatus = "/WatchingEye/LimitedUsersStatus";
        public const string ToggleUserLimit = "/WatchingEye/ToggleUserLimit";
        public const string ResetUserTime = "/WatchingEye/ResetUserTime";
        public const string ResetAllUsersTime = "/WatchingEye/ResetAllUsersTime";
        public const string GetLogEvents = "/WatchingEye/GetLogEvents";
        public const string ClearLogs = "/WatchingEye/ClearLogs";
        public const string TimeOutUser = "/WatchingEye/TimeOutUser";
        public const string GetClientList = "/WatchingEye/GetClientList";
        public const string ClearTimeOut = "/WatchingEye/ClearTimeOut";
    }
}