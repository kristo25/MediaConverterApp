using System.Text;

namespace MediaConverter;

/// <summary>Pure, UI-independent helpers so conversion/naming rules can be unit tested without WinForms.</summary>
internal static class ConversionLogic
{
    private static readonly char[] FormulaLeadChars = ['=', '+', '-', '@'];

    public static string BuildBaseName(string sourceFileName, string namingRule) =>
        namingRule switch
        {
            "Append _converted" => sourceFileName + "_converted",
            "VRC-safe filename" => MakeVrcSafeName(sourceFileName),
            _ => sourceFileName
        };

    public static string MakeVrcSafeName(string name)
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

    public static string GetRelativeDirectory(string root, string directory)
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

    public static string BuildOutputPath(
        string source,
        string rootFolder,
        string outputFormat,
        string namingRule,
        bool useOutputFolder,
        string outputFolder,
        bool preserveFolders)
    {
        var baseName = BuildBaseName(Path.GetFileNameWithoutExtension(source), namingRule);
        var fileName = baseName + "." + outputFormat.ToLowerInvariant();
        var directory = Path.GetDirectoryName(source) ?? "";
        if (useOutputFolder)
        {
            directory = outputFolder.Trim();
            if (preserveFolders && !string.IsNullOrWhiteSpace(rootFolder))
            {
                directory = Path.Combine(directory, GetRelativeDirectory(rootFolder, Path.GetDirectoryName(source) ?? ""));
            }
        }
        return Path.Combine(directory, fileName);
    }

    public static string GetUniqueDestination(string destination, string source, IEnumerable<string> reservedDestinations)
    {
        var reserved = reservedDestinations
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    public static string[] GetEncoderArgs(string format, string preset, string custom)
    {
        custom = custom.Trim();
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
                "Custom" => ["-codec:a", "libvorbis", "-q:a", BitrateToVorbisQuality(custom)],
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

    // Vorbis's own approximate quality-to-bitrate table (see xiph.org's vorbis-tools docs).
    // libvorbis's managed-bitrate mode (-b:a) can reject bitrate/samplerate/channel combos
    // outright ("encoder setup failed") - e.g. 192k on a mono 44.1kHz source. The -q:a VBR
    // scale it's actually designed around has no such failure mode, so map the user's kbps
    // target onto the nearest quality step instead of passing -b:a straight through.
    private static readonly (int Kbps, int Quality)[] VorbisQualitySteps =
    [
        (45, -1), (64, 0), (80, 1), (96, 2), (112, 3), (128, 4), (160, 5), (192, 6), (224, 7), (256, 8), (320, 9), (500, 10)
    ];

    private static string BitrateToVorbisQuality(string custom)
    {
        var digits = new string(custom.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var kbps))
        {
            kbps = 192;
        }
        var closest = VorbisQualitySteps[0];
        foreach (var step in VorbisQualitySteps)
        {
            if (Math.Abs(step.Kbps - kbps) < Math.Abs(closest.Kbps - kbps))
            {
                closest = step;
            }
        }
        return closest.Quality.ToString();
    }

    /// <summary>
    /// CSV-escapes a field and neutralizes leading =/+/-/@ so spreadsheet apps (Excel, etc.)
    /// don't interpret log content — which can contain raw ffmpeg stderr text — as a formula.
    /// </summary>
    public static string Csv(string value)
    {
        if (value.Length > 0 && FormulaLeadChars.Contains(value[0]))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
