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
            // Copy beside the original: SQL Server's service account already reads
            // that folder if this file was ever attached from there.
            string folder = Path.GetDirectoryName(Path.GetFullPath(mdfPath));
            string copy = Path.Combine(folder, Name + ".mdf");
            log(Text.CopyingDataFile);
            File.Copy(mdfPath, copy);
            _copies.Add(copy);

            using (var connection = _options.Open("master"))
            {
                log(Text.Attaching);
                string sql = "CREATE DATABASE " + Sql.QuoteMssql(Name) + " ON (FILENAME = " + Sql.NLiteral(copy) + ") FOR ATTACH_REBUILD_LOG";
                using (var attach = new SqlCommand(sql, connection) { CommandTimeout = 0 })
                    attach.ExecuteNonQuery();
                _attached = true;
            }

            // ATTACH_REBUILD_LOG writes a fresh log next to the copy.
            _copies.Add(Path.Combine(folder, Name + "_log.LDF"));
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
            {
                try
                {
                    if (File.Exists(copy))
                        File.Delete(copy);
                }
                catch (Exception)
                {
                }
            }
            _copies.Clear();
        }
    }
}
