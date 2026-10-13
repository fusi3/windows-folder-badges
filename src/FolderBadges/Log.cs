namespace FolderBadges;

/// <summary>Writes a simple log to %LOCALAPPDATA%\FolderBadges\log.txt for troubleshooting.</summary>
public static class Log
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderBadges", "log.txt");

    static readonly object gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // Start fresh if the log gets big (over ~1 MB)
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                    info.Delete();

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app
        }
    }
}