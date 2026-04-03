using System;
using System.Collections.Generic;

namespace SecureBrowser.Models
{
    public class UserAccount
    {
        public int    Id           { get; set; }
        public string Username     { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string DisplayName  { get; set; } = "";
        public string Role         { get; set; } = "User";
        public string Department   { get; set; } = "";
        public bool   IsActive     { get; set; } = true;
    }

    public class UserPermissions
    {
        public bool         AllowClipboard   { get; set; } = false;
        public bool         AllowPrint       { get; set; } = false;
        public bool         SSLOnly          { get; set; } = true;
        public List<string> AllowedUrls      { get; set; } = new();
        public List<string> AllowedLocations { get; set; } = new() { "Office" };
    }

    public class AuditEvent
    {
        public int      Id        { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string   Username  { get; set; } = "";
        public string   EventType { get; set; } = "";
        public string   Details   { get; set; } = "";
        public string   Severity  { get; set; } = "Info";
        public string   Location  { get; set; } = "";
    }

    public class UserSession
    {
        public UserAccount     Account     { get; set; } = new();
        public UserPermissions Permissions { get; set; } = new();
        public string          Location    { get; set; } = "Office";
        public DateTime        LoginTime   { get; set; } = DateTime.Now;
    }
}
