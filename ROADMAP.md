# Roadmap

## Implemented
- [x] Local game library and JSON persistence
- [x] Add/edit/remove entries and search
- [x] Direct executable launching and official Steam protocol hand-off
- [x] Local Steam library discovery from libraryfolders and appmanifest metadata, with bounded parsing and install-path validation
- [x] JSON import/export with validation and imported-entry confirmation
- [x] Backward-compatible loading of pre-1.1.0 library files
- [x] Bounded discovery in a folder explicitly selected by the user
- [x] Manual candidate selection before adding discovered executables; discovered entries require first-launch confirmation
- [x] Automatic pre-change local backups with 20-copy retention
- [x] Manual backup creation and validated restore with a pre-restore safety copy
- [x] Persist a user-selected backup directory in local settings
- [x] Select any available recovery point in Settings for restore
- [x] Local log rotation
- [x] Per-user Windows installer and uninstaller definition
- [x] Windows CI installs and uninstalls the generated setup EXE, verifies the Start Menu shortcut, and checks that library/settings/backup data survives
- [x] CI packaging for EXE, portable ZIP, and installer
- [x] Optional Authenticode signing support in CI (certificate configuration remains external)
- [x] Expanded smoke tests for Steam URI and installed-library discovery, malformed manifests, secondary libraries, path traversal, legacy data, backup/restore, retention, discovery limits, and invalid restore paths
- [x] WPF XAML/window initialization checks on an STA thread in Windows CI
- [x] NuGet vulnerability check in Windows CI

## Remaining release hardening
- [ ] Configure a trusted publisher Authenticode certificate in repository secrets and verify signed artifacts
- [ ] Run the installer and uninstaller on a clean Windows 10 VM and a clean Windows 11 VM; confirm user data is preserved
- [ ] Complete manual keyboard navigation, screen-reader, and high-contrast testing
- [ ] Add UI automation that interacts with the live app window on an interactive desktop runner
- [x] Add malformed settings/library JSON and blocked-storage failure tests
- [ ] Perform final code review and release signing verification

Any future feature must preserve the no-DRM-bypass, no-credential-collection, and local-first security boundaries.
