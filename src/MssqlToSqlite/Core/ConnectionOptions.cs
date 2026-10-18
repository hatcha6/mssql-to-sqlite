using System.Data.SqlClient;

namespace MssqlToSqlite.Core
{
    /// <summary>How to reach the SQL Server instance. Never persisted with the password.</summary>
    public sealed class ConnectionOptions
    {
        public string Server { get; set; }
        public bool WindowsAuth { get; set; } = true;
        public string UserName { get; set; }
        public string Password { get; set; }

        public string Build(string database)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = (Server ?? "").Trim(),
                InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database,
                ConnectTimeout = 15,
                ApplicationName = "MssqlToSqlite",
                IntegratedSecurity = WindowsAuth,
                // A pooled connection would keep a restored/attached temporary
                // database "in use" and make the cleanup DROP/detach fail.
                Pooling = false,
            };
            if (!WindowsAuth)
            {
                builder.UserID = (UserName ?? "").Trim();
                builder.Password = Password ?? "";
            }
            return builder.ConnectionString;
        }

        public SqlConnection Open(string database)
        {
            var connection = new SqlConnection(Build(database));
            connection.Open();
            return connection;
        }
    }
}
