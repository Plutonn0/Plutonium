# Plutonium launcher 1.1.1

The Windows x64 executable contains the .NET runtime and both Plutonium client payloads. On first run it installs itself in `%LOCALAPPDATA%\Programs\Plutonium`, adds a Start menu shortcut and registers in Windows Settings → Apps. No administrator access is required. Java 21 is downloaded and verified if no suitable runtime exists. Minecraft libraries and assets are installed when Play is pressed. A Microsoft account that owns Minecraft Java and an internet connection are required for normal play.

Uninstall removes the application and Start menu shortcut, while preserving launcher account data and Minecraft worlds. Saved sign-ins are encrypted for the current Windows user. Downloaded older launchers do not overwrite a newer installed version.

## Release hosting

Automatic updates need real HTTPS release manifests and downloadable binaries. They are not live merely because source code or the website is updated. The default feeds are the latest GitHub release assets in Plutonn0/Plutonium.

1. Build with Gradle. `dist/Plutonium Client.exe` is the downloadable installer/launcher.
2. Choose the HTTPS directory that will actually serve releases, then run `scripts/package-release.ps1 -PublicBaseUrl <directory> -ClientVersion <version> -LauncherVersion <version>`.
3. Upload the generated binaries before publishing `client.json` and `launcher.json`. Ensure the host supports their file sizes and binary downloads. The Vercel site can serve manifests even if binaries need a separate release host; edit asset URLs and retain their matching SHA-256 hashes.
4. The launcher defaults to https://github.com/Plutonn0/Plutonium/releases/latest/download/client.json and launcher.json. Custom sources can be entered in Files → Memory, resolution & update sources.
5. With automatic updates enabled, the launcher checks on startup and before Play, verifies SHA-256 before replacing a client, and downloads/restarts itself when a newer launcher is available. Increment version numbers for each release. An older downloaded installer opens the newer installed launcher.

Publish all assets to a versioned GitHub release and mark it latest. The legacy Releases download asset is also refreshed so the existing Vercel website button continues to work through the GitHub repository redirect. Website source is only needed for changes to the website itself.

## Errors

Errors display their actual message and a report preview. Common credentials, bearer tokens, email addresses and Windows user paths are redacted. Send error opens an email draft to `justquirk.business@gmail.com`; the user reviews and sends it. Long reports can be copied in full. There is no automatic telemetry upload or email delivery backend. A browser/server mail integration would require the website source and server-side mail credentials, never credentials embedded in the launcher.

## Verification

Run `dotnet test launcher.tests/PlutoniumLauncher.Tests.csproj --configuration Release` for account persistence, update integrity, repair, helper replacement and report redaction tests. The isolated `--smoke-test` and `--smoke-launch=standalone|fabric` modes skip app installation; set `PLUTONIUM_LAUNCHER_HOME` and `PLUTONIUM_MINECRAFT_DIR` to test directories to avoid touching a real account or world.
