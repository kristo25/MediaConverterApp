namespace MediaConverter;

internal static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaConverter", "logs", "app.log");

    public static void Warn(string context, Exception ex) => Write("WARN", context, ex.Message);

    private static void Write(string level, string context, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} [{level}] {context}: {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best-effort; never let it break the app.
        }
    }
}
