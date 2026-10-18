using System;
using System.Windows.Forms;
using MssqlToSqlite.Cli;
using MssqlToSqlite.Ui;

namespace MssqlToSqlite
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            CommandLine options;
            try
            {
                options = CommandLine.Parse(args);
            }
            catch (ArgumentException error)
            {
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(CommandLine.Usage);
                return 2;
            }

            if (options.IsCli)
                return CliRunner.Run(options);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Initialize();
            var form = new MainForm();
            if (options.Screenshot != null)
                Screenshot.Attach(form, options.Screenshot, options.Demo);
            Application.Run(form);
            return 0;
        }
    }
}
