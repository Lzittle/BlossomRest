# Blossom Rest user guide

Version: 0.1.0 test build. Windows 10 1809+ / Windows 11, x64.

## Install, start and choose a language

Run `BlossomRest-0.1.0-win-x64-setup.exe` and follow the Chinese or English wizard. Installation is per user, without administrator rights. It does not enable automatic startup or launch the app for you. Open Blossom Rest from the Start menu after installation.

For the portable version, extract the entire ZIP and run `TransitShelf.exe` inside `BlossomRest-win-x64`. Keep the supporting files beside the executable. The default package includes the .NET runtime.

In Settings, choose Follow system, 简体中文 or English under Language. The interface updates immediately and the choice survives restart. Existing state files without a language field keep Chinese; new installations follow Windows. User-entered names, text, paths and groups stay unchanged. Standard system file-picker buttons follow Windows' own language.

[English settings preview](images/settings.en.png)

## Add and use items

1. Drop files, folders, text or images onto the panel, or choose Add files / Add folder.
2. Press `Ctrl+Alt+C` to collect the clipboard. Press `Ctrl+Alt+D` to show or hide the panel.
3. By default, click folders to open them and other items to copy them. Double-click to open. You can change the single-click action in Settings.
4. Enter selection mode to copy several items. Files, paths and text are offered together; the destination chooses the format it accepts.
5. In a chat app that only accepts files in the first paste, use Copy text for a second paste. Word, WPS and rich-text email editors can accept text with images together.
6. Enable Workspace to organize long-lived references into groups. Shelf expiry and item limits do not apply to Workspace groups.

## Source files and saved data

Removing a shelf item never deletes its source file. Ordinary file and folder items store paths rather than content copies.

Move to folder and source-file renaming do change the real file system. Read the confirmation before continuing. Moves add a number when a name is already in use. Undo move applies only to the most recent move in the current session; folders cannot be moved between drives.

Suggest a name changes only the shelf display name. It does not rename the source file.

State is saved at `%LOCALAPPDATA%\TransitShelf\state.json`. Source-less images are saved as PNGs in `cache` beside it. Exit the app before backing up the complete data folder. Both portable and installed versions use this location. Updating or uninstalling the program keeps user data; normal expiry and clear-on-exit settings still apply when you run the app.

## Find the floating button or exit

Double-click the tray icon, choose Show Blossom Rest, or press `Ctrl+Alt+D`. Hiding the floating button keeps the process running. To quit completely, use Exit in the tray menu.

## Test-build limits

This build is unsigned and has no online updater. Only use packages from a trusted source and compare the download with its `.sha256` file. Do not disable Windows security features to install it.

More testing is needed for multi-monitor scaling, input methods and destination apps. Video thumbnails depend on installed Windows codecs; unavailable previews fall back to an item icon. When reporting a problem, include the steps, Windows scaling and destination-app version. Hide private paths and text in screenshots.
