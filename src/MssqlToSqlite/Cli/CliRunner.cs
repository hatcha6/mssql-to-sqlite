using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MssqlToSqlite.Core;

namespace MssqlToSqlite.Cli
{
    internal static class CliRunner
    {
        private static StreamWriter _logFile;

        public static int Run(CommandLine options)
        {
            AttachConsole(-1);
            try
            {
                if (options.LogFile != null)
                    _logFile = new StreamWriter(options.LogFile, false, new UTF8Encoding(false)) { AutoFlush = true };

                if (options.Help)
                {
                    Console.WriteLine(CommandLine.Usage);
                    return 0;
                }
                return Execute(options);
            }
            catch (Exception error)
            {
                Write("ERROR: " + Text.Describe(error));
                return 1;
            }
            finally
            {
                _logFile?.Dispose();
            }
        }

        private static int Execute(CommandLine options)
        {
            var connection = new ConnectionOptions
            {
                Server = options.Server,
                WindowsAuth = options.User == null,
                UserName = options.User,
                Password = options.Password,
            };

            MountedDatabase mounted = null;
            try
            {
                string database = options.Database;
                string label = null;
                if (options.Backup != null || options.DataFile != null)
                {
                    label = options.Backup ?? options.DataFile;
                    mounted = MountedDatabase.Mount(connection, options.Backup != null ? SourceKind.Backup : SourceKind.DataFile, label, Write);
                    database = mounted.Name;
                }

                if (database == null)
                {
                    using (SqlConnection master = connection.Open("master"))
                    {
                        Write(SqlServerCatalog.ServerVersion(master));
                        foreach (string name in SqlServerCatalog.Databases(master))
                            Write(name);
                    }
                    return 0;
                }

                List<TableRef> all;
                using (SqlConnection source = connection.Open(database))
                    all = SqlServerCatalog.Tables(source);

                if (options.List)
                {
                    foreach (TableRef table in all)
                        Write(table.FullName + "\t" + table.ApproxRows.ToString(CultureInfo.InvariantCulture));
                    return 0;
                }

                List<TableRef> chosen = options.Tables.Count == 0 ? all : Select(all, options.Tables);
                if (chosen == null)
                    return 2;

                var request = new ExportRequest
                {
                    Connection = connection,
                    Database = database,
                    SourceLabel = label,
                    Tables = chosen,
                    OutputPath = options.Output,
                    TrimFixedText = !options.KeepCharPadding,
                };
                ExportResult result = new SqliteExporter(Report).Run(request, CancellationToken.None);
                Write(string.Format(CultureInfo.InvariantCulture, "OK {0} tables, {1} rows, {2} warnings, {3:F1}s -> {4}",
                    result.Tables, result.TotalRows, result.Warnings, result.Elapsed.TotalSeconds, result.OutputPath));
                return 0;
            }
            finally
            {
                mounted?.Dispose();
            }
        }

        private static List<TableRef> Select(List<TableRef> all, List<string> wanted)
        {
            var chosen = new List<TableRef>();
            foreach (string want in wanted)
            {
                List<TableRef> matches = want.Contains(".")
                    ? all.Where(t => string.Equals(t.FullName, want, StringComparison.OrdinalIgnoreCase)).ToList()
                    : all.Where(t => string.Equals(t.Name, want, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    Write("ERROR: no table named " + want);
                    return null;
                }
                foreach (TableRef match in matches)
                {
                    if (!chosen.Contains(match))
                        chosen.Add(match);
                }
            }
            return chosen;
        }

        private static void Report(ExportEvent ev)
        {
            switch (ev.Kind)
            {
                case ExportEventKind.Log:
                    Write(ev.Message);
                    break;
                case ExportEventKind.Warning:
                    Write("WARNING: " + ev.Message);
                    break;
                case ExportEventKind.TableFinished:
                    Write(ev.Table.FullName + ": " + ev.Rows.ToString(CultureInfo.InvariantCulture) + " rows");
                    break;
            }
        }

        private static void Write(string line)
        {
            Console.WriteLine(line);
            _logFile?.WriteLine(line);
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);
    }
}
