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

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is string exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}