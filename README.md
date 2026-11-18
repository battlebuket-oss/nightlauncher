# NightLauncher

**A free, native Windows game-library and launch manager.** Add games you have installed, keep their launch paths in one local library, and start them with a click.

> **Important:** NightLauncher is not a crack, loader, emulator, launcher replacement, or DRM bypass. It does not grant a game licence, authenticate accounts, defeat ownership checks, disable DRM, or make an unlicensed copy playable. Games that require Steam, Rockstar Games Launcher, or another platform must still pass that platform's normal ownership and authentication checks.

## What is included

- Native Windows desktop UI (WPF, .NET 10).
- Add and edit games by choosing an executable yourself.
- Optional launch arguments and working directory.
- Search and browse a local game library.
- Launch selected executables using the normal Windows process API.
- Import and export the library as JSON.
- Local-only storage under the user's Local AppData folder.
- No telemetry, cloud account, credential collection, game-file patching, DLL injection, process-memory access, or automatic network calls.
- Self-contained win-x64 executable produced by GitHub Actions.

## Download

After the main-branch Windows build and smoke tests pass, the workflow publishes an unsigned CI prerelease under **Releases** and uploads a downloadable executable and portable ZIP. The Actions run also retains the build artifact for 30 days.

For a stable, versioned GitHub Release, push a tag such as v1.0.0; the workflow will attach the executable and ZIP to that release.

The executable is unsigned. Windows SmartScreen may show an unknown-publisher warning. Only download builds from this repository, and review the source before running it.

## Run from source

Requirements: Windows 10/11, .NET 10 SDK, and the Windows Desktop workload included with the SDK.

    dotnet build src/NightLauncher/NightLauncher.csproj -c Release
    dotnet run --project src/NightLauncher/NightLauncher.csproj

## Build a self-contained executable

    dotnet publish src/NightLauncher/NightLauncher.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

The output is NightLauncher.exe in the publish folder. It does not require a separately installed .NET runtime.

## Run smoke tests

    dotnet run --project tests/NightLauncher.SmokeTests/NightLauncher.SmokeTests.csproj -c Release

The smoke tests exercise library save/load, JSON export/import, and the launcher's missing-file guard. The Windows workflow runs them before publishing.

## Add a game

1. Start NightLauncher and select **Add game**.
2. Enter a display name and browse to the game's executable.
3. Optionally add launch arguments. Leave them blank unless the game's official documentation requires them.
4. Save, select the game in the library, and press **Launch**.

NightLauncher does not discover or modify Steam/Rockstar credentials. If a game requires a store client or publisher launcher, use its official supported launch path and remain signed in to an account that owns the game.

## Local data and privacy

The library is stored at:

    %LocalAppData%\\NightLauncher\\library.json

Settings and logs are also local. Export a JSON backup before reinstalling Windows or deleting the application data folder. Imported JSON is validated before it is added to the library.

## Security model

NightLauncher starts only the executable path explicitly selected by the user. It does not elevate privileges automatically. It does not accept remote commands, load plugins, run scripts, download DLLs, or communicate with game processes. See SECURITY.md for reporting guidance.

## Project status

This is the initial production-oriented release foundation. GitHub Actions is the source of truth for Windows build and smoke-test results. A successful build workflow is required before a release artifact should be treated as verified.
