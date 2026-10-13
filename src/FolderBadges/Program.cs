namespace FolderBadges;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // TEMPORARY TEST
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string inbox = Path.Combine(desktop, "Inbox");

        var rules = new CountRules
        {
            Depth = 1,
            MergeFolders = { "Downloads" },   // un-comment if Downloads is a REAL folder inside Inbox, not a shortcut
        };

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new FileCounter(rules).Count(inbox);

        string merged = result.MergedFolders.Count == 0
            ? "   (none)"
            : string.Join("\n", result.MergedFolders.Select(p => "   " + p));

        string topTypes = string.Join("\n", result.ByExtension
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => $"   {kv.Key}: {kv.Value}"));

        MessageBox.Show(
            $"{inbox}\n\n" +
            $"BADGE NUMBER: {result.BadgeCount}\n\n" +
            $"Files: {result.Files}\nFolders: {result.Folders}\nHidden: {result.Hidden}\n\n" +
            $"Merged in:\n{merged}\n\n" +
            $"Top file types:\n{topTypes}\n\n" +
            $"({timer.ElapsedMilliseconds} ms)",
            "FolderBadges test");
    }
}