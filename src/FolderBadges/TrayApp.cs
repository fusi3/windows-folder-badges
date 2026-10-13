using System.Diagnostics;

namespace FolderBadges;

/// <summary>The app itself: a tray icon with a right-click menu, and one watcher per badged folder.</summary>
public sealed class TrayApp : ApplicationContext
{
    readonly Control ui = new(); // invisible; lets background work hand results back to this thread
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly List<BadgeWatcher> watchers = new();
    AppSettings settings;
    SettingsForm? settingsForm;

    public TrayApp()
    {
        _ = ui.Handle; // creates the invisible window
        settings = AppSettings.Load();

        tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "FolderBadges",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();
        menu.Opening += (_, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };

        StartWatchers();
        Log.Write("FolderBadges started");

        if (settings.Folders.Count == 0)
            ShowSettings(); // first run with nothing set up
    }

    // ---------- Watchers ----------

    void StartWatchers()
    {
        foreach (var folder in settings.Folders.Where(f => f.Enabled))
        {
            var watcher = new BadgeWatcher(folder.FolderPath, folder.Rules, ui);
            watcher.Updated += UpdateTooltip;
            watchers.Add(watcher);
            watcher.Start();
        }
        UpdateTooltip();
    }

    void StopWatchers(bool removeBadges)
    {
        foreach (var w in watchers)
        {
            w.Dispose();
            if (removeBadges)
            {
                try { FolderIcon.RemoveBadge(w.Folder); }
                catch (Exception ex) { Log.Write($"Removing badge failed for {w.Folder}: {ex.Message}"); }
            }
        }
        watchers.Clear();
    }

    /// <summary>Called when Save is clicked in the settings window.</summary>
    void ApplySettings(AppSettings newSettings)
    {
        var stillBadged = newSettings.Folders
            .Where(f => f.Enabled)
            .Select(f => f.FolderPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        StopWatchers(removeBadges: false);

        // Folders that were removed or unticked go back to their normal icon
        foreach (var old in settings.Folders.Where(f => f.Enabled && !stillBadged.Contains(f.FolderPath)))
        {
            try { FolderIcon.RemoveBadge(old.FolderPath); }
            catch (Exception ex) { Log.Write($"Removing badge failed for {old.FolderPath}: {ex.Message}"); }
        }

        settings = newSettings;
        StartWatchers();
        Log.Write("Settings applied");
    }

    // ---------- UI ----------

    void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            if (settingsForm.WindowState == FormWindowState.Minimized)
                settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(settings.Clone(), tray.Icon);
        settingsForm.Saved += ApplySettings;
        settingsForm.Show();
    }

    void UpdateTooltip()
    {
        var lines = watchers.Select(w => $"{w.Name}: {w.LastResult?.BadgeCount.ToString() ?? "…"}");
        string text = watchers.Count == 0 ? "FolderBadges" : "FolderBadges\n" + string.Join("\n", lines);
        tray.Text = text.Length > 63 ? text[..63] : text; // Windows limits tooltip length
    }

    /// <summary>Rebuilt every time the menu opens, so the numbers are always current.</summary>
    void BuildMenu()
    {
        menu.Items.Clear();

        if (watchers.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("No badged folders yet") { Enabled = false });

        foreach (var w in watchers)
        {
            var r = w.LastResult;
            string label = r == null
                ? $"{w.Name}: counting…"
                : $"{w.Name}: {r.BadgeCount}   ({r.Files} files, {r.Folders} folders)";
            menu.Items.Add(label, null, (_, _) => OpenPath(w.Folder));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Refresh now", null, (_, _) => watchers.ForEach(w => w.ForceRefresh()));
        menu.Items.Add("Open log", null, (_, _) => OpenPath(Log.FilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (removes badges)", null, (_, _) => ExitThread());
    }

    protected override void ExitThreadCore()
    {
        settingsForm?.Close();
        StopWatchers(removeBadges: true);

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