using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WatchingEye
{
    public class LogEntry
    {
        public string EventType { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class LogPersistenceData
    {
        public List<LogEntry> Entries { get; set; } = new List<LogEntry>();
    }

    public static class LogManager
    {
        private static ILogger? _logger;
        private static IJsonSerializer? _jsonSerializer;
        private static string? _logDataPath;

        private static readonly object _lock = new object();
        private static List<LogEntry> _entries = new List<LogEntry>();
        private static bool _isDirty = false;

        private const int MaxEntries = 200;

        public static void Start(ILogger logger, IJsonSerializer jsonSerializer, IApplicationPaths appPaths)
        {
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _logDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.Logs.json");
            Load();
        }

        public static void Stop()
        {
            Save();
        }

        public static void LogLimitReached(string userId, string username, string clientName)
        {
            AddEntry(new LogEntry
            {
                EventType = "LimitReached",
                Username = username,
                Message = $"Watch time limit reached.",
                ClientName = clientName,
                Timestamp = DateTime.UtcNow
            });
        }

        public static void LogTranscode(SessionInfo session, string reasons)
        {
            AddEntry(new LogEntry
            {
                EventType = "Transcode",
                Username = session.UserName ?? "Unknown",
                Message = $"Transcode started: {reasons}",
                ClientName = session.Client ?? "Unknown",
                Timestamp = DateTime.UtcNow
            });
        }

        public static List<LogEntry> GetLogEntries()
        {
            lock (_lock)
            {
                return _entries.AsEnumerable().Reverse().ToList();
            }
        }

        public static List<string> GetDistinctClientNames()
        {
            lock (_lock)
            {
                return _entries.Select(e => e.ClientName).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
            }
        }

        public static void ClearLogs()
        {
            lock (_lock)
            {
                _entries.Clear();
                _isDirty = true;
            }
            Save();
        }

        private static void AddEntry(LogEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
                _isDirty = true;
            }
            Save();
        }

        private static void Load()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_logDataPath) || !File.Exists(_logDataPath))
                return;

            try
            {
                var json = File.ReadAllText(_logDataPath);
                var data = _jsonSerializer.DeserializeFromString<LogPersistenceData>(json);
                if (data?.Entries != null)
                {
                    lock (_lock)
                    {
                        _entries = data.Entries;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[LogManager] Error loading log data.", ex);
            }
        }

        private static void Save()
        {
            if (_jsonSerializer == null || string.IsNullOrEmpty(_logDataPath)) return;

            lock (_lock)
            {
                if (!_isDirty) return;

                try
                {
                    var data = new LogPersistenceData { Entries = new List<LogEntry>(_entries) };
                    var json = _jsonSerializer.SerializeToString(data);
                    var tempPath = _logDataPath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    if (File.Exists(_logDataPath))
                        File.Replace(tempPath, _logDataPath, null);
                    else
                        File.Move(tempPath, _logDataPath);

                    _isDirty = false;
                }
                catch (Exception ex)
                {
                    _logger?.ErrorException("[LogManager] Error saving log data.", ex);
                }
            }
        }
    }
}
