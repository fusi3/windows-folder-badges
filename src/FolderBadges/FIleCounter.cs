using System.Runtime.InteropServices;

namespace FolderBadges;

/// <summary>
/// Counts the files inside a folder, including files in all subfolders.
/// Folders themselves are never counted. Shortcuts to folders are followed.
/// </summary>
public class FileCounter
{
    public bool IncludeHidden { get; set; } = false;
    public bool FollowShortcuts { get; set; } = true;

    // Files Windows (or this app) creates that should never count as "items".
    static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "Thumbs.db", ".folderbadge.ico"
    };

    public int Count(string folderPath)
    {
        // Remembers every folder already counted, so loops and duplicates are skipped.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return CountFolder(folderPath, visited);
    }

    int CountFolder(string folderPath, HashSet<string> visited)
    {
        string? realPath = ResolveFolder(folderPath);
        if (realPath == null || !visited.Add(realPath))
            return 0; // missing, unreadable, or already counted

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = IncludeHidden
                ? FileAttributes.System
                : FileAttributes.Hidden | FileAttributes.System
        };

        int total = 0;
        try
        {
            foreach (var entry in new DirectoryInfo(realPath).EnumerateFileSystemInfos("*", options))
            {
                if (IgnoredNames.Contains(entry.Name))
                    continue;

                if (entry is DirectoryInfo subfolder)
                {
                    total += CountFolder(subfolder.FullName, visited);
                }
                else if (FollowShortcuts
                         && entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                         && GetShortcutTarget(entry.FullName) is string target
                         && Directory.Exists(target))
                {
                    total += CountFolder(target, visited); // shortcut to a folder: count inside it
                }
                else
                {
                    total++; // a real item
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Folder vanished or became unreadable mid-count; keep what we have.
        }
        return total;
    }

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