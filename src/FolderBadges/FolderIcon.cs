using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FolderBadges;

/// <summary>Applies or removes a badge icon on a folder.</summary>
public static class FolderIcon
{
    static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderBadges", "backups");

    static readonly string[] IconKeys = { "IconResource", "IconFile", "IconIndex" };

    /// <summary>What the folder looked like before we touched it, so it can be restored exactly.</summary>
    record Backup(string Folder, string? OriginalIni, bool WasReadOnly);

    public static void SetBadge(string folder, int count)
    {
        if (!Directory.Exists(folder))
            return;

        if (count <= 0)
        {
            RemoveBadge(folder); // no items = normal folder, no badge
            return;
        }

        string iconPath = BadgeIcon.GetOrCreate(count);
        string iniPath = Path.Combine(folder, "desktop.ini");
        string? existing = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;

        // Already set to this number? Just nudge Explorer in case it's showing something stale.
        if (existing != null && existing.Contains(iconPath, StringComparison.OrdinalIgnoreCase))
        {
            NotifyExplorer(folder);
            return;
        }

        SaveBackupIfNeeded(folder);

        // Preferred: ask Windows to set the icon (same as Properties > Customize > Change Icon).
        int result = SetIconViaWindows(folder, iconPath);
        if (result != 0)
        {
            Log.Write($"Windows icon function failed (0x{result:X8}) for {folder}; writing desktop.ini manually");
            WriteIni(iniPath, SetIconInIni(existing, iconPath));

            // Windows only reads desktop.ini if the folder has the Read-only attribute.
            // (This doesn't make the files inside read-only.)
            var attrs = File.GetAttributes(folder);
            if (!attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(folder, attrs | FileAttributes.ReadOnly);
        }

        Log.Write($"Badge {count} applied to {folder}");
        NotifyExplorer(folder);
    }

    public static void RemoveBadge(string folder)
    {
        string backupPath = BackupPathFor(folder);
        if (!File.Exists(backupPath) || !Directory.Exists(folder))
            return; // we never changed this folder

        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(backupPath));
        string iniPath = Path.Combine(folder, "desktop.ini");

        if (backup?.OriginalIni == null)
        {
            // There was no desktop.ini before us, so delete ours.
            if (File.Exists(iniPath))
            {
                File.SetAttributes(iniPath, FileAttributes.Normal);
                File.Delete(iniPath);
            }
        }
        else
        {
            WriteIni(iniPath, backup.OriginalIni); // put the original back exactly
        }

        if (backup != null && !backup.WasReadOnly)
        {
            var attrs = File.GetAttributes(folder);
            File.SetAttributes(folder, attrs & ~FileAttributes.ReadOnly);
        }

        File.Delete(backupPath);
        Log.Write($"Badge removed from {folder}");
        NotifyExplorer(folder);
    }

    static void SaveBackupIfNeeded(string folder)
    {
        string backupPath = BackupPathFor(folder);
        if (File.Exists(backupPath))
            return; // keep the very first backup, never overwrite it with our own changes

        Directory.CreateDirectory(BackupDir);
        string iniPath = Path.Combine(folder, "desktop.ini");

        var backup = new Backup(
            Folder: folder,
            OriginalIni: File.Exists(iniPath) ? File.ReadAllText(iniPath) : null,
            WasReadOnly: File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));

        File.WriteAllText(backupPath, JsonSerializer.Serialize(backup));
    }

    static string BackupPathFor(string folder)
    {
        string key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToLowerInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(BackupDir, hash + ".json");
    }

    // ---------- Fallback: editing desktop.ini by hand ----------

    static string SetIconInIni(string? ini, string iconPath)
    {
        var lines = (ini ?? "").Replace("\r\n", "\n").Split('\n').ToList();

        int section = lines.FindIndex(l =>
            l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            lines.Insert(0, "[.ShellClassInfo]");
            section = 0;
        }

        int end = section + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('['))
            end++;

        for (int i = end - 1; i > section; i--)
        {
            string key = lines[i].Split('=')[0].Trim();
            if (IconKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                lines.RemoveAt(i);
        }

        lines.Insert(section + 1, $"IconResource={iconPath},0");
        return string.Join("\r\n", lines).TrimEnd() + "\r\n";
    }

    static void WriteIni(string path, string content)
    {
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal); // hidden files can't be overwritten directly

        File.WriteAllText(path, content, Encoding.Unicode);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
    }

    // ---------- Windows functions ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFOLDERCUSTOMSETTINGS
    {
        public uint dwSize;
        public uint dwMask;
        public IntPtr pvid;
        public string? pszWebViewTemplate;
        public uint cchWebViewTemplate;
        public string? pszWebViewTemplateVersion;
        public string? pszInfoTip;
        public uint cchInfoTip;
        public IntPtr pclsid;
        public uint dwFlags;
        public string? pszIconFile;
        public uint cchIconFile;
        public int iIconIndex;
        public string? pszLogo;
        public uint cchLogo;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS settings, string path, uint readWrite);

    const uint FCSM_ICONFILE = 0x00000010;
    const uint FCS_FORCEWRITE = 0x00000002;

    static int SetIconViaWindows(string folder, string iconPath)
    {
        var settings = new SHFOLDERCUSTOMSETTINGS
        {
            dwSize = (uint)Marshal.SizeOf<SHFOLDERCUSTOMSETTINGS>(),
            dwMask = FCSM_ICONFILE,
            pszIconFile = iconPath,
            iIconIndex = 0
        };
        return SHGetSetFolderCustomSettings(ref settings, folder, FCS_FORCEWRITE);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);

    const int SHCNE_UPDATEITEM = 0x00002000;
    const int SHCNE_UPDATEDIR = 0x00001000;
    const uint SHCNF_PATHW = 0x0005;

    /// <summary>Tells Explorer to redraw the folder and the place it sits (e.g. the Desktop).</summary>
    static void NotifyExplorer(string folder)
    {
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, folder, IntPtr.Zero);

        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        if (parent != null)
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, parent, IntPtr.Zero);
    }
}