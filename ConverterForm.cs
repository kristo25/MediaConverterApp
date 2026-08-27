using System.Diagnostics;
using System.Text.Json;

namespace MediaConverter;

internal sealed partial class ConverterForm : Form
{
    private const string DefaultTheme = "Abyss";
    private static readonly HashSet<string> SupportedInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".mp4", ".ogg", ".wav", ".flac", ".aac", ".wma", ".webm", ".mkv", ".mov", ".avi"
    };

    private readonly Dictionary<string, ThemeSpec> themes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abyss"] = new(Color.FromArgb(14, 13, 18), Color.FromArgb(25, 22, 33), Color.FromArgb(38, 34, 50), Color.FromArgb(137, 76, 255), Color.FromArgb(78, 42, 153), Color.FromArgb(238, 54, 77), Color.FromArgb(240, 237, 247), Color.FromArgb(169, 158, 187)),
        ["Royal"] = new(Color.FromArgb(10, 10, 24), Color.FromArgb(22, 20, 43), Color.FromArgb(35, 31, 62), Color.FromArgb(167, 97, 255), Color.FromArgb(93, 68, 196), Color.FromArgb(255, 61, 105), Color.FromArgb(244, 241, 255), Color.FromArgb(179, 171, 205)),
        ["Ember"] = new(Color.FromArgb(16, 11, 13), Color.FromArgb(31, 20, 26), Color.FromArgb(48, 31, 39), Color.FromArgb(190, 82, 255), Color.FromArgb(116, 43, 164), Color.FromArgb(255, 72, 57), Color.FromArgb(255, 241, 243), Color.FromArgb(202, 165, 176)),
        ["Neon"] = new(Color.FromArgb(7, 9, 15), Color.FromArgb(16, 22, 34), Color.FromArgb(26, 35, 52), Color.FromArgb(197, 82, 255), Color.FromArgb(76, 91, 230), Color.FromArgb(255, 42, 85), Color.FromArgb(239, 246, 255), Color.FromArgb(151, 168, 196))
    };

    private readonly List<QueueItem> queue = [];
    private readonly AppSettings settings;
    private readonly string settingsPath;
    private ThemeSpec currentTheme;

    private readonly DataGridView grid = new();
    private readonly ComboBox formatCombo = new();
    private readonly ComboBox themeCombo = new();
    private readonly ComboBox qualityCombo = new();
    private readonly ComboBox namingCombo = new();
    private readonly ComboBox recentFolderCombo = new();
    private readonly CheckBox includeSubfoldersCheck = new();
    private readonly CheckBox overwriteCheck = new();
    private readonly CheckBox deleteOriginalsCheck = new();
    private readonly CheckBox useOutputFolderCheck = new();
    private readonly CheckBox preserveFoldersCheck = new();
    private readonly TextBox outputFolderText = new();
    private readonly TextBox ffmpegPathText = new();
    private readonly TextBox customAudioText = new();
    private readonly Button browseOutputButton = new();
    private readonly Button browseFfmpegButton = new();
    private readonly Button convertButton = new();
    private readonly Button cancelButton = new();
    private readonly Button pauseButton = new();
    private readonly Button openLogButton = new();
    private readonly Button clearConvertedButton = new();
    private readonly Button helpButton = new();
    private readonly Panel summaryPanel = new();
    private readonly Label summaryLabel = new();
    private readonly Button openLastLogButton = new();
    private readonly Button retryFailedButton = new();
    private readonly Button summaryClearConvertedButton = new();
    private readonly Label statusLabel = new();
    private readonly ProgressBar progressBar = new();
    private readonly Button installFfmpegButton = new();

    private CancellationTokenSource? conversionCancellation;
    private Process? activeProcess;
    private string? lastLogPath;
    private bool isConverting;
    private bool pauseRequested;

    public ConverterForm()
    {
        settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaConverter", "settings.json");
        settings = LoadSettings(settingsPath);
        currentTheme = themes.TryGetValue(settings.Theme, out var theme) ? theme : themes[DefaultTheme];

        Text = "Media Converter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 760);
        Size = new Size(1280, 860);
        BackColor = currentTheme.Black;
        ForeColor = currentTheme.Text;
        Font = new Font("Segoe UI", 10f);

        BuildUi();
        ApplySettingsToControls();
        RefreshRecentFolders();
        RefreshGrid();
        UpdateFfmpegStatus();
        FormClosing += (_, _) => SaveSettingsFromControls();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = currentTheme.Black, Padding = new Padding(18, 16, 18, 12) };
        var title = new Label { Text = "Media Converter", AutoSize = false, Height = 36, Dock = DockStyle.Top, Font = new Font("Segoe UI Semibold", 20f), ForeColor = currentTheme.Text };
        var subtitle = new Label { Text = "Drop audio or video files. Convert to MP3, WAV, OGG, FLAC, or M4A with safe logs and queue controls.", AutoSize = false, Height = 28, Dock = DockStyle.Top, ForeColor = currentTheme.Muted };
        var themeLabel = new Label { Text = "THEME", Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(792, 21), Size = new Size(76, 28), Font = new Font("Segoe UI Semibold", 10f), ForeColor = currentTheme.Text, TextAlign = ContentAlignment.MiddleRight };

        themeCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.Items.AddRange(themes.Keys.OrderBy(name => name).Cast<object>().ToArray());
        themeCombo.Location = new Point(878, 16);
        themeCombo.Size = new Size(178, 36);
        themeCombo.Font = new Font("Segoe UI Semibold", 11f);
        themeCombo.FlatStyle = FlatStyle.Flat;
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is string selected && themes.TryGetValue(selected, out var nextTheme))
            {
                currentTheme = nextTheme;
                settings.Theme = selected;
                ApplyTheme();
                SaveSettingsFromControls();
            }
        };

        var accent = new Panel { Dock = DockStyle.Bottom, Height = 4, BackColor = currentTheme.Primary };
        header.Controls.Add(accent);
        header.Controls.Add(themeCombo);
        header.Controls.Add(themeLabel);
        header.Controls.Add(subtitle);
        header.Controls.Add(title);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = currentTheme.Black, Padding = new Padding(18), RowCount = 7, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var dropPanel = new Panel { Dock = DockStyle.Fill, BackColor = currentTheme.Panel, AllowDrop = true, Padding = new Padding(12) };
        dropPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(currentTheme.Primary, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var rect = dropPanel.ClientRectangle;
            rect.Inflate(-2, -2);
            e.Graphics.DrawRectangle(pen, rect);
        };
        dropPanel.Controls.Add(new Label { Text = "Drop files or folders here", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 18f), ForeColor = currentTheme.Text });
        dropPanel.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        dropPanel.DragDrop += (_, e) =>
        {
            var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (paths is { Length: > 0 })
            {
                AddPaths(paths);
            }
        };

        var controls = BuildControlsPanel();
        var hintLabel = new Label { Dock = DockStyle.Fill, Text = "Queue actions: right-click rows to remove, retry, or open source/output folders. Logs are saved under LocalAppData.", ForeColor = currentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        StyleGrid();
        ConfigureGridContextMenu();

        var bottom = BuildBottomPanel();
        var summary = BuildSummaryPanel();
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = currentTheme.Muted;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = currentTheme.Black };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        installFfmpegButton.Text = "Install ffmpeg";
        StyleButton(installFfmpegButton, currentTheme.Primary);
        installFfmpegButton.Visible = false;
        installFfmpegButton.Click += async (_, _) => await InstallFfmpegAsync();
        statusRow.Controls.Add(statusLabel, 0, 0);
        statusRow.Controls.Add(installFfmpegButton, 1, 0);

        main.Controls.Add(dropPanel, 0, 0);
        main.Controls.Add(controls, 0, 1);
        main.Controls.Add(hintLabel, 0, 2);
        main.Controls.Add(grid, 0, 3);
        main.Controls.Add(summary, 0, 4);
        main.Controls.Add(bottom, 0, 5);
        main.Controls.Add(statusRow, 0, 6);

        Controls.Add(main);
        Controls.Add(header);
    }

    private TableLayoutPanel BuildControlsPanel()
    {
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, RowCount = 3, BackColor = currentTheme.Black };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var addFiles = StyledButton("Add files", currentTheme.Primary);
        addFiles.Click += (_, _) => AddFiles();
        var addFolder = StyledButton("Add folder", currentTheme.Secondary);
        addFolder.Click += (_, _) => AddFolder();
        var clear = StyledButton("Clear", currentTheme.Danger);
        clear.Click += (_, _) => { queue.Clear(); progressBar.Value = 0; RefreshGrid(); };

        ConfigureCombo(formatCombo, ["mp3", "wav", "ogg", "flac", "m4a"]);
        ConfigureCombo(qualityCombo, ["Small file", "Balanced", "High quality", "Custom"]);
        ConfigureCombo(namingCombo, ["Same name", "Append _converted", "Auto-number conflicts", "VRC-safe filename"]);
        ConfigureCombo(recentFolderCombo, []);

        formatCombo.SelectedIndexChanged += (_, _) => { RefreshGrid(); SaveSettingsFromControls(); };
        qualityCombo.SelectedIndexChanged += (_, _) => { customAudioText.Enabled = qualityCombo.SelectedItem?.ToString() == "Custom"; RefreshGrid(); SaveSettingsFromControls(); };
        namingCombo.SelectedIndexChanged += (_, _) => { RefreshGrid(); SaveSettingsFromControls(); };
        recentFolderCombo.SelectedIndexChanged += (_, _) =>
        {
            if (recentFolderCombo.SelectedItem is string folder && Directory.Exists(folder))
            {
                AddPaths([folder]);
            }
        };

        StyleTextBox(customAudioText);
        customAudioText.Enabled = false;
        customAudioText.TextChanged += (_, _) => SaveSettingsFromControls();

        includeSubfoldersCheck.Text = "Include subfolders";
        overwriteCheck.Text = "Overwrite";
        deleteOriginalsCheck.Text = "Delete originals after success";
        useOutputFolderCheck.Text = "Use one output folder";
        preserveFoldersCheck.Text = "Preserve folders";
        foreach (var check in new[] { includeSubfoldersCheck, overwriteCheck, deleteOriginalsCheck, useOutputFolderCheck, preserveFoldersCheck })
        {
            StyleCheck(check);
        }
        includeSubfoldersCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        overwriteCheck.CheckedChanged += (_, _) => { RefreshGrid(); SaveSettingsFromControls(); };
        deleteOriginalsCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        useOutputFolderCheck.CheckedChanged += (_, _) =>
        {
            outputFolderText.Enabled = useOutputFolderCheck.Checked;
            browseOutputButton.Enabled = useOutputFolderCheck.Checked;
            preserveFoldersCheck.Enabled = useOutputFolderCheck.Checked;
            RefreshGrid();
            SaveSettingsFromControls();
        };
        preserveFoldersCheck.CheckedChanged += (_, _) => { RefreshGrid(); SaveSettingsFromControls(); };

        StyleTextBox(outputFolderText);
        outputFolderText.Enabled = false;
        outputFolderText.TextChanged += (_, _) => { RefreshGrid(); SaveSettingsFromControls(); };
        StyleTextBox(ffmpegPathText);
        ffmpegPathText.TextChanged += (_, _) => { UpdateFfmpegStatus(); SaveSettingsFromControls(); };

        browseOutputButton.Text = "Output";
        StyleButton(browseOutputButton, currentTheme.Secondary);
        browseOutputButton.Enabled = false;
        browseOutputButton.Click += (_, _) => ChooseOutputFolder();
        browseFfmpegButton.Text = "ffmpeg";
        StyleButton(browseFfmpegButton, currentTheme.Secondary);
        browseFfmpegButton.Click += (_, _) => ChooseFfmpegPath();

        controls.Controls.Add(addFiles, 0, 0);
        controls.Controls.Add(addFolder, 1, 0);
        controls.Controls.Add(clear, 2, 0);
        controls.Controls.Add(ThemedLabel("Format", ContentAlignment.MiddleRight), 3, 0);
        controls.Controls.Add(formatCombo, 4, 0);
        controls.Controls.Add(ThemedLabel("Quality", ContentAlignment.MiddleRight), 5, 0);
        controls.Controls.Add(qualityCombo, 6, 0);
        controls.Controls.Add(customAudioText, 7, 0);
        controls.Controls.Add(browseOutputButton, 8, 0);
        controls.Controls.Add(browseFfmpegButton, 9, 0);
        controls.Controls.Add(overwriteCheck, 0, 1);
        controls.Controls.Add(deleteOriginalsCheck, 1, 1);
        controls.SetColumnSpan(deleteOriginalsCheck, 3);
        controls.Controls.Add(includeSubfoldersCheck, 4, 1);
        controls.Controls.Add(useOutputFolderCheck, 5, 1);
        controls.Controls.Add(preserveFoldersCheck, 6, 1);
        controls.Controls.Add(outputFolderText, 7, 1);
        controls.SetColumnSpan(outputFolderText, 3);
        controls.Controls.Add(ThemedLabel("Naming", ContentAlignment.MiddleRight), 0, 2);
        controls.Controls.Add(namingCombo, 1, 2);
        controls.SetColumnSpan(namingCombo, 2);
        controls.Controls.Add(ThemedLabel("Recent", ContentAlignment.MiddleRight), 3, 2);
        controls.Controls.Add(recentFolderCombo, 4, 2);
        controls.SetColumnSpan(recentFolderCombo, 3);
        controls.Controls.Add(ffmpegPathText, 7, 2);
        controls.SetColumnSpan(ffmpegPathText, 3);
        return controls;
    }

    private TableLayoutPanel BuildBottomPanel()
    {
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, BackColor = currentTheme.Black };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Continuous;
        convertButton.Text = "Convert";
        StyleButton(convertButton, currentTheme.Danger);
        convertButton.Click += async (_, _) => await ConvertQueueAsync();
        pauseButton.Text = "Pause";
        StyleButton(pauseButton, currentTheme.Secondary);
        pauseButton.Enabled = false;
        pauseButton.Click += (_, _) => { pauseRequested = !pauseRequested; pauseButton.Text = pauseRequested ? "Resume" : "Pause"; statusLabel.Text = pauseRequested ? "Pause requested. Current file will finish first." : "Resuming queue."; };
        cancelButton.Text = "Cancel";
        StyleButton(cancelButton, currentTheme.Danger);
        cancelButton.Enabled = false;
        cancelButton.Click += (_, _) => CancelConversion();
        openLogButton.Text = "Open logs";
        StyleButton(openLogButton, currentTheme.Secondary);
        openLogButton.Click += (_, _) => OpenLogsFolder();
        clearConvertedButton.Text = "Clear converted";
        StyleButton(clearConvertedButton, currentTheme.Secondary);
        clearConvertedButton.Click += (_, _) => ClearConvertedItems();
        helpButton.Text = "Help";
        StyleButton(helpButton, currentTheme.Secondary);
        helpButton.Click += (_, _) => ShowHelpDialog();
        bottom.Controls.Add(progressBar, 0, 0);
        bottom.Controls.Add(convertButton, 1, 0);
        bottom.Controls.Add(pauseButton, 2, 0);
        bottom.Controls.Add(cancelButton, 3, 0);
        bottom.Controls.Add(openLogButton, 4, 0);
        bottom.Controls.Add(clearConvertedButton, 5, 0);
        bottom.Controls.Add(helpButton, 6, 0);
        return bottom;
    }

    private Panel BuildSummaryPanel()
    {
        summaryPanel.Dock = DockStyle.Fill;
        summaryPanel.BackColor = currentTheme.Panel;
        summaryPanel.Padding = new Padding(12, 8, 12, 8);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = currentTheme.Panel };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));

        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.Text = "Last run summary will appear here after conversion.";
        summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        summaryLabel.ForeColor = currentTheme.Muted;

        openLastLogButton.Text = "Open last log";
        StyleButton(openLastLogButton, currentTheme.Secondary);
        openLastLogButton.Enabled = false;
        openLastLogButton.Click += (_, _) => OpenLastLog();

        retryFailedButton.Text = "Retry failed";
        StyleButton(retryFailedButton, currentTheme.Secondary);
        retryFailedButton.Enabled = false;
        retryFailedButton.Click += (_, _) => RetryFailedItems();

        summaryClearConvertedButton.Text = "Clear converted";
        StyleButton(summaryClearConvertedButton, currentTheme.Secondary);
        summaryClearConvertedButton.Enabled = false;
        summaryClearConvertedButton.Click += (_, _) => ClearConvertedItems();

        layout.Controls.Add(summaryLabel, 0, 0);
        layout.Controls.Add(openLastLogButton, 1, 0);
        layout.Controls.Add(retryFailedButton, 2, 0);
        layout.Controls.Add(summaryClearConvertedButton, 3, 0);
        summaryPanel.Controls.Add(layout);
        return summaryPanel;
    }

    private Label ThemedLabel(string text, ContentAlignment alignment) => new() { Text = text, ForeColor = currentTheme.Muted, TextAlign = alignment, Dock = DockStyle.Fill };

    private void ConfigureCombo(ComboBox combo, object[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.Clear();
        combo.Items.AddRange(items);
        combo.BackColor = currentTheme.Surface;
        combo.ForeColor = currentTheme.Text;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Dock = DockStyle.Fill;
    }

    private void StyleTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = currentTheme.Surface;
        box.ForeColor = currentTheme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private void ApplySettingsToControls()
    {
        themeCombo.SelectedItem = themes.ContainsKey(settings.Theme) ? settings.Theme : DefaultTheme;
        formatCombo.SelectedItem = formatCombo.Items.Contains(settings.OutputFormat) ? settings.OutputFormat : "mp3";
        qualityCombo.SelectedItem = qualityCombo.Items.Contains(settings.QualityPreset) ? settings.QualityPreset : "Balanced";
        namingCombo.SelectedItem = namingCombo.Items.Contains(settings.NamingRule) ? settings.NamingRule : "Same name";
        customAudioText.Text = string.IsNullOrWhiteSpace(settings.CustomAudioValue) ? "192k" : settings.CustomAudioValue;
        includeSubfoldersCheck.Checked = settings.IncludeSubfolders;
        overwriteCheck.Checked = settings.Overwrite;
        deleteOriginalsCheck.Checked = settings.DeleteOriginals;
        useOutputFolderCheck.Checked = settings.UseOutputFolder;
        preserveFoldersCheck.Checked = settings.PreserveFoldersInOutput;
        outputFolderText.Text = settings.LastOutputFolder;
        ffmpegPathText.Text = settings.FfmpegPath;
        outputFolderText.Enabled = useOutputFolderCheck.Checked;
        browseOutputButton.Enabled = useOutputFolderCheck.Checked;
        preserveFoldersCheck.Enabled = useOutputFolderCheck.Checked;
        customAudioText.Enabled = qualityCombo.SelectedItem?.ToString() == "Custom";
    }

    private void SaveSettingsFromControls()
    {
        try
        {
            settings.Theme = themeCombo.SelectedItem?.ToString() ?? DefaultTheme;
            settings.OutputFormat = formatCombo.SelectedItem?.ToString() ?? "mp3";
            settings.QualityPreset = qualityCombo.SelectedItem?.ToString() ?? "Balanced";
            settings.CustomAudioValue = customAudioText.Text.Trim();
            settings.NamingRule = namingCombo.SelectedItem?.ToString() ?? "Same name";
            settings.FfmpegPath = ffmpegPathText.Text.Trim();
            settings.LastOutputFolder = outputFolderText.Text.Trim();
            settings.IncludeSubfolders = includeSubfoldersCheck.Checked;
            settings.Overwrite = overwriteCheck.Checked;
            settings.DeleteOriginals = deleteOriginalsCheck.Checked;
            settings.UseOutputFolder = useOutputFolderCheck.Checked;
            settings.PreserveFoldersInOutput = preserveFoldersCheck.Checked;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            // Settings are convenience-only; conversion should keep working if persistence fails.
            Logger.Warn("SaveSettingsFromControls", ex);
        }
    }

    private static AppSettings LoadSettings(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex)
        {
            Logger.Warn("LoadSettings", ex);
            return new AppSettings();
        }
    }

    private Button StyledButton(string label, Color color)
    {
        var button = new Button { Text = label, Dock = DockStyle.Fill };
        StyleButton(button, color);
        return button;
    }

    private void StyleButton(Button button, Color color)
    {
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 10f);
        button.Margin = new Padding(4);
        button.Cursor = Cursors.Hand;
    }

    private void StyleCheck(CheckBox check)
    {
        check.Dock = DockStyle.Fill;
        check.ForeColor = currentTheme.Text;
        check.BackColor = currentTheme.Black;
        check.Margin = new Padding(4);
    }

    private void StyleGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = currentTheme.Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Blend(currentTheme.Surface, currentTheme.Primary, 0.35);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = currentTheme.Secondary;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10f);
        grid.DefaultCellStyle.BackColor = currentTheme.Surface;
        grid.DefaultCellStyle.ForeColor = currentTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = Blend(currentTheme.Secondary, currentTheme.Primary, 0.35);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Blend(currentTheme.Surface, currentTheme.Panel, 0.50);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        if (grid.Columns.Count == 0)
        {
            grid.Columns.Add("Status", "Status");
            grid.Columns.Add("Duration", "Duration");
            grid.Columns.Add("Source", "Source");
            grid.Columns.Add("Destination", "Destination");
            grid.Columns.Add("Message", "Message");
            grid.Columns["Status"]!.FillWeight = 14;
            grid.Columns["Duration"]!.FillWeight = 12;
            grid.Columns["Source"]!.FillWeight = 42;
            grid.Columns["Destination"]!.FillWeight = 42;
            grid.Columns["Message"]!.FillWeight = 34;
        }
    }

    private void ConfigureGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Remove selected", null, (_, _) => RemoveSelectedRows());
        menu.Items.Add("Retry failed/skipped", null, (_, _) => RetrySelectedRows());
        menu.Items.Add("Open source folder", null, (_, _) => OpenSelectedFolder(source: true));
        menu.Items.Add("Open output folder", null, (_, _) => OpenSelectedFolder(source: false));
        menu.Items.Add("Clear converted", null, (_, _) => ClearConvertedItems());
        grid.ContextMenuStrip = menu;
    }

    private void ApplyTheme()
    {
        BackColor = currentTheme.Black;
        ForeColor = currentTheme.Text;
        ApplyThemeToControls(Controls);
        StyleGrid();
        grid.Invalidate();
    }

    private void ApplyThemeToControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button, GetButtonColor(button));
                    break;
                case CheckBox check:
                    StyleCheck(check);
                    break;
                case ComboBox combo:
                    combo.BackColor = currentTheme.Surface;
                    combo.ForeColor = currentTheme.Text;
                    break;
                case TextBox box:
                    box.BackColor = currentTheme.Surface;
                    box.ForeColor = currentTheme.Text;
                    break;
                case DataGridView:
                    break;
                case Label label:
                    label.ForeColor = label.Font.Size >= 18 || label.Text == "THEME" ? currentTheme.Text : currentTheme.Muted;
                    break;
                case TableLayoutPanel table:
                    table.BackColor = currentTheme.Black;
                    break;
                case Panel panelControl:
                    panelControl.BackColor = panelControl.Height == 4 ? currentTheme.Primary : panelControl.Dock == DockStyle.Top ? currentTheme.Black : currentTheme.Panel;
                    panelControl.Invalidate();
                    break;
                default:
                    control.BackColor = currentTheme.Black;
                    control.ForeColor = currentTheme.Text;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyThemeToControls(control.Controls);
            }
        }
    }

    private Color GetButtonColor(Button button) => button.Text switch
    {
        "Clear" or "Convert" or "Cancel" => currentTheme.Danger,
        "Add folder" or "Output" or "ffmpeg" or "Open logs" or "Open last log" or "Retry failed" or "Clear converted" or "Pause" or "Resume" or "Help" => currentTheme.Secondary,
        _ => currentTheme.Primary
    };

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * amount), (int)Math.Round(a.G + (b.G - a.G) * amount), (int)Math.Round(a.B + (b.B - a.B) * amount));
    }
}
