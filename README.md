# FolderBadges

A small Windows 11 tray app that puts a red **count badge** on folder icons, like the unread count on a phone app. (It may work on Windows 10, but I've only tested it on Windows 11.)

## Why I made this

I made this for my own personal use. I keep a folder called **Inbox** on my Desktop, and everything new lands there first: loose files, and my Downloads folder, which lives inside it. The idea is that Inbox works like an actual inbox. Things arrive, and I sort them into their proper homes, which keeps my Desktop clean.

The problem was that a folder doesn't tell you anything until you open it. I wanted a badge on the icon that shows how much is waiting, so I'd notice when it was time to tidy up. Windows doesn't do that, so I built it.

It's shared here in case it's useful to anyone else. It's a personal project, so expect it to be shaped around how I work.

**Built with Claude:** I'm learning C#, and I built this app with the help of [Claude](https://claude.ai), Anthropic's AI assistant, using Claude Code.

## What it does

- Shows a red badge with the number of items on any folder you choose. Above 99 it shows **99+**. When the folder is empty, the badge disappears and the normal icon comes back.
- Updates live as files are added, moved or deleted.
- Can **merge** a folder inside it, so its contents count too. For example, Downloads inside Inbox counts every download rather than just "1 folder".
- Lets you choose per folder what counts: how many levels deep, files and/or folders, hidden items, and which file types.
- Sits quietly in the system tray, and can start with Windows.

## Download and install

1. Go to the [latest release](https://github.com/fusi3/windows-folder-badges/releases/latest) and download **`FolderBadges.exe`**.
2. Double-click it.
3. Windows will probably show **"Windows protected your PC"**. That's because the app isn't signed with a paid certificate, not because anything was found in it. Click **More info**, then **Run anyway**.
4. The Settings window opens. Click **Add folder…**, pick a folder, and click **Save**.
   (If you have a folder called `Inbox` on your Desktop, it's added automatically and gets a badge straight away. Double-click the tray icon to open Settings.)

The app now runs in the system tray (bottom right, next to the clock). On Windows 11 it may be hidden behind the **^** arrow. To keep it visible, go to **Settings → Personalization → Taskbar → Other system tray icons** and turn on FolderBadges.

### Which file should I download?

| File | Size | Use it if… |
|---|---|---|
| `FolderBadges.exe` | ~50 MB | You're not sure. It works without installing anything, because .NET is packed inside. |
| `FolderBadges-small.exe` | ~0.2 MB | You already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed. |

They're the same app.

### Starting with Windows

Tick **Start FolderBadges when Windows starts** in Settings and click **Save**. The app copies itself to `%LOCALAPPDATA%\Programs\FolderBadges\` and starts from there, so you can delete the file you downloaded.

To update to a newer version, run the new download, open Settings and click **Save** again. That replaces the copy.

## Using it

- **Double-click** the tray icon to open Settings.
- **Right-click** it to see each folder's count, refresh, open the log, or exit.
- Always close the app with **right-click → Exit (removes badges)**. That puts every folder's original icon back. If the app is closed any other way (crash, Task Manager), the last badge stays on the folder until you start the app again.
- If a badge looks out of date, press **F5** on the Desktop or in Explorer, or use **Refresh now** from the tray menu.

## How counting works

Each folder has its own rules, set in the Settings window. The breakdown at the bottom shows what was found as you change them.

- **Levels deep:** `1` (the default) counts only what's directly inside. A folder counts as 1 item without being opened, so a downloaded app folder with 500 files is still just 1. `0` means count everything, all the way down.
- **Count files / Count folders:** choose what adds to the number.
- **Include hidden items:** off by default, matching what Explorer normally shows.
- **Open shortcuts to folders:** if the folder has a shortcut to another folder (for example a Downloads shortcut), count what's inside that folder instead of the shortcut itself.
- **Also open these subfolders:** the same idea for real folders. Type `Downloads` and its contents count as if they sat directly in the badged folder.
- **Only count / Never count these file types:** for example `pdf, jpg` or `tmp, crdownload`.

`desktop.ini`, `Thumbs.db` and Windows system files are never counted.

## What it changes on your PC

FolderBadges never moves, renames or deletes your files. To show a badge, it does what **Properties → Customize → Change Icon** does:

- It writes the icon setting into the folder's hidden `desktop.ini` file, and marks the folder read-only, which is how Windows knows to look for a custom icon.
- Before the first change, it saves a backup of the original `desktop.ini` (or notes there wasn't one) and the folder's original read-only state. When a badge is removed, both are put back exactly as they were.

Everything the app stores is in `%LOCALAPPDATA%\FolderBadges\`:

| Item | What it is |
|---|---|
| `settings.json` | Your folders and their rules |
| `log.txt` | A short activity log (resets at about 1 MB) |
| `icons\` | The badge images, one per number |
| `backups\` | The original folder settings, used to restore icons |

"Start with Windows" adds a `FolderBadges` entry under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

## Uninstall

1. Open Settings, untick **Start FolderBadges when Windows starts**, and click **Save**.
2. Right-click the tray icon and choose **Exit (removes badges)**. Your folders get their normal icons back.
3. Delete these folders:
   - `%LOCALAPPDATA%\Programs\FolderBadges` (the app, if you used Start with Windows)
   - `%LOCALAPPDATA%\FolderBadges` (settings, log, icons and backups)

   You can paste each path into the File Explorer address bar to open it.
4. Delete the `.exe` you downloaded.

Do step 2 before step 3: the `backups` folder is what the app uses to restore your original icons.

## Building it yourself

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

Run it while developing:

```
dotnet run --project src\FolderBadges
```

Build the two release files into a `dist` folder:

```
.\build-release.ps1
```

The code is in `src/FolderBadges/`. It's C# with WinForms, and the whole UI is built in code (no designer files).

## Licence

MIT. See [LICENSE](LICENSE).
