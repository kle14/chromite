using Npgsql;

namespace SecureBrowser.Data
{
    /// <summary>
    /// Provides database connections. Connection string is read from
    /// environment variable DB_CONN or falls back to the Docker default.
    /// </summary>
    public static class Db
    {
        private static readonly string ConnStr =
            System.Environment.GetEnvironmentVariable("DB_CONN")
            ?? "Host=localhost;Port=5433;Database=securebrowser;Username=sbadmin;Password=SBDemo2024!";

        public static NpgsqlConnection Open()
        {
            var conn = new NpgsqlConnection(ConnStr);
            conn.Open();
            return conn;
        }

        /// <summary>
        /// Quick connectivity test — returns true if the database is reachable.
        /// </summary>
        public static bool TestConnection(out string error)
        {
            error = "";
            try
            {
                using var conn = Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.ExecuteScalar();
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
