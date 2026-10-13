namespace FolderBadges;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // TEMPORARY TEST: count the Inbox folder on the Desktop
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string inbox = Path.Combine(desktop, "Inbox");

        var counter = new FileCounter();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int count = counter.Count(inbox);

        MessageBox.Show(
            $"{inbox}\n\n{count} items\n(counted in {timer.ElapsedMilliseconds} ms)",
            "FolderBadges test");
    }
}