using System.Runtime.InteropServices;

namespace FolderBadges;

/// <summary>The counting rules for one badged folder. The settings window will edit these later.</summary>
public class CountRules
{
    /// <summary>How many levels to look into. 1 = only what's directly inside. 0 = no limit.</summary>
    public int Depth { get; set; } = 1;

    public bool CountFiles { get; set; } = true;
    public bool CountFolders { get; set; } = true;
    public bool IncludeHidden { get; set; } = false;

    /// <summary>Shortcuts to folders (e.g. a Downloads shortcut) are opened and their
    /// contents treated as if they were directly inside this folder.</summary>
    public bool MergeShortcutFolders { get; set; } = true;

    /// <summary>Names of real subfolders to treat the same way (e.g. "Downloads").</summary>
    public List<string> MergeFolders { get; set; } = new();

    /// <summary>If not empty, only files with these extensions count (e.g. "pdf", ".jpg").</summary>
    public List<string> OnlyExtensions { get; set; } = new();

    /// <summary>Files with these extensions never count.</summary>
    public List<string> ExcludeExtensions { get; set; } = new();
}

/// <summary>What the counter found. BadgeCount follows the rules; the rest is a full breakdown.</summary>
public class CountResult
{
    public int BadgeCount { get; set; }
    public int Files { get; set; }
    public int Folders { get; set; }
    public int Hidden { get; set; }
    public Dictionary<string, int> ByExtension { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MergedFolders { get; } = new();
}

public class FileCounter
{
    // Files Windows (or this app) creates that should never count as items.
    static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "Thumbs.db", ".folderbadge.ico"
    };

    readonly CountRules rules;
    readonly HashSet<string> onlyExt;
    readonly HashSet<string> excludeExt;
    readonly HashSet<string> mergeNames;

    HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
    CountResult result = new();

    public FileCounter(CountRules rules)
    {
        this.rules = rules;
        onlyExt = NormalizeExtensions(rules.OnlyExtensions);
        excludeExt = NormalizeExtensions(rules.ExcludeExtensions);
        mergeNames = new HashSet<string>(rules.MergeFolders, StringComparer.OrdinalIgnoreCase);
    }

    public CountResult Count(string folderPath)
    {
        visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        result = new CountResult();
        int levels = rules.Depth <= 0 ? int.MaxValue : rules.Depth;
        ScanFolder(folderPath, levels, allowMerge: true);
        return result;
    }

    void ScanFolder(string folderPath, int levelsLeft, bool allowMerge)
    {
        string? realPath = ResolveFolder(folderPath);
        if (realPath == null || !visited.Add(realPath))
            return; // missing, unreadable, or already counted

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.System // hidden items are still seen, so they can be reported
        };

        try
        {
            foreach (var entry in new DirectoryInfo(realPath).EnumerateFileSystemInfos("*", options))
            {
                if (IgnoredNames.Contains(entry.Name))
                    continue;

                bool hidden = entry.Attributes.HasFlag(FileAttributes.Hidden);
                bool hiddenOk = !hidden || rules.IncludeHidden;

                if (entry is DirectoryInfo subfolder)
                {
                    // A real subfolder marked for merging, e.g. "Downloads"
                    if (allowMerge && mergeNames.Contains(subfolder.Name))
                    {
                        Merge(subfolder.FullName, levelsLeft);
                        continue;
                    }

                    result.Folders++;
                    if (hidden) result.Hidden++;
                    if (rules.CountFolders && hiddenOk) result.BadgeCount++;

                    if (levelsLeft > 1 && hiddenOk)
                        ScanFolder(subfolder.FullName, levelsLeft - 1, allowMerge: false);
                }
                else
                {
                    // A shortcut to a folder, e.g. a Downloads shortcut
                    if (allowMerge && rules.MergeShortcutFolders
                        && entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        && GetShortcutTarget(entry.FullName) is string target
                        && Directory.Exists(target))
                    {
                        Merge(target, levelsLeft);
                        continue;
                    }

                    string ext = string.IsNullOrEmpty(entry.Extension)
                        ? "(no extension)"
                        : entry.Extension.ToLowerInvariant();

                    result.Files++;
                    if (hidden) result.Hidden++;
                    result.ByExtension[ext] = result.ByExtension.GetValueOrDefault(ext) + 1;

                    if (rules.CountFiles && hiddenOk && ExtensionAllowed(ext))
                        result.BadgeCount++;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Folder vanished or became unreadable mid-count; keep what we have.
        }
    }

    /// <summary>Counts a folder's contents as if they were directly in the badged folder.</summary>
    void Merge(string path, int levelsLeft)
    {
        string? real = ResolveFolder(path);
        if (real != null && !visited.Contains(real))
            result.MergedFolders.Add(real);
        ScanFolder(path, levelsLeft, allowMerge: false);
    }

    bool ExtensionAllowed(string ext) =>
        (onlyExt.Count == 0 || onlyExt.Contains(ext)) && !excludeExt.Contains(ext);

    static HashSet<string> NormalizeExtensions(IEnumerable<string> list) =>
        list.Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet();

    /// <summary>Returns the real location of a folder, following junctions/symlinks.</summary>
    static string? ResolveFolder(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return null;

            string fullPath = dir.FullName;
            if (dir.LinkTarget != null)
            {
                var target = dir.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null || !Directory.Exists(target.FullName)) return null;
                fullPath = target.FullName;
            }
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads where a .lnk shortcut points, using Windows' built-in shell.</summary>
    static string? GetShortcutTarget(string lnkPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return null;

        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;
            shortcut = ((dynamic)shell).CreateShortcut(lnkPath);
            string target = ((dynamic)shortcut!).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}