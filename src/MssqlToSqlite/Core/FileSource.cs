using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;

namespace MssqlToSqlite.Core
{
    public enum SourceKind
    {
        Database,
        Backup,
        DataFile,
    }

    /// <summary>
    /// A .bak restored, or a copy of an .mdf attached, under a throwaway name on a
    /// local instance. Disposing removes it again, so the shop's server is left as
    /// it was found. The original files are never opened for writing: a backup is
    /// only read, and an .mdf is copied first because attaching upgrades the file
    /// in place on a newer server and that cannot be undone.
    /// </summary>
    public sealed class MountedDatabase : IDisposable
    {
        private readonly ConnectionOptions _options;
        private readonly List<string> _copies = new List<string>();
        private bool _attached;

        private MountedDatabase(ConnectionOptions options, string name)
        {
            _options = options;
            Name = name;
        }

        public string Name { get; }

        public static MountedDatabase Mount(ConnectionOptions options, SourceKind kind, string path, Action<string> log)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(Text.FileNotFound, path);

            string name = "MssqlToSqlite_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var mounted = new MountedDatabase(options, name);
            try
            {
                if (kind == SourceKind.Backup)
                    mounted.Restore(path, log);
                else
                    mounted.Attach(path, log);
                return mounted;
            }
            catch
            {
                mounted.Dispose();
                throw;
            }
        }

        private void Restore(string backupPath, Action<string> log)
        {
            using (var connection = _options.Open("master"))
            {
                string dataDir = SqlServerCatalog.DataDirectory(connection);

                // The newest backup set in the file is the one people mean.
                int position = 1;
                using (var header = new SqlCommand("RESTORE HEADERONLY FROM DISK = @path", connection))
                {
                    header.Parameters.AddWithValue("@path", backupPath);
                    header.CommandTimeout = 0;
                    using (var reader = header.ExecuteReader())
                    {
                        int ordinal = reader.GetOrdinal("Position");
                        while (reader.Read())
                            position = Math.Max(position, System.Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
                    }
                }

                var moves = new List<KeyValuePair<string, string>>();
                using (var files = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @path WITH FILE = @position", connection))
                {
                    files.Parameters.AddWithValue("@path", backupPath);
                    files.Parameters.AddWithValue("@position", position);
                    files.CommandTimeout = 0;
                    using (var reader = files.ExecuteReader())
                    {
                        int logicalOrdinal = reader.GetOrdinal("LogicalName");
                        int typeOrdinal = reader.GetOrdinal("Type");
                        int n = 0;
                        while (reader.Read())
                        {
                            string logical = reader.GetString(logicalOrdinal);
                            string extension = reader.GetString(typeOrdinal) == "L" ? ".ldf" : (n == 0 ? ".mdf" : ".ndf");
                            moves.Add(new KeyValuePair<string, string>(logical, Path.Combine(dataDir, Name + "_" + n + extension)));
                            n++;
                        }
                    }
                }

                log(Text.Restoring);
                var sql = new System.Text.StringBuilder("RESTORE DATABASE " + Sql.QuoteMssql(Name) + " FROM DISK = @path WITH FILE = @position, RECOVERY");
                using (var restore = new SqlCommand { Connection = connection, CommandTimeout = 0 })
                {
                    restore.Parameters.AddWithValue("@path", backupPath);
                    restore.Parameters.AddWithValue("@position", position);
                    for (int i = 0; i < moves.Count; i++)
                    {
                        sql.Append(", MOVE @l" + i + " TO @p" + i);
                        restore.Parameters.AddWithValue("@l" + i, moves[i].Key);
                        restore.Parameters.AddWithValue("@p" + i, moves[i].Value);
                    }
                    restore.CommandText = sql.ToString();
                    _attached = true; // a failed restore can still leave a "restoring" shell to drop
                    restore.ExecuteNonQuery();
                }
            }
        }

        private void Attach(string mdfPath, Action<string> log)
        {
            string ldfPath = FindLog(mdfPath);
            using (var connection = _options.Open("master"))
            {
                // SQL Server's own DATA folder is the one place its service account
                // can surely write (it needs write access even to rebuild a log).
                // Copying there needs an elevated tool on a full instance, so fall
                // back to the original's folder when it is refused.
                log(Text.CopyingDataFile);
                string folder = SqlServerCatalog.DataDirectory(connection);
                string copy;
                try
                {
                    copy = CopyIn(mdfPath, folder, ".mdf");
                }
                catch (UnauthorizedAccessException)
                {
                    folder = Path.GetDirectoryName(Path.GetFullPath(mdfPath));
                    copy = CopyIn(mdfPath, folder, ".mdf");
                }
                string logCopy = ldfPath != null ? CopyIn(ldfPath, folder, "_log.ldf") : null;

                log(Text.Attaching);
                string sql = "CREATE DATABASE " + Sql.QuoteMssql(Name) + " ON (FILENAME = " + Sql.NLiteral(copy) + ")"
                    + (logCopy != null ? ", (FILENAME = " + Sql.NLiteral(logCopy) + ") FOR ATTACH" : " FOR ATTACH_REBUILD_LOG");
                if (logCopy == null)
                    _copies.Add(Path.Combine(folder, Name + "_log.ldf")); // the rebuilt log lands beside the copy
                try
                {
                    Execute(connection, sql);
                }
                catch (SqlException) when (logCopy != null)
                {
                    // A log from a different moment than the .mdf will not attach; a rebuilt one will.
                    _copies.Remove(logCopy);
                    TryDelete(logCopy);
                    _copies.Add(Path.Combine(folder, Name + "_log.ldf"));
                    Execute(connection, "CREATE DATABASE " + Sql.QuoteMssql(Name) + " ON (FILENAME = " + Sql.NLiteral(copy) + ") FOR ATTACH_REBUILD_LOG");
                }
                _attached = true;
            }
        }

        /// <summary>Copies into <paramref name="folder"/> under this mount's name, writable (detached and CD/USB files are often read-only).</summary>
        private string CopyIn(string source, string folder, string suffix)
        {
            string copy = Path.Combine(folder, Name + suffix);
            File.Copy(source, copy);
            _copies.Add(copy);
            File.SetAttributes(copy, File.GetAttributes(copy) & ~FileAttributes.ReadOnly);
            return copy;
        }

        /// <summary>The usual log names beside an .mdf: Shop_log.ldf, Shop.ldf.</summary>
        private static string FindLog(string mdfPath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(mdfPath));
            string stem = Path.GetFileNameWithoutExtension(mdfPath);
            foreach (string candidate in new[] { stem + "_log.ldf", stem + ".ldf" })
            {
                string path = Path.Combine(folder, candidate);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        private static void Execute(SqlConnection connection, string sql)
        {
            using (var command = new SqlCommand(sql, connection) { CommandTimeout = 0 })
                command.ExecuteNonQuery();
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

        public void Dispose()
        {
            if (_attached)
            {
                try
                {
                    using (var connection = _options.Open("master"))
                    {
                        string name = Sql.QuoteMssql(Name);
                        // A restored database is dropped (its files are ours);
                        // an attached copy is detached and the copy deleted below.
                        string sql = _copies.Count == 0
                            ? "IF DB_ID(" + Sql.NLiteral(Name) + ") IS NOT NULL BEGIN BEGIN TRY ALTER DATABASE " + name + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; END TRY BEGIN CATCH END CATCH; DROP DATABASE " + name + "; END"
                            : "IF DB_ID(" + Sql.NLiteral(Name) + ") IS NOT NULL BEGIN ALTER DATABASE " + name + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; EXEC sp_detach_db " + Sql.NLiteral(Name) + "; END";
                        using (var command = new SqlCommand(sql, connection) { CommandTimeout = 0 })
                            command.ExecuteNonQuery();
                    }
                }
                catch (Exception)
                {
                    // Best effort: a leftover MssqlToSqlite_* database is harmless and named for what it is.
                }
                _attached = false;
            }

            foreach (string copy in _copies)
                TryDelete(copy);
            _copies.Clear();
        }
    }
}
