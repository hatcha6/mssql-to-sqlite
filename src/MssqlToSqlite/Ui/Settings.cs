using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MssqlToSqlite.Ui
{
    /// <summary>Remembers server, sign-in mode, user name and output folder between runs. Never the password.</summary>
    internal sealed class Settings
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Daftar", "MssqlToSqlite", "settings.ini");

        public string Server { get => Get("server"); set => _values["server"] = value; }
        public bool SqlAuth { get => Get("sql_auth") == "1"; set => _values["sql_auth"] = value ? "1" : "0"; }
        public string UserName { get => Get("user"); set => _values["user"] = value; }
        public string OutputFolder { get => Get("output_folder"); set => _values["output_folder"] = value; }
        public bool TrimFixedText { get => Get("trim_fixed") != "0"; set => _values["trim_fixed"] = value ? "1" : "0"; }

        private string Get(string key) => _values.TryGetValue(key, out string value) ? value : null;

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            settings._values[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }
            }
            catch (Exception)
            {
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                foreach (var pair in _values)
                    lines.Add(pair.Key + "=" + (pair.Value ?? "").Replace("\r", "").Replace("\n", ""));
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }
}
