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

        // If something goes badly wrong, write it to the log so there's a clue what happened
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"Unexpected error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Crash: {e.ExceptionObject}");

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}