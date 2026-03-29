using System;
using System.Collections.Generic;

namespace SecureBrowser
{
    // ── User account stored in users.json ────────────────────────────────
    public class UserAccount
    {
        public string Username    { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role        { get; set; } = "User";   // Admin | User
        public string Department  { get; set; } = "";
        public bool   IsActive    { get; set; } = true;
    }

    // ── Per-user security permissions stored in policy.json ──────────────
    public class UserPermissions
    {
        public bool         AllowClipboard     { get; set; } = false;
        public bool         AllowPrint         { get; set; } = false;
        public bool         SSLOnly            { get; set; } = true;
        public List<string> AllowedUrls        { get; set; } = new();
        public List<string> AllowedLocations   { get; set; } = new() { "Office" };
    }

    // ── Top-level policy file ─────────────────────────────────────────────
    public class PolicyData
    {
        public Dictionary<string, UserPermissions> UserPolicies { get; set; } = new();
    }

    // ── Single audit event written to audit.log ───────────────────────────
    public class AuditEvent
    {
        public DateTime Timestamp  { get; set; } = DateTime.Now;
        public string   Username   { get; set; } = "";
        public string   EventType  { get; set; } = "";   // LOGIN | LOGOUT | NAV_BLOCKED | COPY_BLOCKED | SCREENSHOT | CONFIG_CHANGE
        public string   Details    { get; set; } = "";
        public string   Severity   { get; set; } = "Info";  // Info | Warning | Critical
        public string   Location   { get; set; } = "";
    }

    // ── Active browser session passed from login → main form ─────────────
    public class UserSession
    {
        public UserAccount    Account     { get; set; } = new();
        public UserPermissions Permissions { get; set; } = new();
        public string         Location    { get; set; } = "Office";
        public DateTime       LoginTime   { get; set; } = DateTime.Now;
    }
}
