using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MssqlToSqlite.Core
{
    public sealed class ExportRequest
    {
        public ConnectionOptions Connection { get; set; }
        public string Database { get; set; }
        public IList<TableRef> Tables { get; set; }
        public string OutputPath { get; set; }

        /// <summary>What the source was called before mounting (a .bak/.mdf path); recorded in _export_info.</summary>
        public string SourceLabel { get; set; }

        /// <summary>char/nchar values arrive space-padded to their width; padding is storage, not data.</summary>
        public bool TrimFixedText { get; set; } = true;
    }

    public enum ExportEventKind
    {
        Log,
        Warning,
        TableStarted,
        Rows,
        TableFinished,
    }

    public sealed class ExportEvent
    {
        public ExportEventKind Kind { get; set; }
        public TableRef Table { get; set; }
        public int TableIndex { get; set; }
        public long Rows { get; set; }
        public string Message { get; set; }
    }

    public sealed class ExportResult
    {
        public string OutputPath { get; set; }
        public long TotalRows { get; set; }
        public int Tables { get; set; }
        public int Warnings { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>
    /// Copies the chosen tables into a new SQLite file.
    ///
    /// The file is built as <c>output.partial</c> and only renamed into place when
    /// every table made it, so a cancelled or failed run never leaves a
    /// half-written database that looks finished. Indexes are created after the
    /// data (much faster) and a failing one is a warning, not a lost export.
    /// </summary>
    public sealed class SqliteExporter
    {
        private const int CommitEvery = 50000;
        private readonly Action<ExportEvent> _report;
        private int _warnings;

        public SqliteExporter(Action<ExportEvent> report)
        {
            _report = report ?? (_ => { });
        }

        public static string ToolVersion =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public ExportResult Run(ExportRequest request, CancellationToken cancel)
        {
            var clock = Stopwatch.StartNew();
            string output = Path.GetFullPath(request.OutputPath);
            string partial = output + ".partial";
            if (File.Exists(partial))
                File.Delete(partial);

            long totalRows = 0;
            var exported = new List<KeyValuePair<TableRef, KeyValuePair<string, long>>>();
            string serverVersion;

            try
            {
                using (var source = request.Connection.Open(request.Database))
                using (var target = new SQLiteConnection("Data Source=" + partial + ";Version=3;Pooling=False;"))
                {
                    target.Open();
                    serverVersion = SqlServerCatalog.ServerVersion(source);
                    Log(string.Format(Text.ConnectedTo, serverVersion));
                    Execute(target, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-65536; PRAGMA temp_store=MEMORY;");

                    var names = new TableNamer();
                    var schemas = new List<KeyValuePair<TableSchema, string>>();
                    for (int i = 0; i < request.Tables.Count; i++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        TableRef table = request.Tables[i];
                        TableSchema schema = SqlServerCatalog.Describe(source, table);
                        if (schema.Columns.Count == 0)
                        {
                            Warn(string.Format(Text.TableHasNoColumns, table.FullName));
                            continue;
                        }

                        string targetName = names.Assign(table);
                        _report(new ExportEvent { Kind = ExportEventKind.TableStarted, Table = table, TableIndex = i });
                        Execute(target, CreateTableSql(schema, targetName));
                        long rows = CopyRows(source, target, schema, targetName, request.TrimFixedText, i, cancel);
                        totalRows += rows;
                        exported.Add(new KeyValuePair<TableRef, KeyValuePair<string, long>>(table, new KeyValuePair<string, long>(targetName, rows)));
                        schemas.Add(new KeyValuePair<TableSchema, string>(schema, targetName));
                        _report(new ExportEvent { Kind = ExportEventKind.TableFinished, Table = table, TableIndex = i, Rows = rows });
                    }

                    Log(Text.CreatingIndexes);
                    var indexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in schemas)
                    {
                        cancel.ThrowIfCancellationRequested();
                        CreateIndexes(target, pair.Key, pair.Value, indexNames);
                    }

                    WriteExportInfo(target, request, serverVersion, exported, totalRows);
                    Execute(target, "PRAGMA journal_mode=DELETE;");
                }

                SQLiteConnection.ClearAllPools();
                if (File.Exists(output))
                    File.Delete(output);
                File.Move(partial, output);
            }
            catch
            {
                SQLiteConnection.ClearAllPools();
                TryDelete(partial);
                throw;
            }

            return new ExportResult
            {
                OutputPath = output,
                TotalRows = totalRows,
                Tables = exported.Count,
                Warnings = _warnings,
                Elapsed = clock.Elapsed,
            };
        }

        private static string CreateTableSql(TableSchema schema, string targetName)
        {
            var sql = new StringBuilder("CREATE TABLE " + Sql.QuoteSqlite(targetName) + " (");
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                ColumnSchema column = schema.Columns[i];
                if (i > 0)
                    sql.Append(", ");
                sql.Append(Sql.QuoteSqlite(column.Name));
                string type = TypeMap.SqliteType(column.Kind);
                if (type.Length > 0)
                    sql.Append(' ').Append(type);
                if (!column.Nullable)
                    sql.Append(" NOT NULL");
            }
            return sql.Append(')').ToString();
        }

        private long CopyRows(SqlConnection source, SQLiteConnection target, TableSchema schema, string targetName, bool trimFixed, int tableIndex, CancellationToken cancel)
        {
            var select = new StringBuilder("SELECT ");
            var insert = new StringBuilder("INSERT INTO " + Sql.QuoteSqlite(targetName) + " (");
            var values = new StringBuilder(") VALUES (");
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                string comma = i > 0 ? ", " : "";
                select.Append(comma).Append(TypeMap.SelectExpression(schema.Columns[i]));
                insert.Append(comma).Append(Sql.QuoteSqlite(schema.Columns[i].Name));
                values.Append(comma).Append("@p").Append(i);
            }
            select.Append(" FROM ").Append(schema.Table.Quoted);

            long rows = 0;
            long rounded = 0;
            var lastReport = Stopwatch.StartNew();
            using (var read = new SqlCommand(select.ToString(), source) { CommandTimeout = 0 })
            using (var reader = read.ExecuteReader(CommandBehavior.SequentialAccess))
            using (var write = new SQLiteCommand(insert.Append(values).Append(')').ToString(), target))
            {
                var kinds = new SqlKind[schema.Columns.Count];
                var parameters = new SQLiteParameter[schema.Columns.Count];
                for (int i = 0; i < kinds.Length; i++)
                {
                    kinds[i] = schema.Columns[i].Kind;
                    parameters[i] = new SQLiteParameter("@p" + i, TypeMap.ParameterType(kinds[i]));
                    write.Parameters.Add(parameters[i]);
                }

                SQLiteTransaction transaction = target.BeginTransaction();
                try
                {
                    write.Transaction = transaction;
                    while (reader.Read())
                    {
                        cancel.ThrowIfCancellationRequested();
                        for (int i = 0; i < kinds.Length; i++)
                        {
                            object value = TypeMap.Read(reader, i, kinds[i], trimFixed);
                            if (kinds[i] == SqlKind.Variant && !(value is DBNull))
                                parameters[i].DbType = TypeMap.ParameterTypeOf(value);
                            else if (kinds[i] == SqlKind.Decimal && value is string number && TypeMap.SignificantDigits(number) > 15)
                                rounded++;
                            parameters[i].Value = value;
                        }
                        write.ExecuteNonQuery();
                        rows++;

                        if (rows % CommitEvery == 0)
                        {
                            transaction.Commit();
                            transaction.Dispose();
                            transaction = target.BeginTransaction();
                            write.Transaction = transaction;
                        }
                        if (lastReport.ElapsedMilliseconds >= 200)
                        {
                            _report(new ExportEvent { Kind = ExportEventKind.Rows, Table = schema.Table, TableIndex = tableIndex, Rows = rows });
                            lastReport.Reset();
                            lastReport.Start();
                        }
                    }
                    transaction.Commit();
                }
                finally
                {
                    transaction.Dispose();
                }
            }
            if (rounded > 0)
                Warn(string.Format(Text.DecimalsRounded, schema.Table.FullName, rounded.ToString("N0", CultureInfo.InvariantCulture)));
            return rows;
        }

        private void CreateIndexes(SQLiteConnection target, TableSchema schema, string targetName, HashSet<string> taken)
        {
            foreach (IndexSchema index in schema.Indexes)
            {
                string baseName = targetName + "__" + (index.PrimaryKey ? "pk" : index.Name);
                string name = baseName;
                for (int n = 2; !taken.Add(name); n++)
                    name = baseName + "_" + n;

                var columns = new StringBuilder();
                foreach (string column in index.Columns)
                {
                    if (columns.Length > 0)
                        columns.Append(", ");
                    columns.Append(Sql.QuoteSqlite(column));
                }
                string on = " ON " + Sql.QuoteSqlite(targetName) + " (" + columns + ")";
                bool unique = index.Unique || index.PrimaryKey;
                try
                {
                    Execute(target, "CREATE " + (unique ? "UNIQUE " : "") + "INDEX " + Sql.QuoteSqlite(name) + on);
                }
                catch (SQLiteException) when (unique)
                {
                    // SQL Server compares case- and trailing-space-insensitively; SQLite
                    // does not, so uniqueness can only get looser — but keep the lookup
                    // speed even if a constraint ever fails.
                    Warn(string.Format(Text.UniqueIndexDowngraded, schema.Table.FullName, index.Name));
                    Execute(target, "CREATE INDEX " + Sql.QuoteSqlite(name) + on);
                }
            }
        }

        private static void WriteExportInfo(SQLiteConnection target, ExportRequest request, string serverVersion, List<KeyValuePair<TableRef, KeyValuePair<string, long>>> exported, long totalRows)
        {
            Execute(target, "CREATE TABLE \"_export_info\" (\"key\" TEXT PRIMARY KEY, \"value\" TEXT)");
            Execute(target, "CREATE TABLE \"_export_tables\" (\"source_schema\" TEXT, \"source_table\" TEXT, \"sqlite_table\" TEXT, \"rows\" INTEGER)");
            using (var transaction = target.BeginTransaction())
            {
                var info = new Dictionary<string, string>
                {
                    { "tool", "MssqlToSqlite" },
                    { "tool_version", ToolVersion },
                    { "source_server", request.Connection.Server },
                    { "source_version", serverVersion },
                    { "source_database", request.Database },
                    { "source_file", request.SourceLabel ?? "" },
                    { "exported_at_utc", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                    { "tables", exported.Count.ToString(CultureInfo.InvariantCulture) },
                    { "rows", totalRows.ToString(CultureInfo.InvariantCulture) },
                    { "trim_fixed_text", request.TrimFixedText ? "1" : "0" },
                };
                using (var command = new SQLiteCommand("INSERT INTO \"_export_info\" VALUES (@k, @v)", target, transaction))
                {
                    foreach (var pair in info)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@k", pair.Key);
                        command.Parameters.AddWithValue("@v", pair.Value ?? "");
                        command.ExecuteNonQuery();
                    }
                }
                using (var command = new SQLiteCommand("INSERT INTO \"_export_tables\" VALUES (@s, @t, @n, @r)", target, transaction))
                {
                    foreach (var pair in exported)
                    {
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@s", pair.Key.Schema);
                        command.Parameters.AddWithValue("@t", pair.Key.Name);
                        command.Parameters.AddWithValue("@n", pair.Value.Key);
                        command.Parameters.AddWithValue("@r", pair.Value.Value);
                        command.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection))
                command.ExecuteNonQuery();
        }

        private void Log(string message) => _report(new ExportEvent { Kind = ExportEventKind.Log, Message = message });

        private void Warn(string message)
        {
            _warnings++;
            _report(new ExportEvent { Kind = ExportEventKind.Warning, Message = message });
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// dbo tables keep their own name (the migration connectors look tables up
        /// by it); other schemas become schema_table. SQLite names are
        /// case-insensitive, so clashes are resolved case-insensitively.
        /// </summary>
        private sealed class TableNamer
        {
            private readonly HashSet<string> _taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_export_info",
                "_export_tables",
            };

            public string Assign(TableRef table)
            {
                string baseName = string.Equals(table.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                    ? table.Name
                    : table.Schema + "_" + table.Name;
                if (baseName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                    baseName = "_" + baseName;
                string name = baseName;
                for (int n = 2; !_taken.Add(name); n++)
                    name = baseName + "_" + n;
                return name;
            }
        }
    }
}
