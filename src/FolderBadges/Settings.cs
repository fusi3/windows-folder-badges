using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace FolderBadges;

/// <summary>One folder in the list, with its own counting rules.</summary>
public class FolderConfig
{
    public string FolderPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public CountRules Rules { get; set; } = new();

    [JsonIgnore]
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(FolderPath));
}

/// <summary>Everything the app remembers, saved as JSON in %LOCALAPPDATA%\FolderBadges.</summary>
public class AppSettings
{
    public List<FolderConfig> Folders { get; set; } = new();

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderBadges", "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                       ?? CreateDefault();
        }
        catch (Exception ex)
        {
            Log.Write($"Could not read settings, using defaults: {ex.Message}");
        }
        return CreateDefault();
    }

    /// <summary>First run: start with Desktop\Inbox if it exists.</summary>
    static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        string inbox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Inbox");
        if (Directory.Exists(inbox))
            settings.Folders.Add(new FolderConfig { FolderPath = inbox });
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>A separate copy, so the settings window can be cancelled without changing anything.</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}

/// <summary>Turns "start with Windows" on or off (the per-user Run key in the registry).</summary>
public static class StartupSetting
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FolderBadges";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Where the app keeps its own copy so it can start with Windows,
    /// e.g. C:\Users\you\AppData\Local\Programs\FolderBadges\FolderBadges.exe</summary>
    public static readonly string InstalledExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "FolderBadges", "FolderBadges.exe");

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is string exe)
            key.SetValue(ValueName, $"\"{CopyToInstallFolder(exe)}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// People often run the downloaded .exe straight from their Downloads folder and may delete it later.
    /// So before registering it to start with Windows, copy it to a permanent folder and use that copy.
    /// Running a newer download and saving settings again also updates the copy.
    /// </summary>
    static string CopyToInstallFolder(string exe)
    {
        if (exe.Equals(InstalledExe, StringComparison.OrdinalIgnoreCase))
            return exe; // already running from the permanent copy

        // A development build (dotnet run / dotnet build) needs the FolderBadges.dll next to it,
        // so copying only the .exe wouldn't work. Use it where it is.
        if (File.Exists(Path.ChangeExtension(exe, ".dll")))
            return exe;

        Directory.CreateDirectory(Path.GetDirectoryName(InstalledExe)!);
        File.Copy(exe, InstalledExe, overwrite: true);
        Log.Write($"Copied app to {InstalledExe} for start with Windows");
        return InstalledExe;
    }
}