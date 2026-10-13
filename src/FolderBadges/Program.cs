namespace FolderBadges;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Only allow one copy of the app to run at a time
        using var mutex = new Mutex(true, @"Local\FolderBadges", out bool isFirstCopy);
        if (!isFirstCopy)
        {
            MessageBox.Show("FolderBadges is already running. Look for it in the tray (bottom right).",
                "FolderBadges");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}