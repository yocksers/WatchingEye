using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using WatchingEye.Api;
using MediaBrowser.Controller.Library;

namespace WatchingEye
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage, IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IApplicationPaths _appPaths;
        private readonly INotificationManager _notificationManager;
        private readonly ILibraryManager _libraryManager;
        public static Plugin? Instance { get; private set; }
        private ExternalWebServer? _externalWebServer;
        public static string ExternalWebServerStatus { get; private set; } = "Not Enabled";

        public override string Name => "Watching Eye";
        public override string Description => "A plugin to monitor and limit user watch time, with optional transcode notifications.";
        public override Guid Id => Guid.Parse("e8c3b1b3-4f56-4f38-a28a-2e6c5a043007");

        public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, ISessionManager sessionManager, ILogManager logManager, IJsonSerializer jsonSerializer, INotificationManager notificationManager, ILibraryManager libraryManager)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
            _appPaths = appPaths;
            _notificationManager = notificationManager;
            _libraryManager = libraryManager;
        }

        public static bool UpdateUserLimits(string userId, int dailyMinutes, int weeklyHours, int monthlyHours)
        {
            if (Instance == null) return false;

            var userToUpdate = Instance.Configuration.LimitedUsers.FirstOrDefault(u => u.UserId == userId);
            if (userToUpdate == null) return false;

            userToUpdate.DailyLimitMinutes = dailyMinutes;
            userToUpdate.WeeklyLimitHours = weeklyHours;
            userToUpdate.MonthlyLimitHours = monthlyHours;

            Instance.UpdateConfiguration(Instance.Configuration);
            Instance._logger.Info($"[ExternalWebServer] Updated limits for user {userToUpdate.Username}.");
            return true;
        }

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            if (configuration is PluginConfiguration newConfig)
            {
                newConfig.ConfigurationVersion = Guid.NewGuid().ToString();
            }
            base.UpdateConfiguration(configuration);
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "WatchingEyeConfiguration",
                    EmbeddedResourcePath = GetType().Namespace + ".WatchingEyeConfiguration.html",
                },
                new PluginPageInfo
                {
                    Name = "WatchingEyeConfigurationjs",
                    EmbeddedResourcePath = GetType().Namespace + ".WatchingEyeConfiguration.js"
                }
            };
        }

        public void Run()
        {
            InAppNotificationService.Start(_logger, _sessionManager);
            TranscodeMonitor.Start(_sessionManager, _logger, _libraryManager);
            WatchTimeManager.Start(_sessionManager, _logger, _appPaths, _jsonSerializer);
            LogManager.Start(_logger, _jsonSerializer, _appPaths);
            ServerNotificationService.Start(_logger, _notificationManager);

            if (Configuration.EnableExternalWebServer)
            {
                _externalWebServer = new ExternalWebServer(_logger, _jsonSerializer, Configuration.ExternalWebServerPort, Configuration.ExternalWebServerPassword);
                ExternalWebServerStatus = _externalWebServer.Start();
            }
            else
            {
                ExternalWebServerStatus = "Not Enabled";
            }
        }

        public void Dispose()
        {
            TranscodeMonitor.Stop();
            WatchTimeManager.Stop();
            LogManager.Stop();
            ServerNotificationService.Stop();
            InAppNotificationService.Stop();
            _externalWebServer?.Stop();
        }

        public Stream GetThumbImage()
        {
            var assembly = typeof(Plugin).GetTypeInfo().Assembly;
            var resourceName = typeof(Plugin).Namespace + ".Images.logo.jpg";
            return assembly.GetManifestResourceStream(resourceName) ?? Stream.Null;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Jpg;
    }
}