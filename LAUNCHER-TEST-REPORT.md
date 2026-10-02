# Launcher 1.0.1 verification

Built the self-contained Windows executable at `dist/Plutonium Client.exe` using .NET 10.

## Automated checks

`dotnet test launcher.tests/PlutoniumLauncher.Tests.csproj --configuration Release --no-restore`: 16 passed, no failures.

Coverage includes fresh installation, preservation of legacy settings and unrelated files, corrupted bundled-file repair, missing Java rejection, valid and invalid release manifests, checksum rejection, cancellation without replacement, encrypted account-vault round trips, unknown account rejection, newer-client preservation while repairing metadata, and accurate version tracking when restoring missing files.

The launcher replacement helper was executed against temporary fixture files in a path containing Unicode, an apostrophe, a percent sign, and an ampersand. The replacement succeeded, preserved the previous executable, and left no partial replacement. No real launcher or account was replaced by this test.

## UI and runtime observations

The published launcher initialized against an isolated Minecraft directory under `run/launcher-ui`, detected Java 21, and installed its bundled files. The Accounts panel was inspected with the computer-use skill. A successful account add and selection were observed while the user operated the test launcher; authentication prompts were not automated. No account was signed out during UI verification.

The release build compiled successfully after the account and update panels were added. End-to-end downloading from a production release feed remains unverified because no production feed is configured. Update checks and local file replacement are covered separately by automated tests. This report does not claim a fresh full Minecraft module regression run.

The UI test process uses a separate data directory. Close it and reopen `dist/Plutonium Client.exe` normally to use the normal Minecraft installation and launcher account store.
