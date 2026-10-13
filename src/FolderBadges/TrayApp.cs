using System.Diagnostics;

namespace FolderBadges;

/// <summary>The app itself: a tray icon with a right-click menu, and one watcher per badged folder.</summary>
public sealed class TrayApp : ApplicationContext
{
    readonly Control ui = new(); // invisible; lets background work hand results back to this thread
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly List<BadgeWatcher> watchers = new();

    public TrayApp()
    {
        _ = ui.Handle; // creates the invisible window

        tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "FolderBadges",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) =>
        {
            if (watchers.Count > 0) OpenPath(watchers[0].Folder);
        };
        menu.Opening += (_, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };

        // TEMPORARY: hard-coded until the settings window exists
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        AddFolder(Path.Combine(desktop, "Inbox"), new CountRules
        {
            Depth = 1,
            MergeFolders = { "Downloads" },   // un-comment if Downloads is a REAL folder inside Inbox
        });

        Log.Write("FolderBadges started");
    }

    void AddFolder(string folder, CountRules rules)
    {
        var watcher = new BadgeWatcher(folder, rules, ui);
        watcher.Updated += UpdateTooltip;
        watchers.Add(watcher);
        watcher.Start();
    }

    void UpdateTooltip()
    {
        var lines = watchers.Select(w => $"{w.Name}: {w.LastResult?.BadgeCount.ToString() ?? "…"}");
        string text = "FolderBadges\n" + string.Join("\n", lines);
        tray.Text = text.Length > 63 ? text[..63] : text; // Windows limits tooltip length
    }

    /// <summary>Rebuilt every time the menu opens, so the numbers are always current.</summary>
    void BuildMenu()
    {
        menu.Items.Clear();

        foreach (var w in watchers)
        {
            var r = w.LastResult;
            string label = r == null
                ? $"{w.Name}: counting…"
                : $"{w.Name}: {r.BadgeCount}   ({r.Files} files, {r.Folders} folders)";
            menu.Items.Add(label, null, (_, _) => OpenPath(w.Folder));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (_, _) => watchers.ForEach(w => w.ForceRefresh()));
        menu.Items.Add("Open log", null, (_, _) => OpenPath(Log.FilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (removes badges)", null, (_, _) => ExitThread());
    }

    protected override void ExitThreadCore()
    {
        foreach (var w in watchers)
        {
            w.Dispose();
            try { FolderIcon.RemoveBadge(w.Folder); }
            catch (Exception ex) { Log.Write($"Removing badge failed for {w.Folder}: {ex.Message}"); }
        }

        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        ui.Dispose();
        Log.Write("FolderBadges exited");
        base.ExitThreadCore();
    }

    static Icon LoadTrayIcon()
    {
        try
        {
            string imageres = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            return Icon.ExtractIcon(imageres, -3, SystemInformation.SmallIconSize.Width) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* e.g. log doesn't exist yet */ }
    }
}