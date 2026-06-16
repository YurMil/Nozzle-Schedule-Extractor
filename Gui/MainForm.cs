using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NozzleScheduleExtractor
{
    internal sealed class MainForm : Form
    {
        private const string DefaultPython = @"C:\Users\Yurii.Milienin\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe";

        private readonly TextBox _folderBox = new TextBox();
        private readonly TextBox _pdfBox = new TextBox();
        private readonly TextBox _prefixBox = new TextBox();
        private readonly TextBox _logBox = new TextBox();
        private readonly TextBox _xlsxBox = new TextBox();
        private readonly Button _folderButton = new Button();
        private readonly Button _pdfButton = new Button();
        private readonly Button _runFolderButton = new Button();
        private readonly Button _runPdfButton = new Button();
        private readonly Button _openExcelButton = new Button();
        private readonly Button _insertSolidWorksButton = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _statusLabel = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private List<NozzleRow> _lastRows = new List<NozzleRow>();

        public MainForm()
        {
            Text = "Nozzle Schedule Extractor";
            MinimumSize = new Size(1180, 720);
            Size = new Size(1280, 780);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();
            _folderBox.Text = @"C:\wt\Nozzle List\Test folder 01";
            _prefixBox.Text = "W";
            SetBusy(false);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            Controls.Add(root);

            var inputs = new TableLayoutPanel();
            inputs.Dock = DockStyle.Fill;
            inputs.Padding = new Padding(8);
            inputs.ColumnCount = 5;
            inputs.RowCount = 3;
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            inputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            root.Controls.Add(inputs, 0, 0);

            AddLabel(inputs, "Folder", 0, 0);
            _folderBox.Dock = DockStyle.Fill;
            inputs.Controls.Add(_folderBox, 1, 0);
            _folderButton.Text = "Browse...";
            _folderButton.Dock = DockStyle.Fill;
            _folderButton.Click += BrowseFolder;
            inputs.Controls.Add(_folderButton, 2, 0);
            AddLabel(inputs, "Prefix", 3, 0);
            _prefixBox.Dock = DockStyle.Fill;
            inputs.Controls.Add(_prefixBox, 4, 0);

            AddLabel(inputs, "PDF", 0, 1);
            _pdfBox.Dock = DockStyle.Fill;
            inputs.SetColumnSpan(_pdfBox, 1);
            inputs.Controls.Add(_pdfBox, 1, 1);
            _pdfButton.Text = "Browse PDF...";
            _pdfButton.Dock = DockStyle.Fill;
            _pdfButton.Click += BrowsePdf;
            inputs.Controls.Add(_pdfButton, 2, 1);
            _runFolderButton.Text = "Run Folder";
            _runFolderButton.Dock = DockStyle.Fill;
            _runFolderButton.Click += RunFolder;
            inputs.Controls.Add(_runFolderButton, 3, 1);
            _runPdfButton.Text = "Run PDF";
            _runPdfButton.Dock = DockStyle.Fill;
            _runPdfButton.Click += RunPdf;
            inputs.Controls.Add(_runPdfButton, 4, 1);

            AddLabel(inputs, "Excel", 0, 2);
            _xlsxBox.Dock = DockStyle.Fill;
            _xlsxBox.ReadOnly = true;
            inputs.Controls.Add(_xlsxBox, 1, 2);
            inputs.SetColumnSpan(_xlsxBox, 3);
            _openExcelButton.Text = "Open Excel";
            _openExcelButton.Dock = DockStyle.Fill;
            _openExcelButton.Click += OpenExcel;
            inputs.Controls.Add(_openExcelButton, 3, 2);
            _insertSolidWorksButton.Text = "Insert SW";
            _insertSolidWorksButton.Dock = DockStyle.Fill;
            _insertSolidWorksButton.MinimumSize = new Size(0, 30);
            _insertSolidWorksButton.Click += InsertSolidWorks;
            inputs.Controls.Add(_insertSolidWorksButton, 4, 2);

            var statusPanel = new TableLayoutPanel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.ColumnCount = 2;
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            statusPanel.Padding = new Padding(8, 0, 8, 0);
            root.Controls.Add(statusPanel, 0, 1);

            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusPanel.Controls.Add(_statusLabel, 0, 0);
            _progress.Dock = DockStyle.Fill;
            statusPanel.Controls.Add(_progress, 1, 0);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = Color.White;
            root.Controls.Add(_grid, 0, 2);

            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.ReadOnly = true;
            _logBox.Font = new Font("Consolas", 9);
            root.Controls.Add(_logBox, 0, 3);
        }

        private static void AddLabel(TableLayoutPanel panel, string text, int column, int row)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(label, column, row);
        }

        private void BrowseFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(_folderBox.Text) ? _folderBox.Text : @"C:\wt\Nozzle List";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _folderBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowsePdf(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "PDF reports (*.pdf)|*.pdf|All files (*.*)|*.*";
                dialog.InitialDirectory = Directory.Exists(_folderBox.Text) ? _folderBox.Text : @"C:\wt\Nozzle List";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pdfBox.Text = dialog.FileName;
                    _folderBox.Text = Path.GetDirectoryName(dialog.FileName);
                }
            }
        }

        private void RunFolder(object sender, EventArgs e)
        {
            string folder = _folderBox.Text.Trim();
            string prefix = _prefixBox.Text.Trim();
            RunAsync(delegate(Action<string> log) { return ExtractionService.RunFolder(folder, prefix, DefaultPython, log); });
        }

        private void RunPdf(object sender, EventArgs e)
        {
            string pdf = _pdfBox.Text.Trim();
            RunAsync(delegate(Action<string> log) { return ExtractionService.RunPdf(pdf, DefaultPython, log); });
        }

        private async void RunAsync(Func<Action<string>, ExtractionResult> action)
        {
            SetBusy(true);
            _lastRows = new List<NozzleRow>();
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            _xlsxBox.Text = "";
            _logBox.Clear();
            AppendLog("Started: " + DateTime.Now);

            try
            {
                ExtractionResult result = await Task.Factory.StartNew(delegate
                {
                    return action(AppendLogThreadSafe);
                });

                _xlsxBox.Text = result.XlsxPath;
                _lastRows = result.Rows;
                PopulateGrid(result.Rows);
                _statusLabel.Text = "Done: " + result.Rows.Count + " rows. Excel created.";
                AppendLog("Excel: " + result.XlsxPath);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error";
                AppendLog("ERROR: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Extraction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PopulateGrid(List<NozzleRow> rows)
        {
            _grid.Columns.Clear();
            foreach (string header in NozzleColumns.Headers)
                _grid.Columns.Add(header, header);
            foreach (NozzleRow row in rows)
                _grid.Rows.Add(TsvWriter.ToCells(row));
            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        }

        private void OpenExcel(object sender, EventArgs e)
        {
            if (!File.Exists(_xlsxBox.Text))
                return;
            Process.Start(new ProcessStartInfo { FileName = _xlsxBox.Text, UseShellExecute = true });
        }

        private void InsertSolidWorks(object sender, EventArgs e)
        {
            if (_lastRows == null || _lastRows.Count == 0)
            {
                MessageBox.Show(this, "Run extraction first. The SolidWorks table is inserted from the current preview.", "No table data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                AppendLog("Connecting to active SOLIDWORKS drawing...");
                string message = SolidWorksTableInserter.InsertIntoActiveDrawing(_lastRows);
                AppendLog(message);
                _statusLabel.Text = "Inserted into SOLIDWORKS drawing.";
            }
            catch (Exception ex)
            {
                AppendLog("SOLIDWORKS ERROR: " + ex.Message);
                MessageBox.Show(this, ex.Message, "SOLIDWORKS insert failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy)
        {
            _folderButton.Enabled = !busy;
            _pdfButton.Enabled = !busy;
            _runFolderButton.Enabled = !busy;
            _runPdfButton.Enabled = !busy;
            _openExcelButton.Enabled = !busy && File.Exists(_xlsxBox.Text);
            _insertSolidWorksButton.Enabled = !busy && _lastRows != null && _lastRows.Count > 0;
            _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            _statusLabel.Text = busy ? "Working..." : (_statusLabel.Text.Length == 0 ? "Ready" : _statusLabel.Text);
        }

        private void AppendLogThreadSafe(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            AppendLog(message);
        }

        private void AppendLog(string message)
        {
            _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
    }
}
