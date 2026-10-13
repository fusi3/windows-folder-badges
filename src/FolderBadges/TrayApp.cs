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

        Log.Write($"FolderBadges {Application.ProductVersion} started");
        RemoveLeftoverBadges();
        StartWatchers();

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

    /// <summary>
    /// A folder can be left with an old badge if it was removed from the list while the app wasn't running.
    /// On startup, put the normal icon back on any badged folder that isn't in the list (or is unticked).
    /// </summary>
    void RemoveLeftoverBadges()
    {
        try
        {
            FolderIcon.RemoveAllBadges(keep: settings.Folders.Where(f => f.Enabled).Select(f => f.FolderPath));
        }
        catch (Exception ex)
        {
            Log.Write($"Checking for leftover badges failed: {ex.Message}");
        }
    }

    /// <summary>Tray menu → "Remove all badges and exit": a full clean-up, also useful before uninstalling.</summary>
    void RemoveAllBadgesAndExit()
    {
        var answer = MessageBox.Show(
            "This puts the normal icon back on every folder FolderBadges has ever badged, " +
            "deletes the badge images, and closes the app.\n\n" +
            "Your files and your settings aren't touched. Start FolderBadges again to bring the badges back.",
            "Remove all badges", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK)
            return;

        settingsForm?.Close();
        StopWatchers(removeBadges: false); // stop first, so nothing puts a badge back while we clean up

        FolderIcon.CleanupResult result;
        try
        {
            result = FolderIcon.RemoveAllBadges();
        }
        catch (Exception ex)
        {
            Log.Write($"Remove all badges failed: {ex.Message}");
            MessageBox.Show("Something went wrong: " + ex.Message, "Remove all badges");
            ExitThread();
            return;
        }

        Log.Write($"Remove all badges: {result.Restored} restored, {result.NotFound.Count} not found, {result.Failed.Count} failed");

        if (result.AllClear)
        {
            BadgeIcon.DeleteAll(); // no folder uses them any more
        }
        else
        {
            // Keep the images, because folders we couldn't restore may still be showing them
            var lines = new List<string>();
            if (result.NotFound.Count > 0)
                lines.Add("These folders couldn't be found (moved, deleted, or on a drive that isn't plugged in). " +
                          "Their backups are kept, so running this again later will fix them:\n" +
                          string.Join("\n", result.NotFound));
            if (result.Failed.Count > 0)
                lines.Add("These couldn't be restored (see the log for details):\n" + string.Join("\n", result.Failed));

            MessageBox.Show(
                $"{result.Restored} folder(s) restored.\n\n" + string.Join("\n\n", lines),
                "Remove all badges", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ExitThread();
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
        menu.Items.Add("Remove all badges and exit…", null, (_, _) => RemoveAllBadgesAndExit());
        menu.Items.Add("Exit (removes badges)", null, (_, _) => ExitThread());
        menu.Items.Add(new ToolStripMenuItem($"FolderBadges {Application.ProductVersion}") { Enabled = false });
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

    /// <summary>Uses the app's own icon (app.ico, built into the .exe) at tray size.</summary>
    static Icon LoadTrayIcon()
    {
        try
        {
            if (Environment.ProcessPath is string exe
                && Icon.ExtractIcon(exe, 0, SystemInformation.SmallIconSize.Width) is Icon icon)
                return icon;
            return SystemIcons.Application;
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