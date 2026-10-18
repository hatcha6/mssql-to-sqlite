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
    internal sealed partial class MainForm : Form
    {
        private sealed class TableItem
        {
            public TableRef Table;
            public bool Checked = true;
            public string Status = "";
            public Color StatusColor = Theme.MutedInk;
        }

        private readonly Settings _settings = Settings.Load();
        private readonly List<TableItem> _items = new List<TableItem>();

        // Source card
        private readonly SegmentedControl _source = new SegmentedControl(Core.Text.SourceServer, Core.Text.SourceBackup, Core.Text.SourceDataFile);
        private readonly ComboBox _server = NewCombo(ComboBoxStyle.DropDown);
        private readonly PointyButton _discover = new PointyButton(Core.Text.FindOnNetwork, ButtonVariant.Text);
        private readonly ComboBox _auth = NewCombo(ComboBoxStyle.DropDownList);
        private readonly InputShell _user = new InputShell();
        private readonly InputShell _password = new InputShell(null, password: true);
        private readonly InputShell _file = new InputShell();
        private readonly PointyButton _browseFile = new PointyButton(Core.Text.Browse);
        private readonly PointyButton _connect = new PointyButton(Core.Text.Connect, ButtonVariant.Filled);
        private readonly Chip _connectionChip = new Chip();
        private readonly ComboBox _database = NewCombo(ComboBoxStyle.DropDownList);
        private readonly Label _hint = MutedLabel(Core.Text.ServerModeHint);
        private readonly List<Control> _userRows = new List<Control>();
        private readonly List<Control> _fileRows = new List<Control>();
        private readonly List<Control> _databaseRows = new List<Control>();

        // Tables card
        private readonly Label _summary = MutedLabel("");
        private readonly InputShell _search = new InputShell(Core.Text.SearchTables);
        private readonly PointyButton _selectAll = new PointyButton(Core.Text.SelectAll, ButtonVariant.Text);
        private readonly PointyButton _selectNone = new PointyButton(Core.Text.SelectNone, ButtonVariant.Text);
        private readonly ListView _list = new ListView();
        private readonly Label _empty = MutedLabel(Core.Text.TablesEmpty);
        private readonly TextBox _log = new TextBox();

        // Footer
        private readonly InputShell _output = new InputShell();
        private readonly PointyButton _browseOutput = new PointyButton(Core.Text.Browse);
        private readonly PointyButton _export = new PointyButton(Core.Text.Export, ButtonVariant.Filled);
        private readonly PointyButton _cancelButton = new PointyButton(Core.Text.Cancel);
        private readonly CheckBox _trim = new CheckBox();
        private readonly ProgressTrack _progress = new ProgressTrack();
        private readonly Label _status = new Label();
        private readonly PointyButton _openFolder = new PointyButton(Core.Text.OpenFolder, ButtonVariant.Text);

        private MountedDatabase _mounted;
        private string _mountedLabel;
        private CancellationTokenSource _cts;
        private Thread _worker;
        private bool _busy;
        private bool _rebuilding;
        private bool _outputEdited;
        private bool _closeAfterCancel;
        private string _lastOutput;

        public MainForm()
        {
            Text = Core.Text.Brand + " — " + Core.Text.AppTitle;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Page;
            ForeColor = Theme.Ink;
            RightToLeft = RightToLeft.Yes;
            StartPosition = FormStartPosition.CenterScreen;
            // Many shop PCs are 1366x768 or 1024x768: never open taller than the screen.
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Theme.S(1100), screen.Width - Theme.S(24)), Math.Min(Theme.S(740), screen.Height - Theme.S(48)));
            MinimumSize = new Size(Math.Min(Theme.S(940), screen.Width), Math.Min(Theme.S(600), screen.Height));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(Theme.S(12)),
                BackColor = Theme.Page,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Page };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(390)));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(BuildSourceCard(), 0, 0);
            body.Controls.Add(BuildTablesCard(), 1, 0);

            root.Controls.Add(body, 0, 0);
            root.Controls.Add(BuildFooter(), 0, 1);
            Controls.Add(root);
            Controls.Add(new TopBar { Text = Core.Text.Brand, Title = Core.Text.AppTitle, Trailing = "v" + SqliteExporter.ToolVersion });

            foreach (Control latin in new Control[] { _server, _user.Box, _password.Box, _file.Box, _output.Box })
                latin.RightToLeft = RightToLeft.No;

            LoadSettings();
            ApplySourceMode();
            RefreshTables();
            SetConnection(Core.Text.NotConnected, Tone.Neutral);
            SetStatus(Core.Text.Ready, Theme.MutedInk);
            UpdateButtons();
        }

        // ── State ────────────────────────────────────────────────────────

        private SourceKind Kind => (SourceKind)_source.SelectedIndex;

        private void LoadSettings()
        {
            _server.Items.Clear();
            foreach (string instance in SqlServerCatalog.LocalInstances())
                _server.Items.Add(instance);
            _server.Text = !string.IsNullOrEmpty(_settings.Server)
                ? _settings.Server
                : _server.Items.Count > 0 ? (string)_server.Items[0] : @".\SQLEXPRESS";
            _auth.SelectedIndex = _settings.SqlAuth ? 1 : 0;
            _user.Text = _settings.UserName ?? "";
            _trim.Checked = _settings.TrimFixedText;
            ApplyAuthMode();
        }

        private void SaveSettings()
        {
            _settings.Server = _server.Text.Trim();
            _settings.SqlAuth = _auth.SelectedIndex == 1;
            _settings.UserName = _user.Text.Trim();
            _settings.TrimFixedText = _trim.Checked;
            _settings.Save();
        }

        private ConnectionOptions Options() => new ConnectionOptions
        {
            Server = _server.Text.Trim(),
            WindowsAuth = _auth.SelectedIndex == 0,
            UserName = _user.Text,
            Password = _password.Text,
        };

        private void ApplyAuthMode()
        {
            bool sql = _auth.SelectedIndex == 1;
            foreach (Control c in _userRows)
                c.Visible = sql;
        }

        private void ApplySourceMode()
        {
            bool file = Kind != SourceKind.Database;
            foreach (Control c in _fileRows)
                c.Visible = file;
            foreach (Control c in _databaseRows)
                c.Visible = !file;
            _hint.Text = file ? Core.Text.FileModeHint : Core.Text.ServerModeHint;
            _file.Text = "";
        }

        private void ResetConnection()
        {
            ReleaseMount();
            _database.Items.Clear();
            _items.Clear();
            RefreshTables();
            SetConnection(Core.Text.NotConnected, Tone.Neutral);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool idle = !_busy;
            _source.Enabled = idle;
            _server.Enabled = idle;
            _auth.Enabled = idle;
            _user.Box.Enabled = idle;
            _password.Box.Enabled = idle;
            _file.Box.Enabled = idle;
            _browseFile.Enabled = idle;
            _discover.Enabled = idle;
            _database.Enabled = idle;
            _connect.Enabled = idle && _server.Text.Trim().Length > 0 && (Kind == SourceKind.Database || _file.Text.Trim().Length > 0);
            _list.Enabled = true;
            _selectAll.Enabled = idle && _items.Count > 0;
            _selectNone.Enabled = idle && _items.Count > 0;
            _output.Box.Enabled = idle;
            _browseOutput.Enabled = idle;
            _trim.Enabled = idle;
            _export.Enabled = idle && _items.Any(i => i.Checked) && _output.Text.Trim().Length > 0;
            _cancelButton.Visible = _busy;
            _cancelButton.Enabled = _busy && _cts != null && !_cts.IsCancellationRequested;
            _openFolder.Visible = idle && _lastOutput != null && File.Exists(_lastOutput);
        }

        private void SetConnection(string text, Tone tone) => _connectionChip.Set(text, tone);

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void Log(string line)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
            _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + stamped);
        }

        private void ShowError(Exception error)
        {
            string message = Core.Text.Describe(error);
            SetStatus(message, Theme.Danger);
            Log(message);
        }

        /// <summary>Runs <paramref name="work"/> off the UI thread and hands the result back on it.</summary>
        private void Background<T>(Func<T> work, Action<T> done, Action<Exception> failed)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                T result;
                try
                {
                    result = work();
                }
                catch (Exception error)
                {
                    BeginInvokeSafe(() => failed(error));
                    return;
                }
                BeginInvokeSafe(() => done(result));
            });
        }

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        // ── Connecting ───────────────────────────────────────────────────

        private void DiscoverInstances()
        {
            _discover.Enabled = false;
            _discover.Text = Core.Text.Searching;
            Background(SqlServerCatalog.NetworkInstances, found =>
            {
                foreach (string name in found)
                {
                    if (!_server.Items.Contains(name))
                        _server.Items.Add(name);
                }
                _discover.Text = Core.Text.FindOnNetwork;
                _discover.Enabled = true;
                SetStatus(found.Count == 0 ? Core.Text.NoInstancesFound : string.Format(Core.Text.InstancesFound, found.Count), Theme.MutedInk);
                if (found.Count > 0)
                    _server.DroppedDown = true;
            }, error =>
            {
                _discover.Text = Core.Text.FindOnNetwork;
                _discover.Enabled = true;
                ShowError(error);
            });
        }

        private void BrowseSourceFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = Kind == SourceKind.Backup ? Core.Text.BackupFilter : Core.Text.DataFileFilter;
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _file.Text = dialog.FileName;
            }
        }

        private void Connect()
        {
            if (_server.Text.Trim().Length == 0)
            {
                SetStatus(Core.Text.EnterServerFirst, Theme.Warning);
                return;
            }
            if (Kind != SourceKind.Database && _file.Text.Trim().Length == 0)
            {
                SetStatus(Core.Text.ChooseFileFirst, Theme.Warning);
                return;
            }

            ReleaseMount();
            _items.Clear();
            RefreshTables();
            _database.Items.Clear();
            _busy = true;
            UpdateButtons();
            SetConnection(Core.Text.Connecting, Tone.Warning);
            SaveSettings();

            ConnectionOptions options = Options();
            SourceKind kind = Kind;
            string file = _file.Text.Trim();

            if (kind == SourceKind.Database)
            {
                Background(() =>
                {
                    using (SqlConnection connection = options.Open("master"))
                        return new KeyValuePair<string, List<string>>(SqlServerCatalog.ServerVersion(connection), SqlServerCatalog.Databases(connection));
                }, result =>
                {
                    _busy = false;
                    SetConnection(Core.Text.ConnectedChip, Tone.Success);
                    Log(string.Format(Core.Text.ConnectedTo, result.Key));
                    SetStatus(string.Format(Core.Text.ConnectedTo, result.Key), Theme.Success);
                    foreach (string name in result.Value)
                        _database.Items.Add(name);
                    UpdateButtons();
                    if (_database.Items.Count > 0)
                    {
                        _database.SelectedIndex = 0;
                        _database.DroppedDown = _database.Items.Count > 1;
                    }
                }, ConnectFailed);
            }
            else
            {
                Background(() =>
                {
                    var logLines = new List<string>();
                    MountedDatabase mounted = MountedDatabase.Mount(options, kind, file, line => BeginInvokeSafe(() => Log(line)));
                    try
                    {
                        using (SqlConnection connection = options.Open(mounted.Name))
                            return new KeyValuePair<MountedDatabase, List<TableRef>>(mounted, SqlServerCatalog.Tables(connection));
                    }
                    catch
                    {
                        mounted.Dispose();
                        throw;
                    }
                }, result =>
                {
                    _busy = false;
                    _mounted = result.Key;
                    _mountedLabel = file;
                    SetConnection(Core.Text.ConnectedChip, Tone.Success);
                    SetStatus(Path.GetFileName(file), Theme.Success);
                    ShowTables(result.Value, Path.GetFileNameWithoutExtension(file));
                }, ConnectFailed);
            }
        }

        private void ConnectFailed(Exception error)
        {
            _busy = false;
            SetConnection(Core.Text.FailedChip, Tone.Danger);
            ShowError(error);
            UpdateButtons();
        }

        private void LoadTables()
        {
            string database = _database.SelectedItem as string;
            _items.Clear();
            RefreshTables();
            if (string.IsNullOrEmpty(database))
                return;

            ConnectionOptions options = Options();
            Background(() =>
            {
                using (SqlConnection connection = options.Open(database))
                    return SqlServerCatalog.Tables(connection);
            }, tables =>
            {
                if (_database.SelectedItem as string == database)
                    ShowTables(tables, database);
            }, ShowError);
        }

        private void ShowTables(List<TableRef> tables, string baseName)
        {
            _items.Clear();
            foreach (TableRef table in tables)
                _items.Add(new TableItem { Table = table, Status = Core.Text.StatusWaiting });
            RefreshTables();

            if (!_outputEdited || _output.Text.Trim().Length == 0)
            {
                string folder = !string.IsNullOrEmpty(_settings.OutputFolder) && Directory.Exists(_settings.OutputFolder)
                    ? _settings.OutputFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                _output.Text = Path.Combine(folder, SafeFileName(baseName) + "_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".db");
                _outputEdited = false;
            }
            UpdateButtons();
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length == 0 ? "export" : name;
        }

        private void ReleaseMount()
        {
            MountedDatabase mounted = _mounted;
            _mounted = null;
            _mountedLabel = null;
            if (mounted != null)
            {
                Log(Core.Text.Cleanup);
                ThreadPool.QueueUserWorkItem(_ => mounted.Dispose());
            }
        }

        // ── Table list ───────────────────────────────────────────────────

        private IEnumerable<TableItem> VisibleItems()
        {
            string filter = _search.Text.Trim();
            return filter.Length == 0
                ? _items
                : _items.Where(i => i.Table.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshTables()
        {
            _rebuilding = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (TableItem item in VisibleItems())
                {
                    var row = new ListViewItem(item.Table.Name) { Tag = item, Checked = item.Checked, UseItemStyleForSubItems = false };
                    row.SubItems.Add(item.Table.Schema).ForeColor = Theme.MutedInk;
                    row.SubItems.Add(item.Table.ApproxRows.ToString("N0", CultureInfo.InvariantCulture));
                    row.SubItems.Add(item.Status).ForeColor = item.StatusColor;
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
                _rebuilding = false;
            }
            _empty.Visible = _items.Count == 0;
            _list.Visible = _items.Count > 0;
            UpdateSummary();
            UpdateButtons();
        }

        private void UpdateSummary()
        {
            int selected = _items.Count(i => i.Checked);
            long rows = _items.Where(i => i.Checked).Sum(i => i.Table.ApproxRows);
            _summary.Text = _items.Count == 0 ? "" : string.Format(Core.Text.TablesSummary, selected, _items.Count, rows.ToString("N0", CultureInfo.InvariantCulture));
        }

        private void SetVisibleChecked(bool value)
        {
            foreach (TableItem item in VisibleItems())
                item.Checked = value;
            RefreshTables();
        }

        private void SetItemStatus(TableItem item, string status, Color color)
        {
            item.Status = status;
            item.StatusColor = color;
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag == item)
                {
                    row.SubItems[3].Text = status;
                    row.SubItems[3].ForeColor = color;
                    break;
                }
            }
        }

        // ── Export ───────────────────────────────────────────────────────

        private void BrowseOutput()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = Core.Text.SqliteFilter;
                dialog.OverwritePrompt = false; // asked once, at export time
                string current = _output.Text.Trim();
                if (current.Length > 0)
                {
                    try
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(current);
                        dialog.FileName = Path.GetFileName(current);
                    }
                    catch (ArgumentException) { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _output.Text = dialog.FileName;
                    _outputEdited = true;
                }
            }
        }

        private void StartExport()
        {
            string database = Kind == SourceKind.Database ? _database.SelectedItem as string : _mounted?.Name;
            if (string.IsNullOrEmpty(database))
            {
                SetStatus(Kind == SourceKind.Database ? Core.Text.ChooseDatabaseFirst : Core.Text.ChooseFileFirst, Theme.Warning);
                return;
            }
            List<TableItem> chosen = _items.Where(i => i.Checked).ToList();
            if (chosen.Count == 0)
            {
                SetStatus(Core.Text.ChooseTablesFirst, Theme.Warning);
                return;
            }
            string output = _output.Text.Trim();
            if (output.Length == 0)
            {
                SetStatus(Core.Text.ChooseOutputFirst, Theme.Warning);
                return;
            }
            if (File.Exists(output) && MessageBox.Show(this, Core.Text.ConfirmOverwrite, Core.Text.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading) != DialogResult.Yes)
                return;

            foreach (TableItem item in _items)
                SetItemStatus(item, item.Checked ? Core.Text.StatusWaiting : "", Theme.MutedInk);

            var request = new ExportRequest
            {
                Connection = Options(),
                Database = database,
                SourceLabel = _mountedLabel,
                Tables = chosen.Select(i => i.Table).ToList(),
                OutputPath = output,
                TrimFixedText = _trim.Checked,
            };
            var byTable = chosen.ToDictionary(i => i.Table);
            long expected = Math.Max(1, chosen.Sum(i => i.Table.ApproxRows));
            long finishedRows = 0;
            int finishedTables = 0;

            _busy = true;
            _lastOutput = null;
            _progress.Value = 0;
            UpdateButtons();
            SaveSettings();
            try { _settings.OutputFolder = Path.GetDirectoryName(Path.GetFullPath(output)); _settings.Save(); } catch (Exception) { }
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            Action<ExportEvent> report = ev => BeginInvokeSafe(() =>
            {
                TableItem item = ev.Table != null && byTable.TryGetValue(ev.Table, out TableItem found) ? found : null;
                switch (ev.Kind)
                {
                    case ExportEventKind.Log:
                        Log(ev.Message);
                        break;
                    case ExportEventKind.Warning:
                        Log("⚠ " + ev.Message);
                        break;
                    case ExportEventKind.TableStarted:
                        SetItemStatus(item, string.Format(Core.Text.StatusCopying, "0"), Theme.Warning);
                        SetStatus(string.Format(Core.Text.Exporting, ev.Table.FullName, ev.TableIndex + 1, chosen.Count), Theme.Ink);
                        break;
                    case ExportEventKind.Rows:
                        SetItemStatus(item, string.Format(Core.Text.StatusCopying, ev.Rows.ToString("N0", CultureInfo.InvariantCulture)), Theme.Warning);
                        _progress.Value = Progress(finishedRows + ev.Rows, expected, finishedTables, chosen.Count);
                        break;
                    case ExportEventKind.TableFinished:
                        finishedRows += ev.Rows;
                        finishedTables++;
                        SetItemStatus(item, string.Format(Core.Text.StatusDone, ev.Rows.ToString("N0", CultureInfo.InvariantCulture)), Theme.Success);
                        Log(string.Format(Core.Text.TableCopied, ev.Table.FullName, ev.Rows.ToString("N0", CultureInfo.InvariantCulture)));
                        _progress.Value = Progress(finishedRows, expected, finishedTables, chosen.Count);
                        break;
                }
            });

            _worker = new Thread(() =>
            {
                try
                {
                    ExportResult result = new SqliteExporter(report).Run(request, token);
                    BeginInvokeSafe(() => ExportFinished(result, null, chosen));
                }
                catch (Exception error)
                {
                    BeginInvokeSafe(() => ExportFinished(null, error, chosen));
                }
            })
            { IsBackground = true, Name = "export" };
            _worker.Start();
        }

        /// <summary>Row-weighted, but never behind the share of tables already done (row counts are estimates).</summary>
        private static double Progress(long rows, long expected, int tablesDone, int tables) =>
            Math.Min(0.99, Math.Max((double)rows / expected, (double)tablesDone / tables));

        private void ExportFinished(ExportResult result, Exception error, List<TableItem> chosen)
        {
            _busy = false;
            _worker = null;
            if (result != null)
            {
                _progress.Value = 1;
                _lastOutput = result.OutputPath;
                string message = string.Format(Core.Text.ExportDone, result.Tables, result.TotalRows.ToString("N0", CultureInfo.InvariantCulture), FormatElapsed(result.Elapsed));
                if (result.Warnings > 0)
                    message += string.Format(Core.Text.ExportDoneWarnings, result.Warnings);
                SetStatus(message, Theme.Success);
                Log(message);
                Log(result.OutputPath);
            }
            else
            {
                _progress.Value = 0;
                foreach (TableItem item in chosen)
                {
                    if (item.Status.StartsWith(Core.Text.StatusCopying.Split('…')[0], StringComparison.Ordinal))
                        SetItemStatus(item, Core.Text.StatusFailed, Theme.Danger);
                }
                if (error is OperationCanceledException)
                {
                    SetStatus(Core.Text.Cancelled, Theme.Warning);
                    Log(Core.Text.Cancelled);
                }
                else
                {
                    string message = string.Format(Core.Text.ExportFailed, Core.Text.Describe(error));
                    SetStatus(message, Theme.Danger);
                    Log(message);
                }
            }
            UpdateButtons();
            if (_closeAfterCancel)
                Close();
        }

        private static string FormatElapsed(TimeSpan elapsed) =>
            elapsed.TotalHours >= 1
                ? ((int)elapsed.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + elapsed.ToString("mm\\:ss", CultureInfo.InvariantCulture)
                : elapsed.ToString("mm\\:ss", CultureInfo.InvariantCulture);

        private void CancelExport()
        {
            if (_cts == null || !_busy)
                return;
            _cts.Cancel();
            _cancelButton.Enabled = false;
            SetStatus(Core.Text.Cancelling, Theme.Warning);
        }

        private void OpenOutputFolder()
        {
            if (_lastOutput == null)
                return;
            try { Process.Start("explorer.exe", "/select,\"" + _lastOutput + "\""); } catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_forceClose)
            {
                base.OnFormClosing(e);
                return;
            }
            if (_busy && _worker != null)
            {
                if (MessageBox.Show(this, Core.Text.ConfirmQuit, Core.Text.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _closeAfterCancel = true;
                _cts?.Cancel();
                e.Cancel = true;
                return;
            }
            SaveSettings();
            MountedDatabase mounted = _mounted;
            _mounted = null;
            mounted?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
