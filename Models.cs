namespace MediaConverter;

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
