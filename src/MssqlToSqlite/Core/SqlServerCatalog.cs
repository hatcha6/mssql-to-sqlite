using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.IO;
using Microsoft.Win32;

namespace MssqlToSqlite.Core
{
    /// <summary>
    /// Read-only introspection. Every query here runs on SQL Server 2005 (catalog
    /// views only; no SERVERPROPERTY keys or DMVs added after 2005).
    /// </summary>
    public static class SqlServerCatalog
    {
        private static readonly HashSet<string> SystemDatabases =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "master", "model", "msdb", "tempdb" };

        /// <summary>Instances installed on this PC, from the registry (instant, unlike the network browse).</summary>
        public static List<string> LocalInstances()
        {
            var result = new List<string>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    {
                        if (key == null)
                            continue;
                        foreach (string instance in key.GetValueNames())
                        {
                            string name = string.Equals(instance, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                                ? "."
                                : @".\" + instance;
                            if (!result.Contains(name))
                                result.Add(name);
                        }
                    }
                }
                catch (Exception)
                {
                    // A locked-down registry just means no suggestions.
                }
            }
            return result;
        }

        /// <summary>SQL Browser broadcast. Slow (several seconds) and often blocked by firewalls.</summary>
        public static List<string> NetworkInstances()
        {
            var result = new List<string>();
            DataTable table = SqlDataSourceEnumerator.Instance.GetDataSources();
            foreach (DataRow row in table.Rows)
            {
                string server = System.Convert.ToString(row["ServerName"]);
                string instance = System.Convert.ToString(row["InstanceName"]);
                if (string.IsNullOrWhiteSpace(server))
                    continue;
                string name = string.IsNullOrWhiteSpace(instance) ? server : server + "\\" + instance;
                if (!result.Contains(name))
                    result.Add(name);
            }
            return result;
        }

        public static string ServerVersion(SqlConnection connection)
        {
            using (var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))", connection))
                return System.Convert.ToString(command.ExecuteScalar());
        }

        public static List<string> Databases(SqlConnection connection)
        {
            var result = new List<string>();
            const string query = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name";
            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0);
                    if (!SystemDatabases.Contains(name))
                        result.Add(name);
                }
            }
            return result;
        }

        public static List<TableRef> Tables(SqlConnection connection)
        {
            const string query = @"
SELECT s.name, t.name, ISNULL(SUM(p.rows), 0)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0 AND t.name <> N'sysdiagrams'
GROUP BY s.name, t.name
ORDER BY s.name, t.name";
            var result = new List<TableRef>();
            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new TableRef(reader.GetString(0), reader.GetString(1), System.Convert.ToInt64(reader.GetValue(2))));
            }
            return result;
        }

        public static TableSchema Describe(SqlConnection connection, TableRef table)
        {
            var schema = new TableSchema(table);

            const string columnQuery = @"
SELECT c.name, TYPE_NAME(c.system_type_id), c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@table))
ORDER BY c.column_id";
            using (var command = new SqlCommand(columnQuery, connection))
            {
                command.Parameters.AddWithValue("@schema", table.Schema);
                command.Parameters.AddWithValue("@table", table.Name);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string typeName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        schema.Columns.Add(new ColumnSchema
                        {
                            Name = reader.GetString(0),
                            TypeName = typeName,
                            Nullable = reader.GetBoolean(2),
                            Kind = TypeMap.KindOf(typeName),
                        });
                    }
                }
            }

            const string indexQuery = @"
SELECT i.name, i.is_primary_key, i.is_unique, c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@table))
  AND i.type IN (1, 2) AND i.is_hypothetical = 0 AND ic.is_included_column = 0
ORDER BY i.index_id, ic.key_ordinal";
            using (var command = new SqlCommand(indexQuery, connection))
            {
                command.Parameters.AddWithValue("@schema", table.Schema);
                command.Parameters.AddWithValue("@table", table.Name);
                using (var reader = command.ExecuteReader())
                {
                    IndexSchema current = null;
                    while (reader.Read())
                    {
                        string name = reader.GetString(0);
                        if (current == null || current.Name != name)
                        {
                            current = new IndexSchema { Name = name, PrimaryKey = reader.GetBoolean(1), Unique = reader.GetBoolean(2) };
                            schema.Indexes.Add(current);
                        }
                        current.Columns.Add(reader.GetString(3));
                    }
                }
            }
            return schema;
        }

        /// <summary>The folder master's data file lives in — where SQL Server's service account can surely write.</summary>
        public static string DataDirectory(SqlConnection connection)
        {
            using (var command = new SqlCommand("SELECT physical_name FROM sys.master_files WHERE database_id = 1 AND file_id = 1", connection))
                return Path.GetDirectoryName(System.Convert.ToString(command.ExecuteScalar()));
        }
    }
}
