# Windows build validation

This branch exists to run the repository's Windows build and smoke-test workflow against the initial NightLauncher implementation before merging the validation record.

The workflow must compile the WPF application, run the library and launch-guard smoke tests, and publish the self-contained win-x64 executable and portable ZIP. A green workflow run is required before treating the binary as verified.
