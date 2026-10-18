using System;
using System.Collections.Generic;

namespace MssqlToSqlite.Cli
{
    /// <summary>Command-line options. Any export flag switches to headless mode (used by CI and for scripting).</summary>
    internal sealed class CommandLine
    {
        public const string Usage = @"
MssqlToSqlite — export a SQL Server database to SQLite.

  Without arguments the window opens.

  --server <name>        SQL Server instance (default .\SQLEXPRESS)
  --database <name>      database on the server
  --bak <file>           restore this backup temporarily instead (local server)
  --mdf <file>           attach a copy of this data file instead (local server)
  --user <name>          SQL Server login (omit for Windows authentication)
  --password <text>
  --tables <a,b,...>     tables to export, as name or schema.name (default: all)
  --out <file>           SQLite file to write (required to export)
  --keep-char-padding    keep the trailing spaces of char/nchar values
  --list                 list databases (or tables, with --database/--bak/--mdf)
  --log <file>           also write progress to this UTF-8 file

  --screenshot <png> [--demo idle|server|export|file]   (UI preview)

Exit codes: 0 done, 1 failed, 2 bad arguments.";

        public string Server { get; private set; } = @".\SQLEXPRESS";
        public string Database { get; private set; }
        public string Backup { get; private set; }
        public string DataFile { get; private set; }
        public string User { get; private set; }
        public string Password { get; private set; }
        public List<string> Tables { get; } = new List<string>();
        public string Output { get; private set; }
        public bool KeepCharPadding { get; private set; }
        public bool List { get; private set; }
        public string LogFile { get; private set; }
        public string Screenshot { get; private set; }
        public string Demo { get; private set; }
        public bool Help { get; private set; }

        public bool IsCli => Help || List || Output != null;

        public static CommandLine Parse(string[] args)
        {
            var options = new CommandLine();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next()
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("Missing value for " + arg);
                    return args[++i];
                }

                switch (arg.ToLowerInvariant())
                {
                    case "--server": options.Server = Next(); break;
                    case "--database": options.Database = Next(); break;
                    case "--bak": options.Backup = Next(); break;
                    case "--mdf": options.DataFile = Next(); break;
                    case "--user": options.User = Next(); break;
                    case "--password": options.Password = Next(); break;
                    case "--tables":
                        foreach (string t in Next().Split(','))
                        {
                            if (t.Trim().Length > 0)
                                options.Tables.Add(t.Trim());
                        }
                        break;
                    case "--out": options.Output = Next(); break;
                    case "--keep-char-padding": options.KeepCharPadding = true; break;
                    case "--list": options.List = true; break;
                    case "--log": options.LogFile = Next(); break;
                    case "--screenshot": options.Screenshot = Next(); break;
                    case "--demo": options.Demo = Next(); break;
                    case "-h":
                    case "--help":
                    case "/?":
                        options.Help = true;
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + arg);
                }
            }

            int sources = (options.Database != null ? 1 : 0) + (options.Backup != null ? 1 : 0) + (options.DataFile != null ? 1 : 0);
            if (sources > 1)
                throw new ArgumentException("Use only one of --database, --bak, --mdf.");
            if (options.Output != null && sources == 0)
                throw new ArgumentException("--out needs --database, --bak or --mdf.");
            return options;
        }
    }
}
