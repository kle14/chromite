using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using SecureBrowser.Models;

namespace SecureBrowser.Data
{
    /// <summary>
    /// All user, permission, and URL whitelist operations backed by PostgreSQL.
    /// Every read goes to the DB so admin changes take effect immediately.
    /// </summary>
    public static class PolicyEngine
    {
        // ── Authentication ────────────────────────────────────────────────

        public static UserSession? AuthenticateUser(
            string username, string password, string location)
        {
            var user = GetUser(username);
            if (user == null || !user.IsActive) return null;
            if (user.PasswordHash != Hash(password)) return null;

            var perms = GetUserPermissions(username);
            if (!perms.AllowedLocations.Contains(location) &&
                !perms.AllowedLocations.Contains("*"))
                return null;

            return new UserSession
            {
                Account     = user,
                Permissions = perms,
                Location    = location,
                LoginTime   = DateTime.Now
            };
        }

        // ── User queries ──────────────────────────────────────────────────

        public static UserAccount? GetUser(string username)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, password_hash, display_name, role, department, is_active
                FROM users WHERE LOWER(username) = LOWER(@u)";
            cmd.Parameters.AddWithValue("u", username);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new UserAccount
            {
                Id           = r.GetInt32(0),
                Username     = r.GetString(1),
                PasswordHash = r.GetString(2),
                DisplayName  = r.GetString(3),
                Role         = r.GetString(4),
                Department   = r.GetString(5),
                IsActive     = r.GetBoolean(6)
            };
        }

        public static List<UserAccount> GetAllUsers()
        {
            var list = new List<UserAccount>();
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, password_hash, display_name, role, department, is_active FROM users ORDER BY username";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new UserAccount
                {
                    Id           = r.GetInt32(0),
                    Username     = r.GetString(1),
                    PasswordHash = r.GetString(2),
                    DisplayName  = r.GetString(3),
                    Role         = r.GetString(4),
                    Department   = r.GetString(5),
                    IsActive     = r.GetBoolean(6)
                });
            }
            return list;
        }

        public static void SetUserActive(string username, bool active)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "UPDATE users SET is_active = @a WHERE LOWER(username) = LOWER(@u)";
            cmd.Parameters.AddWithValue("a", active);
            cmd.Parameters.AddWithValue("u", username);
            cmd.ExecuteNonQuery();
        }

        // ── Permission queries (always live from DB) ──────────────────────

        public static UserPermissions GetUserPermissions(string username)
        {
            var perms = new UserPermissions();
            var key   = username.ToLower();

            using var conn = Db.Open();

            // Base permissions
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT allow_clipboard, allow_print, ssl_only
                    FROM permissions WHERE LOWER(username) = LOWER(@u)";
                cmd.Parameters.AddWithValue("u", key);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    perms.AllowClipboard = r.GetBoolean(0);
                    perms.AllowPrint     = r.GetBoolean(1);
                    perms.SSLOnly        = r.GetBoolean(2);
                }
            }

            // URL whitelist
            perms.AllowedUrls = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT url FROM url_whitelist WHERE LOWER(username) = LOWER(@u)";
                cmd.Parameters.AddWithValue("u", key);
                using var r = cmd.ExecuteReader();
                while (r.Read()) perms.AllowedUrls.Add(r.GetString(0));
            }

            // Allowed locations
            perms.AllowedLocations = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT location FROM allowed_locations WHERE LOWER(username) = LOWER(@u)";
                cmd.Parameters.AddWithValue("u", key);
                using var r = cmd.ExecuteReader();
                while (r.Read()) perms.AllowedLocations.Add(r.GetString(0));
            }

            return perms;
        }

        // ── URL whitelist check (live DB query every time) ────────────────

        public static bool IsUrlAllowed(string username, string url)
        {
            // about:blank is required internally (session wipe, block pages); all other
            // about: variants, data: URIs, and edge: URIs are blocked — data: in particular
            // can load arbitrary HTML/JS content entirely outside the whitelist.
            if (url == "about:blank" || string.IsNullOrWhiteSpace(url))
                return true;

            if (url.StartsWith("data:",  StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("edge:",  StringComparison.OrdinalIgnoreCase))
                return false;

            var perms = GetUserPermissions(username);

            if (perms.AllowedUrls.Contains("*")) return true;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

            foreach (var allowed in perms.AllowedUrls)
            {
                if (allowed == "*") return true;

                if (Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri))
                {
                    if (uri.Host.Equals(allowedUri.Host, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (uri.Host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Live check — is clipboard currently allowed for this user?
        /// Called on every clipboard event so admin changes work instantly.
        /// </summary>
        public static bool IsClipboardAllowed(string username)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT allow_clipboard FROM permissions WHERE LOWER(username) = LOWER(@u)";
            cmd.Parameters.AddWithValue("u", username);
            var result = cmd.ExecuteScalar();
            return result is true;
        }

        /// <summary>
        /// Live check — is printing currently allowed for this user?
        /// </summary>
        public static bool IsPrintAllowed(string username)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT allow_print FROM permissions WHERE LOWER(username) = LOWER(@u)";
            cmd.Parameters.AddWithValue("u", username);
            var result = cmd.ExecuteScalar();
            return result is true;
        }

        /// <summary>
        /// Live check — is SSL-only mode on for this user?
        /// </summary>
        public static bool IsSSLOnly(string username)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT ssl_only FROM permissions WHERE LOWER(username) = LOWER(@u)";
            cmd.Parameters.AddWithValue("u", username);
            var result = cmd.ExecuteScalar();
            return result is true;
        }

        public static bool IsLocationAllowed(string username, string location)
        {
            var perms = GetUserPermissions(username);
            return perms.AllowedLocations.Contains("*") ||
                   perms.AllowedLocations.Contains(location);
        }

        // ── Admin mutations ───────────────────────────────────────────────

        public static void UpdatePermissions(string username, bool clipboard, bool print, bool ssl)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO permissions (username, allow_clipboard, allow_print, ssl_only, updated_at)
                VALUES (LOWER(@u), @c, @p, @s, NOW())
                ON CONFLICT (username) DO UPDATE
                    SET allow_clipboard = @c, allow_print = @p, ssl_only = @s, updated_at = NOW()";
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.AddWithValue("c", clipboard);
            cmd.Parameters.AddWithValue("p", print);
            cmd.Parameters.AddWithValue("s", ssl);
            cmd.ExecuteNonQuery();
        }

        public static void SetAllowedLocations(string username, List<string> locations)
        {
            using var conn = Db.Open();
            using var tx   = conn.BeginTransaction();

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM allowed_locations WHERE LOWER(username) = LOWER(@u)";
                del.Parameters.AddWithValue("u", username);
                del.ExecuteNonQuery();
            }

            foreach (var loc in locations)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO allowed_locations (username, location) VALUES (LOWER(@u), @l)";
                ins.Parameters.AddWithValue("u", username);
                ins.Parameters.AddWithValue("l", loc);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static void AddUrlToWhitelist(string username, string url)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO url_whitelist (username, url)
                VALUES (LOWER(@u), @url)
                ON CONFLICT DO NOTHING";
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.AddWithValue("url", url);
            cmd.ExecuteNonQuery();
        }

        public static void RemoveUrlFromWhitelist(string username, string url)
        {
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM url_whitelist WHERE LOWER(username) = LOWER(@u) AND url = @url";
            cmd.Parameters.AddWithValue("u", username);
            cmd.Parameters.AddWithValue("url", url);
            cmd.ExecuteNonQuery();
        }

        public static List<string> GetUrlWhitelist(string username)
        {
            var urls = new List<string>();
            using var conn = Db.Open();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT url FROM url_whitelist WHERE LOWER(username) = LOWER(@u) ORDER BY url";
            cmd.Parameters.AddWithValue("u", username);
            using var r = cmd.ExecuteReader();
            while (r.Read()) urls.Add(r.GetString(0));
            return urls;
        }

        // ── Hashing ───────────────────────────────────────────────────────

        public static string Hash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
