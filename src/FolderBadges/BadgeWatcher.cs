namespace FolderBadges;

/// <summary>Watches one badged folder (plus any merged folders) and keeps its badge up to date.</summary>
public sealed class BadgeWatcher : IDisposable
{
    public string Folder { get; }
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder));
    public CountRules Rules { get; }
    public CountResult? LastResult { get; private set; }

    /// <summary>Raised on the UI thread after each recount.</summary>
    public event Action? Updated;

    readonly Control ui;
    readonly System.Threading.Timer timer;
    readonly object countLock = new();
    readonly Dictionary<string, FileSystemWatcher> fsWatchers = new(StringComparer.OrdinalIgnoreCase);
    int lastBadge = -1;
    volatile bool disposed;

    public BadgeWatcher(string folder, CountRules rules, Control ui)
    {
        Folder = folder;
        Rules = rules;
        this.ui = ui;
        timer = new System.Threading.Timer(_ => CountInBackground());
    }

    public void Start() => RecountSoon(0);

    /// <summary>Recount and re-apply the badge even if the number hasn't changed.</summary>
    public void ForceRefresh()
    {
        lastBadge = -1;
        RecountSoon(0);
    }

    /// <summary>Schedules a recount. Called repeatedly during busy activity, it only runs once things go quiet.</summary>
    public void RecountSoon(int delayMs = 750)
    {
        if (disposed) return;
        try { timer.Change(delayMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    // Runs on a background thread so a big count never freezes the tray menu.
    void CountInBackground()
    {
        if (disposed) return;

        CountResult result;
        lock (countLock)
        {
            try
            {
                result = new FileCounter(Rules).Count(Folder);
            }
            catch (Exception ex)
            {
                Log.Write($"Count failed for {Folder}: {ex.Message}");
                return;
            }
        }

        RunOnUi(() => Apply(result));
    }

    // Runs on the UI thread.
    void Apply(CountResult result)
    {
        if (disposed) return;
        LastResult = result;

        if (result.BadgeCount != lastBadge)
        {
            try
            {
                FolderIcon.SetBadge(Folder, result.BadgeCount);
                lastBadge = result.BadgeCount;
            }
            catch (Exception ex)
            {
                Log.Write($"Setting badge failed for {Folder}: {ex.Message}");
            }
        }

        UpdateWatchers(result);
        Updated?.Invoke();
    }

    /// <summary>Watches the badged folder plus every folder merged into it (e.g. Downloads).</summary>
    void UpdateWatchers(CountResult result)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Folder };
        wanted.UnionWith(result.MergedFolders);
        wanted.RemoveWhere(p => !Directory.Exists(p));

        // Stop watching folders that are no longer relevant
        foreach (var path in fsWatchers.Keys.Where(p => !wanted.Contains(p)).ToList())
        {
            fsWatchers[path].Dispose();
            fsWatchers.Remove(path);
        }

        // Start watching new ones
        foreach (var path in wanted)
        {
            if (fsWatchers.ContainsKey(path)) continue;
            try
            {
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = Rules.Depth != 1, // only need subfolders if we count inside them
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                    InternalBufferSize = 64 * 1024
                };
                w.Created += OnFileEvent;
                w.Deleted += OnFileEvent;
                w.Changed += OnFileEvent;
                w.Renamed += OnFileEvent;
                w.Error += (_, e) =>
                {
                    Log.Write($"Watcher error on {path}: {e.GetException().Message}");
                    RecountSoon();
                };
                w.EnableRaisingEvents = true;
                fsWatchers[path] = w;
            }
            catch (Exception ex)
            {
                Log.Write($"Could not watch {path}: {ex.Message}");
            }
        }
    }

    void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Our own icon changes touch desktop.ini; ignore those so we don't loop.
        if (Path.GetFileName(e.FullPath).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            return;
        RecountSoon();
    }

    void RunOnUi(Action action)
    {
        if (disposed || ui.IsDisposed || !ui.IsHandleCreated) return;
        try { ui.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
        foreach (var w in fsWatchers.Values) w.Dispose();
        fsWatchers.Clear();
    }
}