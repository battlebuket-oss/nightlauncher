# NightLauncher

**A free, native Windows game-library and launch manager.** Add games installed on your PC, keep their launch targets in one local library, and start them with a click.

> **Important:** NightLauncher is not a crack, loader, emulator, launcher replacement, or DRM bypass. It does not grant a game licence, authenticate accounts, defeat ownership checks, disable DRM, or make an unlicensed copy playable. Games that require Steam, Rockstar Games Launcher, or another platform must still pass that platform's normal ownership and authentication checks.

## Features

- Native Windows desktop UI (WPF, .NET 10).
- Add and edit games using a directly selected executable.
- Add a Steam game using its numeric Steam App ID.
- Discover installed Steam games across the configured local Steam libraries by reading `libraryfolders.vdf` and `appmanifest_*.acf`; when automatic detection misses a portable or custom Steam install, select its library folder manually. Steam metadata parsing has per-file and total scan limits, uses bounded reads, rejects reparse-point folders and nested install paths, and never modifies Steam files. Newly discovered entries require a one-time review and launch through the official Steam client.
- Steam entries are handed to the official Steam client using its registered `steam://rungameid/<appid>` protocol.
- The details pane shows a detected install location and opens valid local game folders in Explorer; rescanning Steam refreshes existing names and install paths.
- Keyboard shortcuts: Ctrl+F focuses search, Escape clears search, Enter launches the selected game, and Delete asks to remove the selected game.
- Steam rescans refresh stored game names and install locations; the details pane shows a validated install folder and can open it in Windows Explorer.
- Optional launch arguments for direct executable entries.
- Search and browse a local game library; combine text search, category, and favorites-only filters.
- Scan only a folder explicitly selected by the user (bounded to four directory levels and 500 candidates by default); discovered executables are never auto-added or launched.
- Folder and Steam library discovery run on background tasks so scanning does not block normal window interaction.
- Import and export the library as JSON.
- Local-only storage under the user's Local AppData folder.
- Automatic pre-change backups with a rolling retention limit of 20 copies.
- User-selectable backup storage and a selectable recovery-point list in Settings; restore validates the selected file and marks launch targets for confirmation.
- Backward-compatible loading of library files created before Steam launch support.
- Imported entries require a one-time confirmation before launch.
- Dependabot checks NuGet packages and GitHub Actions weekly and opens update pull requests.
- Windows build/test jobs use read-only repository permissions; a separate publisher job receives release-write permission only for main-branch and version-tag pushes.
- CodeQL runs extended C# security queries on pull requests, main-branch pushes, and a weekly schedule.
- No telemetry, cloud account, credential collection, game-file patching, DLL injection, process-memory access, or automatic network calls.

## Steam launch mode

1. Select **Add a game**.
2. Choose **Official Steam client** as the launch method.
3. Enter the game's numeric Steam App ID.
4. Save the entry and select **Launch game**.

For example, the Steam App ID for GTA V Legacy is `271590`. NightLauncher sends that ID to the Steam client. Steam performs its normal sign-in, entitlement and launch checks; NightLauncher cannot skip them or make an unlicensed installation playable. For games purchased through Steam that also use a publisher launcher, the publisher's normal authentication flow remains in effect.

## Download and installation

After the Windows build, smoke tests, vulnerability check, and packaging steps pass, the workflow publishes a CI prerelease containing the self-contained executable, portable ZIP, and per-user Windows installer. The release also includes `SHA256SUMS.txt`, covering the self-contained EXE, installer, and portable ZIP. The publishing job verifies the manifest before uploading. On Windows, compare a downloaded file with its manifest entry using `Get-FileHash -Path "NightLauncher.exe" -Algorithm SHA256` (use the corresponding filename for the installer or ZIP). The Actions run also retains the artifacts for 30 days.

For a stable, versioned GitHub Release, push a tag matching the application version, such as `v1.3.0`. The workflow attaches the executable, portable ZIP, and installer only after Authenticode signing and signature verification succeed. A tag with a mismatched version is rejected.

Stable tagged releases require a trusted Authenticode certificate. Configure the GitHub Actions secrets `WINDOWS_SIGNING_CERTIFICATE_BASE64` (a base64-encoded PFX file) and `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`. When the certificate is absent, the workflow blocks stable releases rather than publishing unsigned binaries. Main-branch CI prereleases remain available for testing and are unsigned when the certificate is not configured; Windows SmartScreen may show an unknown-publisher warning. Only download builds from this repository, and review the source before running it.

The Inno Setup installer installs per-user under `%LocalAppData%\\Programs\\NightLauncher`, adds a Start Menu shortcut, and optionally adds a desktop shortcut. Uninstalling the application intentionally leaves the local library and backups untouched.

## Run from source

Requirements: Windows 10/11, .NET 10 SDK, and the Windows Desktop workload included with the SDK.

    dotnet build src/NightLauncher/NightLauncher.csproj -c Release
    dotnet run --project src/NightLauncher/NightLauncher.csproj

## Build a self-contained executable

    dotnet publish src/NightLauncher/NightLauncher.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

The output is NightLauncher.exe in the publish folder. It does not require a separately installed .NET runtime.

## Run smoke tests

    dotnet run --project tests/NightLauncher.SmokeTests/NightLauncher.SmokeTests.csproj -c Release

The Windows smoke suite covers library persistence, JSON import/export, legacy library compatibility, Steam library discovery (including secondary libraries, missing installs, path traversal, and malformed manifests), Steam URI generation, backup/restore/retention, custom backup preference persistence, malformed settings/library files, storage failures, selected-folder discovery limits, launch-target validation, and WPF recovery-list initialization. The Windows workflow also checks NuGet packages for known vulnerabilities before creating the installer and release artifacts.

## Add a direct executable game

1. Start NightLauncher and select **Add a game**.
2. Choose **Direct executable**.
3. Enter a display name and browse to the game's executable.
4. Optionally add launch arguments. Leave them blank unless the game's official documentation requires them.
5. Save, select the game in the library, and press **Launch**.

Select any game and use the **Category** field to assign a label such as Action, RPG, Racing, or Strategy. Use the category filter beside search to show one category, or combine it with the search box and **Favorites only** checkbox. Use **Add to favorites** in the details pane to pin favorite games above non-favorites. Categories and favorite status are stored locally and preserved by import/export and backups. Choose Name A–Z, Recently played, or Recently added in the sort selector; NightLauncher remembers that choice across restarts, favorites stay pinned first in every mode, and changing the backup folder does not overwrite this preference. Older library files remain compatible. Open **Settings → Diagnostics** to open the local log folder or export a ZIP. The ZIP excludes the library, backups, and settings; logs may still contain local file paths, so review it before sharing.

## Local data, backups and privacy

The library is stored at:

    %LocalAppData%\NightLauncher\library.json

The default backup location is:

    %LocalAppData%\NightLauncher\backups

You can choose another local or external folder in **Settings & privacy**. NightLauncher remembers the chosen folder in its local `settings.json` preference file. NightLauncher creates a backup before each change to an existing library and retains the 20 newest copies in the selected backup folder. You can choose a different folder (including a removable or external drive), create an additional backup, and select any listed recovery point from **Settings & privacy**. Changing the folder does not move or delete backups already stored in the previous location. Restoring validates the file, first preserves the current library, and requires one-time confirmation before restored launch targets can run. Logs are stored locally under `%LocalAppData%\NightLauncher\logs` and rotated to bounded files. Export an additional backup to an external drive before reinstalling Windows or deleting the application data folder.

## Security model

NightLauncher starts only the executable path explicitly selected by the user or opens a Steam protocol URI containing a validated numeric App ID. Steam URI requests are handed to the registered Steam client, which is responsible for sign-in, ownership and game launch. NightLauncher does not read or modify Steam/Rockstar credentials, modify app manifests, patch game executables, or inspect game processes. Its optional Steam discovery reads local `libraryfolders.vdf` and `appmanifest_*.acf` metadata only to identify installed App IDs, display names, and installation folders; it does not interpret that metadata as proof of ownership. Steam performs its normal sign-in, entitlement, and launch checks. It does not elevate privileges automatically, accept remote commands, load plugins, run scripts, download DLLs, or bypass DRM.

## Project status

Version 1.9.1 hardens library, diagnostics, and settings writes by using unique temporary files that cannot overwrite neighboring user files. Version 1.9.0 added a favorites-only filter that works together with category and text search, alongside the user-triggered diagnostics ZIP export with bounded log inclusion and a minimal runtime summary. Recent releases also add persistent sort preferences, alphabetical/recent sorting with favorites pinned, editable game categories and filtering, configurable backups and restore points, local log rotation, a per-user installer, and Windows packaging. NightLauncher remains a development release until trusted signing and hands-on Windows 10/11 compatibility and accessibility checks are completed. See [ROADMAP.md](ROADMAP.md) for remaining release work. GitHub Actions is the source of truth for build, smoke-test, vulnerability-check, and packaging results.
