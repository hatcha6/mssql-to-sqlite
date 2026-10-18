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
        // ── Layout ───────────────────────────────────────────────────────

        private Control BuildSourceCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4)) };
            var grid = Column();
            // SQL sign-in rows can outgrow a 768px-tall screen; scroll only then.
            grid.Layout += (s, e) =>
            {
                bool overflow = grid.GetPreferredSize(new Size(grid.ClientSize.Width, 0)).Height > grid.Height;
                if (grid.AutoScroll != overflow)
                    grid.AutoScroll = overflow;
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // shrink to fit: never a horizontal bar
            grid.Controls.Add(TitleLabel(Core.Text.SourceTitle));
            grid.Controls.Add(Stretch(_source));

            grid.Controls.Add(FieldLabel(Core.Text.ServerLabel));
            grid.Controls.Add(Row(new ComboShell(_server), _discover));

            grid.Controls.Add(FieldLabel(Core.Text.AuthLabel));
            _auth.Items.AddRange(new object[] { Core.Text.AuthWindows, Core.Text.AuthSql });
            grid.Controls.Add(Stretch(new ComboShell(_auth)));

            _userRows.Add(FieldLabel(Core.Text.UserLabel));
            _userRows.Add(Stretch(_user));
            _userRows.Add(FieldLabel(Core.Text.PasswordLabel));
            _userRows.Add(Stretch(_password));
            foreach (Control c in _userRows)
                grid.Controls.Add(c);

            _fileRows.Add(FieldLabel(Core.Text.FileLabel));
            _fileRows.Add(Row(_file, _browseFile));
            foreach (Control c in _fileRows)
                grid.Controls.Add(c);

            var connectRow = Row(_connect, _connectionChip);
            connectRow.Margin = new Padding(0, Theme.S(Theme.Compact ? 6 : 12), 0, Theme.S(4));
            _connect.Width = Theme.S(120);
            connectRow.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
            connectRow.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
            _connectionChip.Anchor = AnchorStyles.Right | AnchorStyles.Left;
            _connectionChip.Dock = DockStyle.None;
            grid.Controls.Add(connectRow);

            _databaseRows.Add(FieldLabel(Core.Text.DatabaseLabel));
            _databaseRows.Add(Stretch(new ComboShell(_database)));
            foreach (Control c in _databaseRows)
                grid.Controls.Add(c);

            _hint.Margin = new Padding(Theme.S(4), Theme.S(Theme.Compact ? 6 : 12), Theme.S(4), 0);
            _hint.MaximumSize = new Size(Theme.S(340), 0);
            grid.Controls.Add(_hint);
            card.Controls.Add(grid);

            _source.SelectedIndexChanged += (s, e) => { ApplySourceMode(); ResetConnection(); };
            _auth.SelectedIndexChanged += (s, e) => { ApplyAuthMode(); };
            _discover.Click += (s, e) => DiscoverInstances();
            _browseFile.Click += (s, e) => BrowseSourceFile();
            _connect.Click += (s, e) => Connect();
            _database.SelectedIndexChanged += (s, e) => { if (!_rebuilding) LoadTables(); };
            _server.TextChanged += (s, e) => UpdateButtons();
            _file.Box.TextChanged += (s, e) => UpdateButtons();
            return card;
        }

        private Control BuildTablesCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4)) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Surface, Margin = Padding.Empty };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty, BackColor = Theme.Surface };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(TitleLabel(Core.Text.TablesTitle), 0, 0);
            _summary.TextAlign = ContentAlignment.MiddleLeft; // mirrored: the far (left) end in RTL
            _summary.Dock = DockStyle.Fill;
            header.Controls.Add(_summary, 1, 0);
            grid.Controls.Add(header, 0, 0);

            var tools = Row(_search, _selectAll, _selectNone);
            tools.Margin = new Padding(0, Theme.S(4), 0, Theme.S(4));
            grid.Controls.Add(tools, 0, 1);

            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.BorderStyle = BorderStyle.None;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.RightToLeftLayout = true;
            _list.Font = Theme.Body;
            _list.ForeColor = Theme.Ink;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(Core.Text.ColumnTable, Theme.S(260));
            _list.Columns.Add(Core.Text.ColumnSchema, Theme.S(80));
            _list.Columns.Add(Core.Text.ColumnRows, Theme.S(100), HorizontalAlignment.Right);
            _list.Columns.Add(Core.Text.ColumnStatus, Theme.S(150));
            _list.ItemChecked += (s, e) =>
            {
                if (_rebuilding)
                    return;
                if (e.Item.Tag is TableItem item)
                    item.Checked = e.Item.Checked;
                UpdateSummary();
                UpdateButtons();
            };
            _list.Resize += (s, e) => FitColumns();

            _empty.Dock = DockStyle.Fill;
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.BackColor = Theme.Surface;
            var listHost = new Card { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4)), Padding = new Padding(Theme.S(2), Theme.S(6), Theme.S(2), Theme.S(6)), BackColor = Theme.Surface };
            listHost.Controls.Add(_list);
            listHost.Controls.Add(_empty);
            grid.Controls.Add(listHost, 0, 2);

            grid.Controls.Add(FieldLabel(Core.Text.LogTitle), 0, 3);
            var logWell = new Card { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4)), Padding = new Padding(Theme.S(10)), BackColor = Theme.Surface, Fill = Theme.SurfaceSunken };
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.SurfaceSunken;
            _log.ForeColor = Theme.Ink;
            _log.Font = Theme.Small;
            _log.Dock = DockStyle.Fill;
            logWell.Controls.Add(_log);
            grid.Controls.Add(logWell, 0, 4);
            card.Controls.Add(grid);

            _search.Box.TextChanged += (s, e) => RefreshTables();
            _selectAll.Click += (s, e) => SetVisibleChecked(true);
            _selectNone.Click += (s, e) => SetVisibleChecked(false);
            return card;
        }

        private Control BuildFooter()
        {
            var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(4), Theme.S(4)) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true, BackColor = Theme.Surface, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label outputLabel = FieldLabel(Core.Text.OutputLabel);
            outputLabel.Margin = new Padding(Theme.S(4), 0, Theme.S(8), 0);
            outputLabel.Anchor = AnchorStyles.Right;
            outputLabel.Dock = DockStyle.None;
            grid.Controls.Add(outputLabel, 0, 0);
            _output.Dock = DockStyle.Fill;
            grid.Controls.Add(_output, 1, 0);
            grid.Controls.Add(Sized(_browseOutput), 2, 0);
            _export.Width = Theme.S(140);
            grid.Controls.Add(_export, 3, 0);
            grid.Controls.Add(Sized(_cancelButton), 4, 0);

            _trim.Text = Core.Text.TrimFixedText;
            _trim.AutoSize = true;
            _trim.ForeColor = Theme.Ink;
            _trim.Margin = new Padding(Theme.S(4), Theme.S(6), Theme.S(4), 0);
            grid.Controls.Add(_trim, 1, 1);
            grid.SetColumnSpan(_trim, 4);

            _progress.Dock = DockStyle.Fill;
            grid.Controls.Add(_progress, 0, 2);
            grid.SetColumnSpan(_progress, 5);

            _status.AutoSize = true;
            _status.Font = Theme.Body;
            _status.Margin = new Padding(Theme.S(4), Theme.S(4), Theme.S(4), 0);
            _status.MaximumSize = new Size(Theme.S(820), 0);
            grid.Controls.Add(_status, 0, 3);
            grid.SetColumnSpan(_status, 4);
            grid.Controls.Add(Sized(_openFolder), 4, 3);
            card.Controls.Add(grid);

            _browseOutput.Click += (s, e) => BrowseOutput();
            _export.Click += (s, e) => StartExport();
            _cancelButton.Click += (s, e) => CancelExport();
            _openFolder.Click += (s, e) => OpenOutputFolder();
            _output.Box.TextChanged += (s, e) => UpdateButtons();
            _output.Box.KeyPress += (s, e) => _outputEdited = true;
            return card;
        }

        private static TableLayoutPanel Column()
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = false,
                BackColor = Theme.Surface,
                Margin = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
        }

        private static TableLayoutPanel Row(Control fill, params Control[] trailing)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 + trailing.Length, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Surface };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fill.Dock = DockStyle.Fill;
            row.Controls.Add(fill, 0, 0);
            for (int i = 0; i < trailing.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.Controls.Add(Sized(trailing[i]), i + 1, 0);
            }
            return row;
        }

        private static Control Sized(Control control)
        {
            if (control is PointyButton button && button.Width < button.GetPreferredSize(Size.Empty).Width)
                button.Width = button.GetPreferredSize(Size.Empty).Width;
            return control;
        }

        private static Control Stretch(Control control)
        {
            control.Dock = DockStyle.Fill;
            return control;
        }

        private static Label TitleLabel(string text) => new Label
        {
            Text = text,
            Font = Theme.Title,
            ForeColor = Theme.Ink,
            AutoSize = true,
            Margin = new Padding(Theme.S(4), 0, Theme.S(4), Theme.S(8)),
        };

        private static Label FieldLabel(string text) => new Label
        {
            Text = text,
            Font = Theme.Label,
            ForeColor = Theme.MutedInk,
            AutoSize = true,
            Margin = new Padding(Theme.S(4), Theme.S(Theme.Compact ? 4 : 8), Theme.S(4), 0),
        };

        private static Label MutedLabel(string text) => new Label
        {
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.MutedInk,
            AutoSize = true,
        };

        private static ComboBox NewCombo(ComboBoxStyle style) => new ComboBox
        {
            DropDownStyle = style,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Body,
            BackColor = Theme.Surface,
            ForeColor = Theme.Ink,
            Margin = new Padding(Theme.S(4), Theme.S(6), Theme.S(4), Theme.S(6)),
            IntegralHeight = false,
            MaxDropDownItems = 14,
        };

        private void FitColumns()
        {
            int rest = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width;
            int width = _list.ClientSize.Width - rest - 4;
            if (width > Theme.S(120))
                _list.Columns[0].Width = width;
        }
    }
}
