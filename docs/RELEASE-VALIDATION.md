# Release validation checklist

This checklist separates CI-verifiable checks from tests that require a real interactive Windows session.

## GitHub Actions checks

- [ ] CodeQL C# security analysis completes and findings are reviewed.

- [ ] Windows x64 Release build succeeds.
- [ ] Smoke tests pass (library persistence, Steam URI and Steam library discovery, backup/restore, retention, discovery limits, invalid inputs, favorite persistence/import/export, category editing/filtering, library sort modes, settings preference round-trips, and diagnostics export limits/content selection).
- [ ] WPF windows initialize and compiled XAML fields resolve on an STA thread.
- [ ] NuGet vulnerability report is reviewed.
- [ ] Self-contained EXE publishes.
- [ ] Inno Setup creates the per-user installer.
- [x] CI installs the generated setup EXE as a standard user, verifies the application and shortcut, uninstalls it, and confirms library/settings/backups remain intact.
- [ ] EXE, portable ZIP, installer, and SHA256SUMS manifest are uploaded as release assets.
- [ ] Publisher verifies every file against SHA256SUMS.txt before release upload.
- [ ] Confirm PR/build jobs have read-only repository permissions and only the gated publisher job receives contents write permission.
- [ ] A versioned release tag is rejected when no trusted signing certificate is configured.
- [ ] For a signed versioned release, Authenticode signatures verify for both EXE and installer.

## Interactive Windows 10 and Windows 11 checks

- [ ] Install as a standard user without administrator elevation.
- [ ] Launch from Start Menu shortcut and optional desktop shortcut.
- [ ] Add, edit, remove, search, import, and export game entries.
- [ ] Add a Steam App ID and confirm the request opens the official Steam client.
- [ ] Scan installed Steam libraries automatically and from a manually selected portable/custom library folder; verify entries refresh their saved install path, the details pane opens valid local folders, legacy libraries without the new field still load, and no manifests or credentials are changed.
- [ ] Scan a user-selected folder, select candidate executables, and verify no file is launched during discovery.
- [ ] Confirm discovered/imported entries require a one-time trust review before launching.
- [ ] Select a custom backup folder and restart the app to confirm the preference persists.
- [ ] Create multiple recovery points, select a non-latest backup, and restore it.
- [ ] Create a backup, make a library change, and restore the previous backup.
- [ ] Confirm restoring creates a safety backup of the current library.
- [ ] Uninstall and confirm the user's library, backups, and logs remain intact.
- [ ] Verify keyboard navigation (Ctrl+F, Escape, Enter, Delete), high contrast, scaling, and screen-reader labels.

The GitHub-hosted Windows runner can validate compilation, service tests, XAML initialization, and packaging. It does not substitute for these interactive desktop checks on physical or virtual Windows installations.
