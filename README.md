# Plutonium

A Minecraft **Java 1.21.11** client targeting **Java 21**. Plutonium provides its own launcher profile and a Fabric-compatible profile, so you can use Fabric mods alongside Plutonium.

## Play

Build and run `dist\Plutonium Client.exe`. It installs as a per-user Windows app with a Start menu shortcut and Windows Apps entry. On first launch it detects `.minecraft` and Java 21, verifies or repairs its bundled Plutonium files, and provisions a verified Java 21 runtime if needed. Sign-in opens the supported Microsoft flow in your browser; the launcher never asks for your password. Minecraft libraries and assets are obtained from Mojang when required and are not bundled in the executable.

Choose **STANDALONE** or **FABRIC**, then press **PLAY**. Fabric uses `%APPDATA%\.minecraft\mods`; the standalone profile reuses an existing legacy Quirk game directory when found. The launcher does not rewrite Minecraft launcher profiles or remove worlds, settings, or unrelated mods. Verified client and launcher updates use the latest GitHub release by default; update sources remain configurable in Settings.

Launcher 1.2 uses five main pages: Play, Mods, Account, Files and Settings, with a dark Play button, a player-skin avatar and the Plutonium app icon. The **Account** page lets you add Microsoft accounts, choose which one to use, or sign out of an account locally. Saved sign-ins are encrypted for the current Windows user. Each Play refreshes the selected Minecraft session before starting the game. The Cancel button stops cancellable sign-in, setup, and download operations.

The **Settings → Updates** section shows separate client and launcher versions. Manual checks only check; with automatic updates enabled, startup and Play checks also install verified client updates and restart for newer launchers. An unavailable feed shows its actual error and still permits playing the installed client. Launcher replacement keeps a `.previous` backup beside the executable. Repair restores missing standalone metadata without downgrading a newer client jar. Profile changes, repair, and updates are disabled while the launched game is running.

Client release manifests are HTTPS JSON with `version`, `standalone`, and `fabric` fields; each asset has an HTTPS `url` and a 64-character SHA-256 hex digest. Launcher manifests use `version` and an `executable` asset with the same fields. A client manifest example is:

```json
{
	"version": "1.0.1",
	"standalone": { "url": "<HTTPS asset URL>", "sha256": "<64-character SHA-256>" },
	"fabric": { "url": "<HTTPS asset URL>", "sha256": "<64-character SHA-256>" }
}
```

## Launcher library (1.2)

- **Mods**, directly below Play, searches Modrinth with Minecraft 1.21.11 and Fabric filters, category and sort controls, pagination, icons and descriptions. Full-width cards offer Install/Installed and View. View opens the complete description, gallery and compatible dependency plan natively inside the launcher. Existing mod jars (including renamed and disabled files) are identified through Modrinth SHA-512 lookups and marked Installed. Stable releases are the default; beta/alpha versions are opt-in. Installation automatically selects the Fabric profile.
- **Installed mods** supports update checks, reinstall, enable/disable and removal. Required dependencies are resolved recursively; incompatible versions and duplicate top-level Fabric mod IDs are rejected. Every jar is SHA-512 verified and inspected before the complete plan is committed. Local/unmanaged jars remain visible but are never overwritten. Removed managed jars are retained in `mods/.removed`; modified managed files replaced during repair are preserved in `mods/.replaced`. The index is `mods/.plutonium-mods.json`.
- **Settings** groups game preferences, appearance, saved servers, downloads, updates, history and health checks. Memory uses a custom monochrome slider and resolution/search filters use matching custom dropdowns.
- **Server favorites** saves names and addresses across restarts. Join starts Minecraft with its Quick Play multiplayer argument; Copy address and Remove are also available.
- **Downloads** displays progress, size, speed, estimated remaining time, status and cancellation for mod, client, launcher and Java transfers. These transfers can pause/resume during the current launcher session and retry transient network failures up to twice. Minecraft's own asset/library preparation appears as a cancellable aggregate task; its installer resumes missing files on the next Play. Failed mod installations are retried from the original Install button so their dependency transaction remains intact.
- **Health check** checks Java 21, writable game folders, disk space, client archives, managed-mod checksums and duplicate mod IDs. Client/Java repair is offered separately from mod reinstall. Minecraft libraries/assets are checked by the game installer when launching; this is not a guarantee that arbitrary third-party mods work together.
- **Update history** records update activity and keeps checksum-verified pre-update snapshots for client and launcher rollback. Backups begin with updates performed by this launcher version. Restore is limited to the original installation location and disables automatic updates; client rollback pins survive bootstrap repair. Launcher rollback uses the same exit-and-replace helper as normal updates.

All installed-file changes are blocked while the launcher is preparing or running Minecraft. Search remains available. Mod compatibility is based on Modrinth's version metadata and declared dependencies; a compatible listing does not guarantee compatibility with every other installed mod.

Run launcher checks with `dotnet test launcher.tests/PlutoniumLauncher.Tests.csproj`. To additionally exercise live Modrinth search, dependency resolution, downloads and library persistence in a temporary folder, set `PLUTONIUM_LIVE_MODRINTH=1` for that test run. Tests never use the user's account or production mod directory.

Elytra Glide now supplies smooth, configurable propulsion while already gliding; look in the direction you want to fly. It does not consume rockets or force your pitch. Render distance, simulation distance and the FPS limit are saved after changes and on normal shutdown using Minecraft’s native options file.

## Client menu controls

- **F8** opens or closes Plutonium; **ESC** closes it. The world continues running.
- The compact, translucent module columns use centered Montserrat TrueType text and scale with the window, including 1440p and 4K. They do not force your monitor resolution.
- Left-click a module row or switch to toggle it.
- Coordinates occupy a small three-line box at the upper left. These are the player's containing block coordinates, rounded down correctly for negative positions, even in freecam.

Opening the menu releases gameplay keys, captures mouse input, and closes any prior container through its normal close path. Settings are saved to `quirk/settings.json` inside the profile's game directory.

See [launcher release and error reporting](docs/launcher-release.md) for hosting updates and sending opt-in email reports.

## Modules

| Category | Modules |
| --- | --- |
| Combat | Aim Assist, Automace, Auto Totem, Double Anchor, No Hit Delay, Autoclicker |
| Movement | Freecam, Fly, Elytra Glide, Inventory Move, Sprint, Fast Place, Auto Clutch, Auto Firework |
| Render | Player ESP, Mob ESP, Storage ESP, Spawner ESP, Tracers, X-ray, Fullbright, SusChunk, Freelook, Name Tags, Pearl Trajectory |
| Misc | Auto Eat, Auto Inv Totem, Fake Pay, Netherite Finder, Fake Stats |
| HUD | Coordinates, Active Modules, Weather Notifier, Notifications |
| Entertainment | Discord Presence, Radio |

Block ESP uses the world render pass and the engine's exact view/projection matrices, including view bobbing and camera effects. Coordinates are subtracted from the camera in double precision before uploading vertices. Outlines and translucent face fills follow the actual block shape; they are no longer reconstructed as integer HUD lines. The Filled style includes both outlines and a subtle interior color. Global tracers work independently of ESP switches, and respect container filters. Mob ESP has separate hostile/passive switches.

Fullbright writes a white lightmap while enabled and leaves the normal update active for immediate restoration. X-ray hides ordinary terrain and fluids, preserves ore/valuable block models, disables occlusion, and rebuilds chunks on toggle. Netherite Finder incrementally scans loaded chunk sections for ancient debris. These features only use information received by the client; they cannot reveal unloaded chunks or defeat server-side hidden ore replacement.

SusChunk marks the surface perimeter of active chunks with red blocks. Activity means repeated movement of remote players observed in this session; this does not claim to detect old bases or historical/unobserved activity. Name Tags is on by default; disabling hides entity labels. Pearl Trajectory previews the held pearl's flight, block/entity collisions, and first impact; random launch spread and future entity movement can alter the actual throw.

Freecam uses the current movement bindings (normally WASD / Space / Shift), holds player input still, and disarms after disconnect/death. The player remains subject to world simulation. With Freelook enabled, hold Alt to enter third person and rotate the camera without changing player aim; releasing Alt restores the previous perspective. Sprint engages when forward movement and normal hunger/movement conditions allow it.

Aim Assist uses line-of-sight, range, cone, motion prediction, and a stronger adjustable turn limit. Each correction is proportional and bounded so it approaches the target without snapping. Automace can select a hotbar mace for a target in reach; its default falling-only option requires an actual fall. It uses ordinary attacks and does not manufacture height or bypass server reach checks. No Hit Delay removes the client miss timer; server damage cooldowns still apply. Autoclicker repeats attacks at the configured CPS, requires held left mouse by default, and leaves continuous block mining alone.

Auto Eat holds use until food is consumed, then releases it and restores the slot. Auto Clutch selects a suitable hotbar item, checks interaction reach, aims at the landing surface, uses buckets through their actual use action and blocks through placement, and retries failed actions. It prefers water outside the Nether, then web/powder snow or available landing blocks. Timing, terrain, and server rules still affect success. Auto Inv Totem operates only while the player's inventory is open; Auto Totem operates during gameplay. Neither moves inventory items while the cursor holds a stack.

Fake Pay intercepts `/pay` (including namespaced variants) before sending and displays a **local preview** notification/chat line. Fake Stats replaces only the local sidebar; edit the title and separate rows with `|`. Neither changes the server's balances or statistics. Weather notifications report changes with an adjustable cooldown. Module changes include the module description in a centered notification; eating, totem swaps, clutch attempts, and mace attacks also produce brief notifications.

## Entertainment

Discord Presence connects to the desktop Discord IPC service using application ID **1555301670643310712**. The ID can be edited in the menu. Only the client name and a generic menu/in-game activity are sent, not server addresses or account credentials. Enable activity sharing in Discord if needed. Connection status appears in the module's settings. Connection work stays off the render thread, retries after disconnects, and clears activity when disabled.

Radio offers YouTube/browser playback and direct HTTP(S) Ogg/Vorbis streaming in Minecraft. Use Enabled to start/stop the native stream, and toggle it off/on to reconnect. In-game volume applies to native streams; YouTube uses browser controls. No songs are bundled.


Discord integration follows the [official IPC protocol](https://docs.discord.com/developers/topics/rpc).

## Build

Install a JDK 21 or newer and .NET 10 SDK, then run **BUILD.cmd**, or:

```powershell
.\gradlew.bat build
.\gradlew.bat installClient
```

The build publishes the self-contained Windows launcher to `dist\Plutonium Client.exe`; end users do not need Gradle, .NET, or a Java development kit installed to run the launcher. The launcher installs a Java 21 runtime when none is available.

All Plutonium classes compile with `--release 21`. The wrapper pins Gradle 9.5.1. Build preparation retrieves Minecraft 1.21.11, its official mappings and dependencies from Mojang and checks their SHA-1 hashes; matching local launcher downloads are reused. Build tooling maps the client for compilation, adds verified integration hooks, then packages and remaps the standalone client. Tiny Remapper, Mapping IO, and ASM run only during the build.

Outputs:

- `build/client/plutonium-1.21.11.jar`: complete locally assembled client, used by the installer.
- `fabric/build/libs/plutonium-client-fabric-1.1.1.jar`: Plutonium as a Fabric client mod, installed automatically in `%APPDATA%\.minecraft\mods`.
- `build/libs/plutonium-client-1.0.0.jar`: Plutonium's compiled implementation only; not a drop-in mod.
- `build/reports/tests/test/index.html`: automated test results.

Minecraft game binaries are downloaded and assembled locally, and are not part of this source project. The Fabric build uses Fabric Loom and official Minecraft mappings. UI typography uses Montserrat under the SIL Open Font License; see `licenses/Montserrat-OFL.txt`.

## Development verification

The settings tests cover immediate callbacks, bounded and quantized slider values, all-option persistence, invalid configuration recovery, and safe freecam startup. Geometry tests cover viewport clipping and invalid coordinates; rendering tests cover storage shape bounds and antialiased corner coverage. The Fabric profile is built through Loom and can be launched with `.\gradlew.bat :fabric:runClient` during development.

An opt-in game smoke test creates a separate flat world in `run/smoke`, requests a 2560x1440 client window (the desktop may cap the actual render size), exercises the menu and visual/aim features, verifies continuing world ticks, captures screenshots, and exits. It uses a local developer session and no Microsoft credentials:

```powershell
.\scripts\smoke.ps1 -Java21 'C:\path\to\java-21\bin\java.exe'
```

Its result is written to `run/smoke/smoke-result.txt`. Screenshots are in `run/smoke/screenshots`. Never point this harness at a normal game directory.

Existing internal module IDs and the `quirk/settings.json` location are retained so the Plutonium rename preserves saved settings. The installer updates the existing profile names and keeps their game directory.

## Release 1.1.1

The launcher waits for a Minecraft window before showing Playing, monitors exits, and offers Stop Minecraft for stalled starts. The account head uses sharp nearest-pixel scaling and the title bar labels are aligned.

Storage ESP excludes Ender Chests and has container filters plus optional per-block colors. Right-click any module for settings. Fly supports singleplayer and server-granted flight; Elytra Glide stabilizes pitch during existing elytra flight. Inventory Move works in the player inventory and suspends movement while typing. Multiplayer Auto Clutch only uses the landing surface under the real crosshair, without forced rotation packets; server rules can still reject automation.

Radio has two explicit modes: YouTube opens the supplied playlist in the browser, where playback is controlled; In-game stream accepts direct Ogg/Vorbis URLs and has an in-game volume slider. A YouTube page is not a direct audio stream.
