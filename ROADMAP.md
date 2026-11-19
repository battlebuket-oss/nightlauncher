# Roadmap

## Implemented
- [x] Local game library and JSON persistence
- [x] Add/edit/remove entries and search
- [x] Direct executable launching and official Steam protocol hand-off
- [x] JSON import/export with validation and imported-entry confirmation
- [x] Backward-compatible loading of pre-1.1.0 library files
- [x] Automatic pre-change local backups with 20-copy retention
- [x] Manual backup creation and validated restore with a pre-restore safety copy
- [x] Local log rotation
- [x] Per-user Windows installer and uninstaller definition
- [x] CI packaging for EXE, portable ZIP, and installer
- [x] Optional Authenticode signing support in CI (certificate configuration remains external)
- [x] Expanded smoke tests for Steam URI validation, legacy data, backup/restore, retention, and invalid restore paths
- [x] NuGet vulnerability check in Windows CI

## Remaining release hardening
- [ ] Configure a trusted publisher Authenticode certificate in repository secrets and verify signed artifacts
- [ ] Run the installer and uninstaller on a clean Windows 10 VM and a clean Windows 11 VM; confirm user data is preserved
- [ ] Add interactive UI automation on a desktop-capable Windows runner
- [ ] Complete manual keyboard navigation, screen-reader, and high-contrast testing
- [ ] Add optional game discovery limited to a folder explicitly selected by the user; require manual selection before adding any executable
- [ ] Add user-selectable backup location and backup restore point picker
- [ ] Add broader malformed-library and disk-failure tests
- [ ] Perform final code review and release signing verification

Any future feature must preserve the no-DRM-bypass, no-credential-collection, and local-first security boundaries.
