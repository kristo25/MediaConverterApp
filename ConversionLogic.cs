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
