# Branvik Emulator Updater

A Playnite plugin built with C#, WPF, Playnite SDK 6.18.0 and .NET Framework 4.6.2. English is the default interface language.

## Build and debug

Open `Branvik.EmulatorUpdater.csproj` in Visual Studio with the .NET desktop workload. Restore NuGet packages and build Debug. Add `bin/Debug/net462` to Playnite's external extensions in its developer settings.

Copy `Properties/launchSettings.example.json` to `Properties/launchSettings.json` and adjust the paths for your Playnite installation. The local file is excluded from Git. Select the `Playnite Desktop` launch profile to start Playnite with the debugger using F5. Stop debugging with Shift+F5 before rebuilding. Close any independently running Playnite instance first.

## Interface

Open the plugin using the download-arrow button in Playnite Desktop's top panel. Its tooltip is **Branvik Emulator Updater**. Clicking it again activates the existing window. The plugin no longer adds an entry to the main menu.

The bottom toolbar contains icons with tooltips:

- Left: **Synchronize**, **Check for updates**, **Update**.
- Right: **Add emulator manually**, **Remove emulator**.

Check and Update operate on selected rows. Use Ctrl or Shift to select multiple rows; Ctrl-click selected rows to deselect them. Remove is available only when every selected row was added manually. Removing an entry does not delete its files. Text edits and checkbox changes are saved automatically.

Enter a GitHub repository as `owner/repo`. The installed tag is optional; without it the plugin reports the available release without deciding whether it is newer. Include prereleases selects the newest publication among the first 100 releases, including stable releases. Nightlies hosted outside GitHub Releases and changes under an unchanged tag are not detected.

## Installation support

- `PCSX2/pcsx2`: Windows x64 Qt portable archives; the target folder must contain `pcsx2-qt.exe`.
- `RPCS3/rpcs3-binaries-win`: Windows x64 portable archives, including `_win64_msvc.7z`; the target folder must contain `rpcs3.exe`.

Other repositories support release checks only. Updates require 7-Zip installed in Program Files and a closed emulator. The plugin refreshes release information and asks for confirmation before installation. Review the tag: changing channels may select an older build.

Downloads are staged separately, checked for size and SHA-256 when GitHub supplies a digest, and extracted before installation. Replaced files are backed up to `BranvikBackups/<id>/files` beside the emulator folder. Settings and known user-data paths are excluded from replacement, including RPCS3 `dev_*`, `config`, `GuiConfigs` and `patches`. Files absent from the update are not removed.

Copy errors trigger rollback. Incomplete rollback reports the backup path. Recovery from power loss or forced process termination is not guaranteed. For manual recovery, close the emulator, restore the backup's `files` into the path recorded in `installation.txt`, and review `added-files.txt` for files introduced by the update. Correct the installed tag afterwards.

## Validation

Build with `dotnet build --no-restore -o bin/InstallerCheck`. Run the Windows PowerShell scripts in `tests` to check release selection, replacement, backup, rollback and RPCS3 data preservation. Tests use their own fixture directories. Runtime appearance depends on the active Playnite theme; full installation and visual verification inside Playnite require a separate run.
