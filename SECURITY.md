# Security policy

## Scope

NightLauncher is a local Windows game-library manager. It can start an executable explicitly selected by the user or open a Steam protocol URI containing a validated numeric App ID. Steam protocol requests are handed to the registered Steam client; Steam is responsible for authentication, entitlement checks and the actual game launch.

NightLauncher intentionally does not:
- read, store, or request Steam, Rockstar, or other launcher credentials;
- bypass DRM, authentication, licence, or ownership checks;
- patch game executables, manifests, launchers, or libraries;
- inject DLLs, attach to game processes, or read/write process memory;
- download executable content or run remote scripts;
- silently elevate privileges;
- scan arbitrary folders or run discovered executables without the user's explicit selection.

## Local data and backups

The game library, settings, default rolling backups, and logs are stored under the current Windows user's Local AppData directory. The local settings file stores only the user-selected backup folder path. Backups contain game names, selected executable paths, launch arguments, and metadata; they do not contain store credentials. A user-selected backup folder may be on another local or external drive. Anyone with access to the same Windows user profile or selected backup folder may be able to read these files. Restored launch entries require a one-time confirmation before execution. The user-triggered diagnostics exporter includes only the bounded `latest.log` and rotated `latest.1.log`–`latest.3.log` files plus a basic app/runtime summary; it excludes the library, backups, and settings file. Log contents may still contain local file paths or exception details, so users should review the ZIP before sharing it.

The per-user installer does not require administrator privileges. Uninstalling NightLauncher removes the application files and shortcuts but intentionally leaves the user's library, backups, and logs in place.

## Release signing

The Windows CI workflow can Authenticode-sign the executable and installer when both `WINDOWS_SIGNING_CERTIFICATE_BASE64` and `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` GitHub Actions secrets are configured. Without those secrets, releases are unsigned development builds. A successful compile or package operation is not proof that a binary is signed or trusted.

## Reporting a vulnerability

Please do not publish exploit details in a public issue. Use GitHub's private vulnerability reporting feature for this repository where available. Include the affected version, Windows version, reproduction steps, and impact. Do not include passwords, access tokens, or personal game-account data.

## User guidance

Download binaries only from this repository's GitHub Actions artifacts or GitHub Releases. When releases are unsigned, Windows may display a SmartScreen warning. Review the source and build locally if you prefer not to run an unsigned binary.
