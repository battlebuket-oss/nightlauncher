# Roadmap

## Initial release foundation
- [x] Local game library and JSON persistence
- [x] Add/edit/remove entries
- [x] Search and launch user-selected executables
- [x] JSON import/export
- [x] Self-contained Windows publishing workflow
- [x] Smoke tests and security documentation

## Follow-up work
- [ ] Authenticode signing and timestamped release pipeline
- [ ] Installer and uninstall support
- [ ] User-selectable library backup location
- [ ] Optional directory scan limited to a folder explicitly selected by the user
- [ ] Accessible keyboard navigation and high-contrast testing
- [ ] Automated UI tests on a Windows runner
- [ ] More detailed diagnostics and log rotation

Any future feature must preserve the no-DRM-bypass, no-credential-collection, and local-first security boundaries.
