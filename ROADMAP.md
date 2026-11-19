# Roadmap

## Initial release foundation
- [x] Local game library and JSON persistence
- [x] Add/edit/remove entries
- [x] Search and launch user-selected executables
- [x] JSON import/export
- [x] Self-contained Windows publishing workflow
- [x] Smoke tests and security documentation
- [x] Official Steam protocol hand-off by validated App ID
- [x] Backward-compatible loading of pre-1.1.0 library files

## Release hardening
- [ ] Authenticode signing and timestamped release pipeline
- [ ] Installer and uninstall support
- [ ] User-selectable library backup location and scheduled local backups
- [ ] Optional game discovery limited to user-selected folders
- [ ] Accessible keyboard navigation and high-contrast testing
- [ ] Automated UI tests on a Windows runner
- [ ] Broader automated tests for malformed imports, persistence failures and launch errors
- [ ] More detailed diagnostics and log rotation
- [ ] Manual compatibility testing on Windows 10 and Windows 11

Any future feature must preserve the no-DRM-bypass, no-credential-collection, and local-first security boundaries.
