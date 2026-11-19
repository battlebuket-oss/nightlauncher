# Security policy

## Scope

NightLauncher is a local Windows game-library manager. It can start an executable explicitly selected by the user or open a Steam protocol URI containing a validated numeric App ID. Steam protocol requests are handed to the registered Steam client; Steam is responsible for authentication, entitlement checks and the actual game launch.

It intentionally does not:
- read, store, or request Steam, Rockstar, or other launcher credentials;
- bypass DRM, authentication, licence, or ownership checks;
- patch game executables, manifests, launchers, or libraries;
- inject DLLs, attach to game processes, or read/write process memory;
- download executable content or run remote scripts;
- silently elevate privileges;
- scan the PC or enumerate arbitrary folders without the user's action.

## Reporting a vulnerability

Please do not publish exploit details in a public issue. Use GitHub's private vulnerability reporting feature for this repository where available. Include the affected version, Windows version, reproduction steps, and impact. Do not include passwords, access tokens, or personal game-account data.

## User guidance

Download binaries only from this repository's GitHub Actions artifacts or GitHub Releases. Initial builds are unsigned, so Windows may display a SmartScreen warning. Review the source and build locally if you prefer not to run an unsigned binary.
