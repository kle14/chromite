using System;
using System.Collections.Generic;
using SecureBrowser.Models;

namespace SecureBrowser.Data
{
    /// <summary>
    /// Writes and reads audit events from PostgreSQL.
    /// Every event is written immediately — no batching.
    /// </summary>
    public static class AuditLogger
    {
        // ── Write ─────────────────────────────────────────────────────────

        public static void Log(string username, string eventType,
                               string details, string severity = "Info",
                               string location = "")
        {
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO audit_log (timestamp, username, event_type, details, severity, location)
                    VALUES (NOW(), @u, @t, @d, @s, @l)";
                cmd.Parameters.AddWithValue("u", username);
                cmd.Parameters.AddWithValue("t", eventType);
                cmd.Parameters.AddWithValue("d", details);
                cmd.Parameters.AddWithValue("s", severity);
                cmd.Parameters.AddWithValue("l", location);
                cmd.ExecuteNonQuery();
            }
            catch { /* never crash the app because logging failed */ }
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

        public static void LogPrintBlocked(string username)
            => Log(username, "PRINT_BLOCKED",
                   "Print attempt intercepted",
                   "Warning");

        public static void LogScreenshotAttempt(string username)
            => Log(username, "SCREENSHOT",
                   "Screenshot attempt detected (window blacked out)",
                   "Warning");

        public static void LogSaveBlocked(string username)
            => Log(username, "SAVE_BLOCKED",
                   "Save-page shortcut intercepted (Ctrl+S / Ctrl+Shift+S)",
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

        public static List<AuditEvent> GetRecentEvents(int count)
        {
            var list = new List<AuditEvent>();
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, timestamp, username, event_type, details, severity, location
                    FROM audit_log ORDER BY timestamp DESC LIMIT @n";
                cmd.Parameters.AddWithValue("n", count);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadEvent(r));
            }
            catch { }
            return list;
        }

        public static List<AuditEvent> GetAllEvents()
        {
            var list = new List<AuditEvent>();
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, timestamp, username, event_type, details, severity, location
                    FROM audit_log ORDER BY timestamp DESC";
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadEvent(r));
            }
            catch { }
            return list;
        }

        public static List<AuditEvent> GetUserEvents(string username)
        {
            var list = new List<AuditEvent>();
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, timestamp, username, event_type, details, severity, location
                    FROM audit_log WHERE LOWER(username) = LOWER(@u)
                    ORDER BY timestamp DESC";
                cmd.Parameters.AddWithValue("u", username);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadEvent(r));
            }
            catch { }
            return list;
        }

        public static List<AuditEvent> GetEventsByType(string eventType)
        {
            var list = new List<AuditEvent>();
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, timestamp, username, event_type, details, severity, location
                    FROM audit_log WHERE LOWER(event_type) = LOWER(@t)
                    ORDER BY timestamp DESC";
                cmd.Parameters.AddWithValue("t", eventType);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadEvent(r));
            }
            catch { }
            return list;
        }

        public static List<AuditEvent> GetFilteredEvents(string? username, string? eventType)
        {
            var list = new List<AuditEvent>();
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                var sql = @"
                    SELECT id, timestamp, username, event_type, details, severity, location
                    FROM audit_log WHERE 1=1";

                if (!string.IsNullOrEmpty(username) && username != "All")
                {
                    sql += " AND LOWER(username) = LOWER(@u)";
                    cmd.Parameters.AddWithValue("u", username);
                }
                if (!string.IsNullOrEmpty(eventType) && eventType != "All")
                {
                    sql += " AND LOWER(event_type) = LOWER(@t)";
                    cmd.Parameters.AddWithValue("t", eventType);
                }

                sql += " ORDER BY timestamp DESC LIMIT 500";
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadEvent(r));
            }
            catch { }
            return list;
        }

        // ── Stats ─────────────────────────────────────────────────────────

        public static int CountTodayByType(string eventType)
        {
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM audit_log
                    WHERE event_type = @t AND timestamp::date = CURRENT_DATE";
                cmd.Parameters.AddWithValue("t", eventType);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        public static int CountWarningsToday()
        {
            try
            {
                using var conn = Db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM audit_log
                    WHERE (severity = 'Warning' OR severity = 'Critical')
                      AND timestamp::date = CURRENT_DATE";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static AuditEvent ReadEvent(Npgsql.NpgsqlDataReader r)
        {
            return new AuditEvent
            {
                Id        = r.GetInt32(0),
                Timestamp = r.GetDateTime(1),
                Username  = r.GetString(2),
                EventType = r.GetString(3),
                Details   = r.GetString(4),
                Severity  = r.GetString(5),
                Location  = r.GetString(6)
            };
        }
    }
}
