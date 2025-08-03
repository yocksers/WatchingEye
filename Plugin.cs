﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using WatchingEye.Api;

namespace WatchingEye
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage, IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IApplicationPaths _appPaths;
        public static Plugin? Instance { get; private set; }

        public override string Name => "Watching Eye";
        public override string Description => "A plugin to monitor and limit user watch time, with optional transcode notifications.";
        public override Guid Id => Guid.Parse("e8c3b1b3-4f56-4f38-a28a-2e6c5a043007");

        public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, ISessionManager sessionManager, ILogManager logManager, IJsonSerializer jsonSerializer)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
            _appPaths = appPaths;
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
            TranscodeMonitor.Start(_sessionManager, _logger);
            WatchTimeManager.Start(_sessionManager, _logger, _appPaths, _jsonSerializer);
            LogManager.Start(_logger, _jsonSerializer, _appPaths);
        }

        public void Dispose()
        {
            TranscodeMonitor.Stop();
            WatchTimeManager.Stop();
            LogManager.Stop();
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