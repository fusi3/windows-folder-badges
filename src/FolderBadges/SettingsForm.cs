using System.Text.Json;

namespace FolderBadges;

/// <summary>The settings window: which folders get badges, their rules, and a live breakdown.</summary>
public sealed class SettingsForm : Form
{
    /// <summary>Raised when the user clicks Save, with the new settings.</summary>
    public event Action<AppSettings>? Saved;

    readonly AppSettings settings;

    // Left side
    readonly ListView folderList = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        Dock = DockStyle.Fill
    };

    // Rules
    readonly NumericUpDown depth = new() { Minimum = 0, Maximum = 50, Width = 60 };
    readonly CheckBox countFiles = MakeCheck("Count files");
    readonly CheckBox countFolders = MakeCheck("Count folders (each folder counts as 1)");
    readonly CheckBox includeHidden = MakeCheck("Include hidden items");
    readonly CheckBox mergeShortcuts = MakeCheck("Open shortcuts to folders (e.g. a Downloads shortcut) and count what's inside");
    readonly TextBox mergeFolders = MakeText();
    readonly TextBox onlyTypes = MakeText();
    readonly TextBox excludeTypes = MakeText();

    // Breakdown
    readonly Label summary = new() { AutoSize = true, Padding = new Padding(0, 4, 0, 8) };
    readonly ListView typeList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };

    // Bottom
    readonly CheckBox startWithWindows = MakeCheck("Start FolderBadges when Windows starts");

    readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 400 };
    Control rulesPanel = null!;
    bool loading;       // true while filling in controls, so we don't treat that as user edits
    int previewVersion; // ignores out-of-date preview results

    public SettingsForm(AppSettings settings, Icon? icon)
    {
        this.settings = settings;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi; // scales properly on high-DPI screens like the G14
        Text = "FolderBadges – Settings";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(960, 640);
        MinimumSize = new Size(760, 520);

        folderList.Columns.Add("Folder", 150);
        folderList.Columns.Add("Location", 260);
        typeList.Columns.Add("File type", 160);
        typeList.Columns.Add("Count", 80, HorizontalAlignment.Right);

        Controls.Add(BuildLayout());
        ResumeLayout(false);
        PerformLayout();

        // Events
        folderList.SelectedIndexChanged += (_, _) => ShowSelected();
        folderList.ItemChecked += (_, e) =>
        {
            if (!loading && e.Item.Tag is FolderConfig c) c.Enabled = e.Item.Checked;
        };
        depth.ValueChanged += (_, _) => RulesChanged();
        foreach (var box in new[] { countFiles, countFolders, includeHidden, mergeShortcuts })
            box.CheckedChanged += (_, _) => RulesChanged();
        foreach (var text in new[] { mergeFolders, onlyTypes, excludeTypes })
            text.TextChanged += (_, _) => RulesChanged();
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            RunPreview();
        };
        FormClosed += (_, _) => previewTimer.Dispose();

        startWithWindows.Checked = StartupSetting.IsEnabled;
        PopulateList(null);
    }

    // ---------- Layout ----------

    Control BuildLayout()
    {
        // Left: folder list + buttons
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(Header("Badged folders (tick to show a badge)"), 0, 0);
        left.Controls.Add(folderList, 0, 1);
        left.Controls.Add(Flow(
            MakeButton("Add folder…", (_, _) => AddFolder()),
            MakeButton("Add all Desktop folders", (_, _) => AddAllDesktopFolders()),
            MakeButton("Remove", (_, _) => RemoveSelected())), 0, 2);

        // Right: rules
        var rules = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        rules.Controls.Add(Header("Counting rules"));
        rules.Controls.Add(Flow(Caption("Levels deep:"), depth,
            Hint("1 = only what's directly inside    0 = everything")));
        rules.Controls.Add(countFiles);
        rules.Controls.Add(countFolders);
        rules.Controls.Add(includeHidden);
        rules.Controls.Add(mergeShortcuts);
        rules.Controls.Add(Caption("Also open these subfolders and count what's inside (names, comma-separated):"));
        rules.Controls.Add(mergeFolders);
        rules.Controls.Add(Caption("Only count these file types (blank = all), e.g. pdf, jpg, zip:"));
        rules.Controls.Add(onlyTypes);
        rules.Controls.Add(Caption("Never count these file types:"));
        rules.Controls.Add(excludeTypes);
        rulesPanel = rules;

        // Right: rules on top, breakdown below
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(rules, 0, 0);
        right.Controls.Add(Header("Breakdown"), 0, 1);
        right.Controls.Add(summary, 0, 2);
        right.Controls.Add(typeList, 0, 3);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        main.Controls.Add(left, 0, 0);
        main.Controls.Add(right, 1, 0);

        // Bottom bar
        var cancel = MakeButton("Cancel", (_, _) => Close());
        CancelButton = cancel;
        var buttons = Flow(MakeButton("Save", (_, _) => SaveAndClose()), cancel);
        buttons.Anchor = AnchorStyles.Right;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(startWithWindows, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(main, 0, 0);
        root.Controls.Add(bottom, 0, 1);
        return root;
    }

    // ---------- Folder list ----------

    FolderConfig? Selected =>
        folderList.SelectedItems.Count > 0 ? folderList.SelectedItems[0].Tag as FolderConfig : null;

    void PopulateList(FolderConfig? select)
    {
        loading = true;
        folderList.BeginUpdate();
        folderList.Items.Clear();
        foreach (var c in settings.Folders)
        {
            var item = new ListViewItem(new[] { c.Name, c.FolderPath }) { Tag = c, Checked = c.Enabled };
            if (!Directory.Exists(c.FolderPath))
                item.ForeColor = SystemColors.GrayText; // folder has gone missing
            folderList.Items.Add(item);
        }
        folderList.EndUpdate();
        loading = false;

        var toSelect = folderList.Items.Cast<ListViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, select))
                       ?? (folderList.Items.Count > 0 ? folderList.Items[0] : null);
        if (toSelect != null)
        {
            toSelect.Selected = true; // triggers ShowSelected
            toSelect.EnsureVisible();
        }
        else
        {
            ShowSelected();
        }
    }

    void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to show a badge on",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddPaths(new[] { dialog.SelectedPath });
    }

    void AddAllDesktopFolders()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var folders = Directory.GetDirectories(desktop)
            .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.Hidden));
        AddPaths(folders);
    }

    void AddPaths(IEnumerable<string> paths)
    {
        FolderConfig? last = null;
        foreach (var p in paths)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
            var existing = settings.Folders.FirstOrDefault(f =>
                f.FolderPath.Equals(full, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                last = existing; // already in the list
                continue;
            }
            last = new FolderConfig { FolderPath = full };
            settings.Folders.Add(last);
        }
        PopulateList(last);
    }

    void RemoveSelected()
    {
        if (Selected is FolderConfig c)
        {
            settings.Folders.Remove(c);
            PopulateList(null);
        }
    }

    // ---------- Rules ----------

    void ShowSelected()
    {
        var c = Selected;
        rulesPanel.Enabled = c != null;

        if (c == null)
        {
            summary.Text = settings.Folders.Count == 0
                ? "Add a folder to get started."
                : "Select a folder to see its rules.";
            typeList.Items.Clear();
            return;
        }

        loading = true;
        depth.Value = Math.Clamp(c.Rules.Depth, (int)depth.Minimum, (int)depth.Maximum);
        countFiles.Checked = c.Rules.CountFiles;
        countFolders.Checked = c.Rules.CountFolders;
        includeHidden.Checked = c.Rules.IncludeHidden;
        mergeShortcuts.Checked = c.Rules.MergeShortcutFolders;
        mergeFolders.Text = string.Join(", ", c.Rules.MergeFolders);
        onlyTypes.Text = string.Join(", ", c.Rules.OnlyExtensions);
        excludeTypes.Text = string.Join(", ", c.Rules.ExcludeExtensions);
        loading = false;

        RunPreview();
    }

    void RulesChanged()
    {
        if (loading || Selected is not FolderConfig c) return;

        c.Rules.Depth = (int)depth.Value;
        c.Rules.CountFiles = countFiles.Checked;
        c.Rules.CountFolders = countFolders.Checked;
        c.Rules.IncludeHidden = includeHidden.Checked;
        c.Rules.MergeShortcutFolders = mergeShortcuts.Checked;
        c.Rules.MergeFolders = ParseList(mergeFolders.Text);
        c.Rules.OnlyExtensions = ParseList(onlyTypes.Text);
        c.Rules.ExcludeExtensions = ParseList(excludeTypes.Text);

        // Wait until typing pauses before recounting
        previewTimer.Stop();
        previewTimer.Start();
    }

    // ---------- Live breakdown ----------

    async void RunPreview()
    {
        if (Selected is not FolderConfig c) return;
        int version = ++previewVersion;

        if (!Directory.Exists(c.FolderPath))
        {
            summary.Text = "This folder doesn't exist any more.";
            typeList.Items.Clear();
            return;
        }

        summary.Text = "Counting…";
        string path = c.FolderPath;
        var rules = JsonClone(c.Rules);

        CountResult result;
        try
        {
            result = await Task.Run(() => new FileCounter(rules).Count(path)); // off the UI thread
        }
        catch (Exception ex)
        {
            if (version == previewVersion && !IsDisposed)
                summary.Text = "Couldn't count: " + ex.Message;
            return;
        }

        if (version != previewVersion || IsDisposed)
            return; // the user changed something while we were counting

        string merged = result.MergedFolders.Count == 0
            ? "nothing"
            : string.Join(", ", result.MergedFolders.Select(Path.GetFileName));

        summary.Text =
            $"Badge shows: {result.BadgeCount}\n" +
            $"Found {result.Files} files and {result.Folders} folders ({result.Hidden} hidden)\n" +
            $"Also counting inside: {merged}";

        typeList.BeginUpdate();
        typeList.Items.Clear();
        foreach (var kv in result.ByExtension.OrderByDescending(kv => kv.Value))
            typeList.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value.ToString() }));
        typeList.EndUpdate();
    }

    // ---------- Save ----------

    void SaveAndClose()
    {
        try { StartupSetting.Set(startWithWindows.Checked); }
        catch (Exception ex) { Log.Write($"Could not change start-with-Windows: {ex.Message}"); }

        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't save settings: " + ex.Message, "FolderBadges");
            return;
        }

        Saved?.Invoke(settings);
        Close();
    }

    // ---------- Small helpers ----------

    static List<string> ParseList(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    static T JsonClone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    static CheckBox MakeCheck(string text) => new() { Text = text, AutoSize = true };

    static TextBox MakeText() => new() { Width = 420, Margin = new Padding(3, 0, 3, 8) };

    static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 2, 6, 2)
        };
        b.Click += onClick;
        return b;
    }

    static Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(3, 6, 3, 6)
    };

    static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 2) };

    static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(6, 8, 3, 0)
    };

    static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        panel.Controls.AddRange(controls);
        return panel;
    }
}