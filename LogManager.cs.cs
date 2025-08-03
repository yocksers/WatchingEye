using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WatchingEye
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public static class LogManager
    {
        private static ILogger? _logger;
        private static IJsonSerializer? _jsonSerializer;
        private static string? _logFilePath;
        private static bool _isRunning = false;

        private static readonly ConcurrentQueue<LogEntry> _logEntries = new();
        private const int MaxLogEntries = 200;

        public static void Start(ILogger logger, IJsonSerializer jsonSerializer, IApplicationPaths appPaths)
        {
            if (_isRunning) return;

            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _logFilePath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.Logging.json");

            LoadLogs();
            _isRunning = true;
            _logger.Info("[LogManager] Started.");
        }

        public static void Stop()
        {
            SaveLogs();
            _isRunning = false;
            _logger?.Info("[LogManager] Stopped.");
        }

        public static void LogTranscode(SessionInfo session, string reason)
        {
            var message = $"Transcode started for '{session.NowPlayingItem?.Name ?? "Unknown"}' on client '{session.Client}'. Reason: {reason}.";
            AddLogEntry("Transcode", session, message);
        }

        public static void LogLimitReached(string userId, string username)
        {
            AddLogEntry("Limit Reached", new SessionInfo { UserId = userId, UserName = username, Client = "Unknown" }, "User reached their watch time limit.");
        }

        private static void AddLogEntry(string eventType, SessionInfo session, string message)
        {
            if (!_isRunning) return;

            var entry = new LogEntry
            {
                EventType = eventType,
                UserId = session.UserId,
                Username = session.UserName,
                ClientName = session.Client,
                Message = message
            };
            _logEntries.Enqueue(entry);

            while (_logEntries.Count > MaxLogEntries)
            {
                _logEntries.TryDequeue(out _);
            }
            SaveLogs();
        }

        public static IEnumerable<LogEntry> GetLogEntries()
        {
            return _logEntries.OrderByDescending(e => e.Timestamp);
        }

        public static void ClearLogs()
        {
            _logEntries.Clear();
            SaveLogs();
            _logger?.Info("[LogManager] All log entries have been cleared.");
        }

        private static void LoadLogs()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath)) return;

            try
            {
                var json = File.ReadAllText(_logFilePath);
                var logs = _jsonSerializer.DeserializeFromString<List<LogEntry>>(json);
                if (logs != null)
                {
                    foreach (var log in logs)
                    {
                        _logEntries.Enqueue(log);
                    }
                }
                _logger?.Info($"[LogManager] Loaded {_logEntries.Count} log entries from file.");
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[LogManager] Error loading logging data.", ex);
            }
        }

        private static void SaveLogs()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_logFilePath)) return;

            try
            {
                var json = _jsonSerializer.SerializeToString(_logEntries.ToList());
                File.WriteAllText(_logFilePath, json);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[LogManager] Error saving logging data.", ex);
            }
        }
    }
}