using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MediaConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ConverterForm());
    }
}

internal sealed record ThemeSpec(Color Black, Color Panel, Color Surface, Color Primary, Color Secondary, Color Danger, Color Text, Color Muted);

internal sealed class AppSettings
{
    public string Theme { get; set; } = "Abyss";
    public string OutputFormat { get; set; } = "mp3";
    public string QualityPreset { get; set; } = "Balanced";
    public string CustomAudioValue { get; set; } = "192k";
    public string NamingRule { get; set; } = "Same name";
    public string FfmpegPath { get; set; } = "";
    public string LastInputFolder { get; set; } = "";
    public string LastOutputFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public bool Overwrite { get; set; }
    public bool DeleteOriginals { get; set; }
    public bool UseOutputFolder { get; set; }
    public bool PreserveFoldersInOutput { get; set; } = true;
    public List<string> RecentFolders { get; set; } = [];
}

internal sealed class QueueItem
{
    public required string Source { get; init; }
    public string Destination { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Message { get; set; } = "";
    public string Duration { get; set; } = "";
    public long SourceBytes { get; set; }
    public string RootFolder { get; set; } = "";
}

internal sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
}

internal sealed class ConverterForm : Form
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
        var themeLabel = new Label { Text = "Theme", Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(920, 19), Size = new Size(58, 24), ForeColor = currentTheme.Muted, TextAlign = ContentAlignment.MiddleRight };

        themeCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.Items.AddRange(themes.Keys.OrderBy(name => name).Cast<object>().ToArray());
        themeCombo.Location = new Point(986, 18);
        themeCombo.Size = new Size(132, 28);
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

        main.Controls.Add(dropPanel, 0, 0);
        main.Controls.Add(controls, 0, 1);
        main.Controls.Add(hintLabel, 0, 2);
        main.Controls.Add(grid, 0, 3);
        main.Controls.Add(summary, 0, 4);
        main.Controls.Add(bottom, 0, 5);
        main.Controls.Add(statusLabel, 0, 6);

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
        catch
        {
            // Settings are convenience-only; conversion should keep working if persistence fails.
        }
    }

    private static AppSettings LoadSettings(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings() : new AppSettings();
        }
        catch
        {
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
                    label.ForeColor = label.Font.Size >= 18 ? currentTheme.Text : currentTheme.Muted;
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

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Media files|*.mp3;*.m4a;*.mp4;*.ogg;*.wav;*.flac;*.aac;*.wma;*.webm;*.mkv;*.mov;*.avi|All files|*.*" };
        if (!string.IsNullOrWhiteSpace(settings.LastInputFolder) && Directory.Exists(settings.LastInputFolder))
        {
            dialog.InitialDirectory = settings.LastInputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(settings.LastInputFolder) && Directory.Exists(settings.LastInputFolder))
        {
            dialog.SelectedPath = settings.LastInputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths([dialog.SelectedPath]);
        }
    }

    private void ChooseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(settings.LastOutputFolder) && Directory.Exists(settings.LastOutputFolder))
        {
            dialog.SelectedPath = settings.LastOutputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFolderText.Text = dialog.SelectedPath;
        }
    }

    private void ChooseFfmpegPath()
    {
        using var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|Executable files|*.exe|All files|*.*", Title = "Choose ffmpeg.exe" };
        var detected = GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(detected) && File.Exists(detected))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(detected);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ffmpegPathText.Text = dialog.FileName;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var known = queue.Select(item => item.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = new List<string>();
        var ffprobe = GetFfprobePath();
        var addedItems = new List<QueueItem>();
        foreach (var path in paths)
        {
            foreach (var fileResult in GetMediaFiles(path))
            {
                if (fileResult.File is null)
                {
                    if (!string.IsNullOrWhiteSpace(fileResult.Message))
                    {
                        skipped.Add(fileResult.Message);
                    }
                    continue;
                }
                var file = fileResult.File;
                if (known.Add(file.FullName))
                {
                    var item = new QueueItem { Source = file.FullName, SourceBytes = file.Length, Duration = string.IsNullOrWhiteSpace(ffprobe) ? "" : "Loading...", RootFolder = fileResult.RootFolder };
                    queue.Add(item);
                    addedItems.Add(item);
                    RememberFolder(file.DirectoryName ?? "");
                    added++;
                }
            }
        }
        RefreshRecentFolders();
        RefreshGrid();
        if (added == 0)
        {
            MessageBox.Show(this, "No new supported media files were found.", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (skipped.Count > 0)
        {
            MessageBox.Show(this, $"Added {added} file(s). Some folders/files were skipped:\n\n{string.Join("\n", skipped.Take(8))}", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        if (addedItems.Count > 0)
        {
            _ = LoadDurationsAsync(addedItems);
        }
    }

    private List<(FileInfo? File, string RootFolder, string Message)> GetMediaFiles(string path)
    {
        var results = new List<(FileInfo? File, string RootFolder, string Message)>();
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            if (SupportedInputs.Contains(file.Extension))
            {
                results.Add((file, file.DirectoryName ?? "", ""));
            }
            return results;
        }
        if (!Directory.Exists(path))
        {
            return results;
        }
        RememberFolder(path);
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(path);
        while (pendingFolders.Count > 0)
        {
            var folder = pendingFolders.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                results.Add((null, path, $"Skipped folder {folder}: {ex.Message}"));
                continue;
            }

            foreach (var filePath in files)
            {
                FileInfo file;
                try
                {
                    file = new FileInfo(filePath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    results.Add((null, path, $"Skipped file {filePath}: {ex.Message}"));
                    continue;
                }
                if (SupportedInputs.Contains(file.Extension))
                {
                    results.Add((file, path, ""));
                }
            }

            if (!includeSubfoldersCheck.Checked)
            {
                continue;
            }

            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                results.Add((null, path, $"Skipped subfolders under {folder}: {ex.Message}"));
                continue;
            }

            foreach (var subfolder in subfolders)
            {
                pendingFolders.Push(subfolder);
            }
        }
        return results;
    }

    private void RememberFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }
        settings.LastInputFolder = folder;
        settings.RecentFolders.RemoveAll(existing => existing.Equals(folder, StringComparison.OrdinalIgnoreCase));
        settings.RecentFolders.Insert(0, folder);
        if (settings.RecentFolders.Count > 10)
        {
            settings.RecentFolders.RemoveRange(10, settings.RecentFolders.Count - 10);
        }
        SaveSettingsFromControls();
    }

    private void RefreshRecentFolders()
    {
        var selected = recentFolderCombo.SelectedItem?.ToString();
        recentFolderCombo.Items.Clear();
        foreach (var folder in settings.RecentFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            recentFolderCombo.Items.Add(folder);
        }
        if (!string.IsNullOrWhiteSpace(selected) && recentFolderCombo.Items.Contains(selected))
        {
            recentFolderCombo.SelectedItem = selected;
        }
    }

    private void RefreshGrid(bool updateStatus = true)
    {
        UpdateQueueDestinations();
        grid.Rows.Clear();
        foreach (var item in queue)
        {
            grid.Rows.Add(item.Status, item.Duration, item.Source, item.Destination, item.Message);
        }
        var pending = queue.Count(item => item.Status == "Pending");
        if (updateStatus)
        {
            statusLabel.Text = $"Queue: {queue.Count} file(s), {pending} ready";
        }
    }

    private void UpdateQueueDestinations()
    {
        var destinations = new Dictionary<string, List<QueueItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in queue)
        {
            item.Destination = GetOutputPath(item.Source, item.RootFolder);
            if (!destinations.TryGetValue(item.Destination, out var items))
            {
                items = [];
                destinations[item.Destination] = items;
            }
            items.Add(item);
        }
        foreach (var item in queue)
        {
            var sourceFull = Path.GetFullPath(item.Source);
            var destinationFull = Path.GetFullPath(item.Destination);
            var collidingItems = destinations[item.Destination];
            if (sourceFull.Equals(destinationFull, StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "Skipped";
                item.Message = "Input already matches selected output";
            }
            else if (collidingItems.Count > 1 && namingCombo.SelectedItem?.ToString() != "Auto-number conflicts")
            {
                item.Status = "Skipped";
                item.Message = "Output path conflicts with another queued item";
            }
            else if (File.Exists(item.Destination) && !overwriteCheck.Checked)
            {
                item.Status = "Skipped";
                item.Message = "Output already exists";
            }
            else if (item.Status is "Skipped" or "Pending")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
    }

    private string GetOutputPath(string source, string rootFolder)
    {
        var format = (formatCombo.SelectedItem?.ToString() ?? "mp3").ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(source);
        var namingRule = namingCombo.SelectedItem?.ToString() ?? "Same name";
        baseName = namingRule switch
        {
            "Append _converted" => baseName + "_converted",
            "VRC-safe filename" => MakeVrcSafeName(baseName),
            _ => baseName
        };
        var fileName = baseName + "." + format;
        var directory = Path.GetDirectoryName(source) ?? "";
        if (useOutputFolderCheck.Checked)
        {
            directory = outputFolderText.Text.Trim();
            if (preserveFoldersCheck.Checked && !string.IsNullOrWhiteSpace(rootFolder))
            {
                directory = Path.Combine(directory, GetRelativeDirectory(rootFolder, Path.GetDirectoryName(source) ?? ""));
            }
        }
        var destination = Path.Combine(directory, fileName);
        return namingRule == "Auto-number conflicts" && (!overwriteCheck.Checked || queue.Count > 1) ? GetUniqueDestination(destination, source) : destination;
    }

    private string GetUniqueDestination(string destination, string source)
    {
        var reserved = queue.Where(item => !item.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).Select(item => item.Destination).Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(destination) && !reserved.Contains(destination))
        {
            return destination;
        }
        var directory = Path.GetDirectoryName(destination) ?? "";
        var name = Path.GetFileNameWithoutExtension(destination);
        var extension = Path.GetExtension(destination);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        } while (File.Exists(candidate) || reserved.Contains(candidate));
        return candidate;
    }

    private static string GetRelativeDirectory(string root, string directory)
    {
        try
        {
            var relative = Path.GetRelativePath(root, directory);
            return relative == "." ? "" : relative;
        }
        catch
        {
            return "";
        }
    }

    private static string MakeVrcSafeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append('_');
            }
        }
        var result = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "converted" : result;
    }

    private string[] GetEncoderArgs(string format)
    {
        var preset = qualityCombo.SelectedItem?.ToString() ?? "Balanced";
        var custom = customAudioText.Text.Trim();
        return format.ToLowerInvariant() switch
        {
            "mp3" => preset switch
            {
                "Small file" => ["-codec:a", "libmp3lame", "-b:a", "128k"],
                "High quality" => ["-codec:a", "libmp3lame", "-q:a", "0"],
                "Custom" => ["-codec:a", "libmp3lame", "-b:a", string.IsNullOrWhiteSpace(custom) ? "192k" : custom],
                _ => ["-codec:a", "libmp3lame", "-q:a", "2"]
            },
            "wav" => ["-codec:a", "pcm_s16le"],
            "ogg" => preset switch
            {
                "Small file" => ["-codec:a", "libvorbis", "-q:a", "3"],
                "High quality" => ["-codec:a", "libvorbis", "-q:a", "7"],
                "Custom" => ["-codec:a", "libvorbis", "-b:a", string.IsNullOrWhiteSpace(custom) ? "192k" : custom],
                _ => ["-codec:a", "libvorbis", "-q:a", "5"]
            },
            "flac" => ["-codec:a", "flac"],
            "m4a" => preset switch
            {
                "Small file" => ["-codec:a", "aac", "-b:a", "128k"],
                "High quality" => ["-codec:a", "aac", "-b:a", "256k"],
                "Custom" => ["-codec:a", "aac", "-b:a", string.IsNullOrWhiteSpace(custom) ? "192k" : custom],
                _ => ["-codec:a", "aac", "-b:a", "192k"]
            },
            _ => throw new InvalidOperationException($"Unsupported output format: {format}")
        };
    }

    private string? GetFfmpegPath()
    {
        var configured = ffmpegPathText.Text.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        return FindExecutableOnPath("ffmpeg.exe");
    }

    private string? GetFfprobePath()
    {
        var ffmpeg = GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }
        return FindExecutableOnPath("ffprobe.exe");
    }

    private static string? FindExecutableOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private void UpdateFfmpegStatus()
    {
        var ffmpeg = GetFfmpegPath();
        statusLabel.Text = ffmpeg is null ? "ffmpeg not found. Use the ffmpeg button to choose ffmpeg.exe." : $"Ready. ffmpeg: {ffmpeg}";
    }

    private static string GetDurationText(string? ffprobe, string file)
    {
        if (string.IsNullOrWhiteSpace(ffprobe) || !File.Exists(ffprobe))
        {
            return "";
        }
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo { FileName = ffprobe, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", file })
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2500);
            return double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "";
        }
        catch
        {
            return "";
        }
    }

    private async Task LoadDurationsAsync(List<QueueItem> items)
    {
        var ffprobe = GetFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobe) || !File.Exists(ffprobe))
        {
            return;
        }

        foreach (var item in items)
        {
            if (!queue.Contains(item))
            {
                continue;
            }

            var duration = await Task.Run(() => GetDurationText(ffprobe, item.Source));
            if (!queue.Contains(item) || IsDisposed || !IsHandleCreated)
            {
                continue;
            }

            BeginInvoke(new Action(() =>
            {
                if (queue.Contains(item))
                {
                    item.Duration = duration;
                    RefreshGrid(updateStatus: false);
                }
            }));
        }
    }

    private async Task ConvertQueueAsync()
    {
        if (isConverting)
        {
            return;
        }
        var ffmpeg = GetFfmpegPath();
        if (ffmpeg is null)
        {
            MessageBox.Show(this, "ffmpeg was not found. Use the ffmpeg button to choose ffmpeg.exe.", "Missing ffmpeg", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (useOutputFolderCheck.Checked && !Directory.Exists(outputFolderText.Text))
        {
            MessageBox.Show(this, "Choose a valid output folder, or turn off 'Use one output folder'.", "Invalid output folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        UpdateQueueDestinations();
        var work = queue.Where(item => item.Status == "Pending").ToList();
        if (work.Count == 0)
        {
            MessageBox.Show(this, "Nothing is ready to convert.", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        isConverting = true;
        pauseRequested = false;
        conversionCancellation = new CancellationTokenSource();
        convertButton.Enabled = false;
        cancelButton.Enabled = true;
        pauseButton.Enabled = true;
        pauseButton.Text = "Pause";
        progressBar.Minimum = 0;
        progressBar.Maximum = work.Count;
        progressBar.Value = 0;
        var format = formatCombo.SelectedItem?.ToString() ?? "mp3";
        var encoderArgs = GetEncoderArgs(format);
        var logRows = new List<object>();

        try
        {
            for (var index = 0; index < work.Count; index++)
            {
                if (conversionCancellation.IsCancellationRequested)
                {
                    break;
                }
                while (pauseRequested && !conversionCancellation.IsCancellationRequested)
                {
                    statusLabel.Text = "Paused. Click Resume to continue.";
                    await Task.Delay(200);
                }
                var item = work[index];
                item.Status = "Converting";
                item.Message = "";
                RefreshGrid();
                statusLabel.Text = $"Converting {index + 1} of {work.Count}: {Path.GetFileName(item.Source)}";
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination) ?? ".");
                var overwriteArg = overwriteCheck.Checked ? "-y" : "-n";
                var args = new List<string> { "-hide_banner", "-loglevel", "error", overwriteArg, "-i", item.Source, "-vn", "-map", "0:a:0" };
                args.AddRange(encoderArgs);
                args.Add(item.Destination);
                var started = DateTime.Now;
                var result = await RunProcessAsync(ffmpeg, args, conversionCancellation.Token);
                var ended = DateTime.Now;
                if (conversionCancellation.IsCancellationRequested)
                {
                    item.Status = "Cancelled";
                    item.Message = "Cancelled by user";
                }
                else if (result.ExitCode == 0 && File.Exists(item.Destination) && new FileInfo(item.Destination).Length > 0)
                {
                    item.Status = "Converted";
                    item.Message = "";
                    if (deleteOriginalsCheck.Checked)
                    {
                        TryDeleteOriginal(item);
                    }
                }
                else
                {
                    item.Status = "Failed";
                    item.Message = string.IsNullOrWhiteSpace(result.Output) ? "ffmpeg failed without output" : result.Output.Trim();
                }
                logRows.Add(new { item.Source, item.Destination, OutputFormat = format, QualityPreset = qualityCombo.SelectedItem?.ToString() ?? "", item.Status, item.Message, StartedAt = started, EndedAt = ended, result.ExitCode });
                progressBar.Value = index + 1;
                RefreshGrid();
            }
            lastLogPath = WriteRunLog(logRows);
            var converted = queue.Count(item => item.Status == "Converted");
            var failed = queue.Count(item => item.Status == "Failed");
            var skipped = queue.Count(item => item.Status == "Skipped");
            var cancelled = queue.Count(item => item.Status == "Cancelled");
            UpdateRunSummary(converted, failed, skipped, cancelled);
            statusLabel.Text = $"Done. Converted: {converted}, failed: {failed}, skipped: {skipped}, cancelled: {cancelled}. Log: {lastLogPath}";
        }
        finally
        {
            activeProcess = null;
            conversionCancellation?.Dispose();
            conversionCancellation = null;
            convertButton.Enabled = true;
            cancelButton.Enabled = false;
            pauseButton.Enabled = false;
            pauseButton.Text = "Pause";
            pauseRequested = false;
            isConverting = false;
            SaveSettingsFromControls();
        }
    }

    private void TryDeleteOriginal(QueueItem item)
    {
        try
        {
            var output = new FileInfo(item.Destination);
            if (output.Exists && output.Length > 0 && !item.Source.Equals(item.Destination, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(item.Source);
                item.Message = "Original deleted";
            }
        }
        catch (Exception ex)
        {
            item.Message = $"Converted, but original delete failed: {ex.Message}";
        }
    }

    private void CancelConversion()
    {
        conversionCancellation?.Cancel();
        try
        {
            if (activeProcess is { HasExited: false })
            {
                activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        statusLabel.Text = "Cancelling...";
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> args, CancellationToken token)
    {
        var output = new StringBuilder();
        using var process = new Process();
        activeProcess = process;
        process.StartInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            return new ProcessResult { ExitCode = -1, Output = "Cancelled by user" };
        }
        finally
        {
            if (ReferenceEquals(activeProcess, process))
            {
                activeProcess = null;
            }
        }
        return new ProcessResult { ExitCode = process.ExitCode, Output = output.ToString() };
    }

    private string WriteRunLog(IEnumerable<object> rows)
    {
        var logRoot = GetLogRoot();
        Directory.CreateDirectory(logRoot);
        var path = Path.Combine(logRoot, $"conversion-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var lines = new List<string> { "Source,Destination,OutputFormat,QualityPreset,Status,Message,StartedAt,EndedAt,ExitCode" };
        foreach (dynamic row in rows)
        {
            lines.Add(string.Join(",", new[] { Csv(row.Source), Csv(row.Destination), Csv(row.OutputFormat), Csv(row.QualityPreset), Csv(row.Status), Csv(row.Message), Csv(row.StartedAt.ToString("O")), Csv(row.EndedAt.ToString("O")), Csv(row.ExitCode.ToString()) }));
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
        return path;
    }

    private static string GetLogRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaConverter", "logs");

    private void OpenLogsFolder()
    {
        var logRoot = GetLogRoot();
        Directory.CreateDirectory(logRoot);
        Process.Start(new ProcessStartInfo(logRoot) { UseShellExecute = true });
    }

    private void OpenLastLog()
    {
        if (!string.IsNullOrWhiteSpace(lastLogPath) && File.Exists(lastLogPath))
        {
            Process.Start(new ProcessStartInfo(lastLogPath) { UseShellExecute = true });
            return;
        }
        OpenLogsFolder();
    }

    private void UpdateRunSummary(int converted, int failed, int skipped, int cancelled)
    {
        summaryLabel.Text = $"Last run: {converted} converted, {failed} failed, {skipped} skipped, {cancelled} cancelled.";
        openLastLogButton.Enabled = !string.IsNullOrWhiteSpace(lastLogPath) && File.Exists(lastLogPath);
        retryFailedButton.Enabled = queue.Any(item => item.Status is "Failed" or "Skipped" or "Cancelled");
        summaryClearConvertedButton.Enabled = queue.Any(item => item.Status == "Converted");
    }

    private void RemoveSelectedRows()
    {
        foreach (var item in GetSelectedQueueItems())
        {
            queue.Remove(item);
        }
        RefreshGrid();
    }

    private void RetrySelectedRows()
    {
        foreach (var item in GetSelectedQueueItems())
        {
            if (item.Status is "Failed" or "Skipped" or "Cancelled")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
        RefreshGrid();
    }

    private void RetryFailedItems()
    {
        foreach (var item in queue)
        {
            if (item.Status is "Failed" or "Skipped" or "Cancelled")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
        retryFailedButton.Enabled = false;
        RefreshGrid();
    }

    private void ClearConvertedItems()
    {
        queue.RemoveAll(item => item.Status == "Converted");
        summaryClearConvertedButton.Enabled = queue.Any(item => item.Status == "Converted");
        RefreshGrid();
    }

    private void ShowHelpDialog()
    {
        MessageBox.Show(
            this,
            "Drag files or folders into the top area, or use Add files/Add folder.\n\n" +
            "Default output is MP3 in the same folder as the source. Use Format, Quality, and Naming to change the result. Use one output folder if you want every converted file saved somewhere else.\n\n" +
            "Quality presets choose common ffmpeg audio settings. Custom accepts values such as 192k for bitrate.\n\n" +
            "Delete originals after success only removes a source file after ffmpeg succeeds and the output file exists. Logs and settings are stored in LocalAppData\\MediaConverter.",
            "Media Converter Help",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenSelectedFolder(bool source)
    {
        var item = GetSelectedQueueItems().FirstOrDefault();
        if (item is null)
        {
            return;
        }
        var path = source ? item.Source : item.Destination;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }

    private List<QueueItem> GetSelectedQueueItems()
    {
        var indexes = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).Where(index => index >= 0 && index < queue.Count).Distinct().OrderByDescending(index => index).ToList();
        return indexes.Select(index => queue[index]).ToList();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
