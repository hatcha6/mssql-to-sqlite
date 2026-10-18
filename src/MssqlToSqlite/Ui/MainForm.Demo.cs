using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MssqlToSqlite.Core;

namespace MssqlToSqlite.Ui
{
    internal sealed partial class MainForm
    {
        // ── Preview harness ──────────────────────────────────────────────

        private bool _forceClose;

        internal void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        /// <summary>Fake states for <c>--screenshot</c>; touches no server.</summary>
        internal void ApplyDemo(string state)
        {
            if (state == "idle")
                return;
            if (state == "file")
            {
                _source.SelectedIndex = 1;
                _file.Text = @"D:\Backup\Shop2005_full.bak";
            }

            var demo = new[]
            {
                new TableRef("dbo", "CAR_PART", 18342), new TableRef("dbo", "CAR_PART_D", 4210),
                new TableRef("dbo", "CAR_PART_D2", 960), new TableRef("dbo", "CUSTOMERS", 1283),
                new TableRef("dbo", "WARED", 214), new TableRef("dbo", "SALES_INV", 96120),
                new TableRef("dbo", "SALES_INV_D", 402877), new TableRef("dbo", "PURCH_INV", 5402),
                new TableRef("dbo", "PURCH_INV_D", 38210), new TableRef("dbo", "control", 4612004),
                new TableRef("dbo", "USERS", 9), new TableRef("acc", "ACCOUNTS", 640),
                new TableRef("acc", "JOURNAL", 210334), new TableRef("dbo", "SETTINGS", 1),
            };
            if (state != "file")
            {
                _database.Items.AddRange(new object[] { "Shop2005", "Shop2005_old" });
                _rebuilding = true;
                _database.SelectedIndex = 0;
                _rebuilding = false;
            }
            SetConnection(Core.Text.ConnectedChip, Tone.Success);
            SetStatus(string.Format(Core.Text.ConnectedTo, "9.00.5000.00 Express Edition"), Theme.Success);
            _items.Clear();
            foreach (TableRef table in demo)
                _items.Add(new TableItem { Table = table, Status = Core.Text.StatusWaiting });
            _items[10].Checked = false;
            _output.Text = @"C:\Users\Shop\Documents\Shop2005_20261018.db";
            RefreshTables();
            Log(string.Format(Core.Text.ConnectedTo, "9.00.5000.00 Express Edition"));

            if (state == "export")
            {
                _busy = true;
                _cts = new CancellationTokenSource();
                for (int i = 0; i < 6; i++)
                {
                    _items[i].Status = string.Format(Core.Text.StatusDone, demo[i].ApproxRows.ToString("N0", CultureInfo.InvariantCulture));
                    _items[i].StatusColor = Theme.Success;
                    Log(string.Format(Core.Text.TableCopied, demo[i].FullName, demo[i].ApproxRows.ToString("N0", CultureInfo.InvariantCulture)));
                }
                _items[6].Status = string.Format(Core.Text.StatusCopying, "251,300");
                _items[6].StatusColor = Theme.Warning;
                RefreshTables();
                _progress.Value = 0.42;
                SetStatus(string.Format(Core.Text.Exporting, "dbo.SALES_INV_D", 7, 13), Theme.Ink);
                UpdateButtons();
            }
        }
    }
}
