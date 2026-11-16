# 0.1.0 test build

This build is for small-scale usability and compatibility testing. It has not completed every requirement for a public stable release.

## Included

- .NET 10 LTS, with the desktop runtime included in default packages. Existing items, settings and workspace data are preserved.
- One program with live Simplified Chinese / English switching, a saved language choice and a bilingual installer.
- A tray-resident floating shelf with show, collapse, hide and position memory.
- The shelf recalculates its size and position when Windows display scaling changes.
- File, folder, text and image collection; multi-selection; paths, text and RTF text/image copying.
- Workspace groups, search, type filters and image/video previews.
- Confirmed moves with conflict-safe names and one-level undo; readable display-name suggestions.
- Cleanup settings, single-click behavior, optional startup and system-theme following.
- Self-contained portable and current-user installer packages with Chinese/English guides, license notices and SHA-256 files.

## Limits

- No network access, accounts, telemetry or automatic message sending. Automatic collection is off by default.
- Chat apps may accept only one clipboard format per paste. Text can be copied separately.
- Video previews depend on Windows codecs.
- Folders cannot be moved between drives; move undo lasts for the current process only.
- Multi-monitor scaling, input focus, cross-app delivery and long-running resource use still need hands-on verification.
- First launch has taken about 2 seconds, above the 1.5-second target. Faster later launches do not count as passing the first-launch check.
- The build is unsigned and has no online update feature.

The v0.2/v0.3 labels in planning documents describe planned feature stages. The current test-package version remains 0.1.0.
