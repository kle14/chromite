using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SecureBrowser
{
    /// <summary>
    /// Writes and reads audit events from a JSON-lines file.
    /// In a real enterprise this would ship events to Elasticsearch.
    /// For the demo every event is a JSON object on its own line.
    /// </summary>
    public static class AuditLogger
    {
        private static readonly string LogFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "audit.log");

        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = false
        };

        // ── Write ─────────────────────────────────────────────────────────

        public static void Log(string username, string eventType,
                               string details, string severity = "Info",
                               string location = "")
        {
            var ev = new AuditEvent
            {
                Timestamp = DateTime.Now,
                Username  = username,
                EventType = eventType,
                Details   = details,
                Severity  = severity,
                Location  = location
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile,
                    JsonSerializer.Serialize(ev, Opts) + Environment.NewLine);
            }
            catch { /* never crash the app just because logging failed */ }
        }

        // ── Convenience helpers ───────────────────────────────────────────

        public static void LogLogin(string username, string location)
            => Log(username, "LOGIN",
                   $"User '{username}' authenticated from {location}",
                   "Info", location);

        public static void LogLogout(string username, string location)
            => Log(username, "LOGOUT",
                   $"Session ended for '{username}'",
                   "Info", location);

        public static void LogNavBlocked(string username, string url, string reason)
            => Log(username, "NAV_BLOCKED",
                   $"Blocked navigation to: {url} — {reason}",
                   "Warning");

        public static void LogCopyBlocked(string username)
            => Log(username, "COPY_BLOCKED",
                   "Clipboard copy attempt intercepted",
                   "Warning");

        public static void LogScreenshotAttempt(string username)
            => Log(username, "SCREENSHOT",
                   "Screenshot attempt detected (window blacked out)",
                   "Warning");

        public static void LogConfigChange(string adminUser,
                                           string targetUser, string change)
            => Log(adminUser, "CONFIG_CHANGE",
                   $"Admin '{adminUser}' changed policy for '{targetUser}': {change}",
                   "Critical");

        public static void LogLocationDenied(string username, string location)
            => Log(username, "LOCATION_DENIED",
                   $"Login denied — location '{location}' not permitted",
                   "Critical", location);

        // ── Read ──────────────────────────────────────────────────────────

        public static List<AuditEvent> GetAllEvents()
        {
            var events = ReadAll();
            events.Reverse();
            return events;
        }

        public static List<AuditEvent> GetRecentEvents(int count)
            => GetAllEvents().Take(count).ToList();

        public static List<AuditEvent> GetUserEvents(string username)
            => GetAllEvents()
               .Where(e => e.Username.Equals(username,
                   StringComparison.OrdinalIgnoreCase))
               .ToList();

        public static List<AuditEvent> GetEventsByType(string eventType)
            => GetAllEvents()
               .Where(e => e.EventType.Equals(eventType,
                   StringComparison.OrdinalIgnoreCase))
               .ToList();

        private static List<AuditEvent> ReadAll()
        {
            var result = new List<AuditEvent>();
            if (!File.Exists(LogFile)) return result;

            try
            {
                foreach (var line in File.ReadAllLines(LogFile))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var ev = JsonSerializer.Deserialize<AuditEvent>(line);
                    if (ev != null) result.Add(ev);
                }
            }
            catch { /* return whatever we got */ }

            return result;
        }

        // ── Stats helpers for dashboard ────────────────────────────────────

        public static int CountTodayByType(string eventType)
        {
            var today = DateTime.Today;
            return ReadAll()
                .Count(e => e.EventType == eventType &&
                            e.Timestamp.Date == today);
        }

        public static int CountWarningsToday()
        {
            var today = DateTime.Today;
            return ReadAll()
                .Count(e => (e.Severity == "Warning" || e.Severity == "Critical") &&
                            e.Timestamp.Date == today);
        }
    }
}
