using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureBrowser
{
    /// <summary>
    /// Manages users, permissions, and URL whitelists from local JSON files.
    /// In a real enterprise this would be an API call to a policy service.
    /// For the demo it reads/writes files in the data/ directory.
    /// </summary>
    public static class PolicyEngine
    {
        private static readonly string DataDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        private static readonly string UsersFile  = Path.Combine(DataDir, "users.json");
        private static readonly string PolicyFile = Path.Combine(DataDir, "policy.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // ── Initialise default data on first run ─────────────────────────

        public static void EnsureDefaultData()
        {
            Directory.CreateDirectory(DataDir);

            if (!File.Exists(UsersFile))
                CreateDefaultUsers();

            if (!File.Exists(PolicyFile))
                CreateDefaultPolicy();
        }

        private static void CreateDefaultUsers()
        {
            var users = new List<UserAccount>
            {
                new()
                {
                    Username     = "admin",
                    PasswordHash = Hash("Admin123!"),
                    DisplayName  = "System Administrator",
                    Role         = "Admin",
                    Department   = "IT Security",
                    IsActive     = true
                },
                new()
                {
                    Username     = "alice",
                    PasswordHash = Hash("Alice123!"),
                    DisplayName  = "Alice Johnson",
                    Role         = "User",
                    Department   = "Finance",
                    IsActive     = true
                },
                new()
                {
                    Username     = "bob",
                    PasswordHash = Hash("Bob123!"),
                    DisplayName  = "Bob Smith",
                    Role         = "User",
                    Department   = "Operations",
                    IsActive     = true
                }
            };

            File.WriteAllText(UsersFile,
                JsonSerializer.Serialize(users, JsonOpts));
        }

        private static void CreateDefaultPolicy()
        {
            var policy = new PolicyData
            {
                UserPolicies = new()
                {
                    ["admin"] = new UserPermissions
                    {
                        AllowClipboard   = true,
                        AllowPrint       = true,
                        SSLOnly          = false,
                        AllowedUrls      = new() { "*" },
                        AllowedLocations = new() { "Office", "Remote", "Branch" }
                    },
                    ["alice"] = new UserPermissions
                    {
                        AllowClipboard   = false,
                        AllowPrint       = false,
                        SSLOnly          = true,
                        AllowedUrls      = new()
                        {
                            "https://www.google.com",
                            "https://google.com",
                            "https://github.com",
                            "https://www.github.com"
                        },
                        AllowedLocations = new() { "Office" }
                    },
                    ["bob"] = new UserPermissions
                    {
                        AllowClipboard   = true,
                        AllowPrint       = false,
                        SSLOnly          = true,
                        AllowedUrls      = new()
                        {
                            "https://www.google.com",
                            "https://google.com",
                            "https://www.microsoft.com",
                            "https://microsoft.com",
                            "https://stackoverflow.com"
                        },
                        AllowedLocations = new() { "Office", "Remote" }
                    }
                }
            };

            File.WriteAllText(PolicyFile,
                JsonSerializer.Serialize(policy, JsonOpts));
        }

        // ── Authentication ────────────────────────────────────────────────

        public static UserSession? AuthenticateUser(
            string username, string password, string location)
        {
            var users = LoadUsers();
            var user  = users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                u.IsActive);

            if (user == null) return null;
            if (user.PasswordHash != Hash(password)) return null;

            var permissions = GetUserPermissions(username);

            // Location check — is the user allowed to login from this location?
            if (!permissions.AllowedLocations.Contains(location) &&
                !permissions.AllowedLocations.Contains("*"))
            {
                return null; // Location not permitted
            }

            return new UserSession
            {
                Account     = user,
                Permissions = permissions,
                Location    = location,
                LoginTime   = DateTime.Now
            };
        }

        // ── Permission queries ────────────────────────────────────────────

        public static UserPermissions GetUserPermissions(string username)
        {
            var policy = LoadPolicy();
            var key    = username.ToLower();
            return policy.UserPolicies.TryGetValue(key, out var p)
                ? p
                : new UserPermissions(); // default = locked down
        }

        /// <summary>
        /// Returns true if the given URL is on the user's whitelist.
        /// Supports "*" (allow all) and domain-level matching.
        /// </summary>
        public static bool IsUrlAllowed(string username, string url)
        {
            // Internal WebView2 pages always allowed
            if (url.StartsWith("about:")    ||
                url.StartsWith("data:")     ||
                url.StartsWith("edge://")   ||
                string.IsNullOrWhiteSpace(url))
                return true;

            var permissions = GetUserPermissions(username);

            // Wildcard — admin bypass
            if (permissions.AllowedUrls.Contains("*")) return true;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            foreach (var allowed in permissions.AllowedUrls)
            {
                if (allowed == "*") return true;

                // Try exact domain match
                if (Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri))
                {
                    if (uri.Host.Equals(allowedUri.Host,
                        StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    // Plain domain like "google.com"
                    if (uri.Host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static bool IsLocationAllowed(string username, string location)
        {
            var permissions = GetUserPermissions(username);
            return permissions.AllowedLocations.Contains("*") ||
                   permissions.AllowedLocations.Contains(location);
        }

        // ── Admin mutations ───────────────────────────────────────────────

        public static void UpdateUserPermissions(string username, UserPermissions perms)
        {
            var policy = LoadPolicy();
            policy.UserPolicies[username.ToLower()] = perms;
            SavePolicy(policy);
        }

        public static void AddUrlToWhitelist(string username, string url)
        {
            var policy = LoadPolicy();
            var key    = username.ToLower();
            if (!policy.UserPolicies.ContainsKey(key))
                policy.UserPolicies[key] = new UserPermissions();
            if (!policy.UserPolicies[key].AllowedUrls.Contains(url))
                policy.UserPolicies[key].AllowedUrls.Add(url);
            SavePolicy(policy);
        }

        public static void RemoveUrlFromWhitelist(string username, string url)
        {
            var policy = LoadPolicy();
            var key    = username.ToLower();
            if (policy.UserPolicies.ContainsKey(key))
                policy.UserPolicies[key].AllowedUrls.Remove(url);
            SavePolicy(policy);
        }

        public static List<UserAccount> GetAllUsers() => LoadUsers();
        public static PolicyData GetAllPolicies()      => LoadPolicy();

        public static void SetUserActive(string username, bool active)
        {
            var users = LoadUsers();
            var user  = users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                user.IsActive = active;
                SaveUsers(users);
            }
        }

        // ── File I/O ──────────────────────────────────────────────────────

        private static List<UserAccount> LoadUsers()
        {
            try
            {
                var json = File.ReadAllText(UsersFile);
                return JsonSerializer.Deserialize<List<UserAccount>>(json, JsonOpts)
                       ?? new();
            }
            catch { return new(); }
        }

        private static void SaveUsers(List<UserAccount> users)
        {
            File.WriteAllText(UsersFile,
                JsonSerializer.Serialize(users, JsonOpts));
        }

        private static PolicyData LoadPolicy()
        {
            try
            {
                var json = File.ReadAllText(PolicyFile);
                return JsonSerializer.Deserialize<PolicyData>(json, JsonOpts)
                       ?? new();
            }
            catch { return new(); }
        }

        private static void SavePolicy(PolicyData policy)
        {
            File.WriteAllText(PolicyFile,
                JsonSerializer.Serialize(policy, JsonOpts));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        public static string Hash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
