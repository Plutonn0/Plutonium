# Plutonium

A Minecraft **Java 1.21.11** client targeting **Java 21**. Plutonium provides its own launcher profile and a Fabric-compatible profile, so you can use Fabric mods alongside Plutonium.

## Play

Build and run `dist\Plutonium Client.exe`. On first launch it detects `.minecraft` and Java 21, verifies or repairs its bundled Plutonium files, and provisions a verified Java 21 runtime if needed. Sign-in opens the supported Microsoft flow in your browser; the launcher never asks for your password. Minecraft libraries and assets are obtained from Mojang when required and are not bundled in the executable.

Choose **STANDALONE** or **FABRIC**, then press **PLAY**. Fabric uses `%APPDATA%\.minecraft\mods`; the standalone profile reuses an existing legacy Quirk game directory when found. The launcher does not rewrite Minecraft launcher profiles or remove worlds, settings, or unrelated mods. Release manifests can be configured in Settings; no production update endpoint is built in.

Launcher 1.0.1 adds an **Accounts** panel: add Microsoft accounts, choose which one to use, or sign out of an account locally. Saved sign-ins are encrypted for the current Windows user. Each Play refreshes the selected Minecraft session before starting the game. The Cancel button stops cancellable sign-in, setup, and download operations.

The **Updates** panel shows separate client and launcher versions and installation buttons. Checking for updates does not install them. An unavailable feed no longer prevents playing the installed client. Launcher updates require **Update and restart launcher**, verify the download, replace the executable after it closes, and keep a `.previous` backup beside it. Repair restores missing standalone metadata without downgrading a newer client jar. Profile changes, repair, and updates are disabled while the launched game is running.

Client release manifests are HTTPS JSON with `version`, `standalone`, and `fabric` fields; each asset has an HTTPS `url` and a 64-character SHA-256 hex digest. Launcher manifests use `version` and an `executable` asset with the same fields. A client manifest example is:

```json
{
	"version": "1.0.1",
	"standalone": { "url": "<HTTPS asset URL>", "sha256": "<64-character SHA-256>" },
	"fabric": { "url": "<HTTPS asset URL>", "sha256": "<64-character SHA-256>" }
}
```

## Menu controls

- **F8** opens or closes Plutonium; **ESC** closes it. The world continues running.
- The compact, translucent module columns use centered Montserrat TrueType text and scale with the window, including 1440p and 4K. They do not force your monitor resolution.
- Left-click a module row or switch to toggle it.
- Coordinates occupy a small three-line box at the upper left. These are the player's containing block coordinates, rounded down correctly for negative positions, even in freecam.

Opening the menu releases gameplay keys, captures mouse input, and closes any prior container through its normal close path. Settings are saved to `quirk/settings.json` inside the profile's game directory.

## Modules

| Category | Modules |
| --- | --- |
| Combat | Aim Assist, Automace, Auto Totem, Double Anchor, No Hit Delay, Autoclicker |
| Movement | Freecam, Sprint, Fast Place, Auto Clutch, Auto Firework |
| Render | Player ESP, Mob ESP, Storage ESP, Spawner ESP, Tracers, X-ray, Fullbright, SusChunk, Freelook, Name Tags, Pearl Trajectory |
| Misc | Auto Eat, Auto Inv Totem, Fake Pay, Netherite Finder, Fake Stats |
| HUD | Coordinates, Active Modules, Weather Notifier, Notifications |
| Entertainment | Discord Presence, Radio |

Block ESP uses the world render pass and the engine's exact view/projection matrices, including view bobbing and camera effects. Coordinates are subtracted from the camera in double precision before uploading vertices. Outlines and translucent face fills follow the actual block shape; they are no longer reconstructed as integer HUD lines. The Filled style includes both outlines and a subtle interior color. Global tracers work independently of ESP switches, and respect container filters. Mob ESP has separate hostile/passive switches.

Fullbright writes a white lightmap while enabled and leaves the normal update active for immediate restoration. X-ray hides ordinary terrain and fluids, preserves ore/valuable block models, disables occlusion, and rebuilds chunks on toggle. Netherite Finder incrementally scans loaded chunk sections for ancient debris. These features only use information received by the client; they cannot reveal unloaded chunks or defeat server-side hidden ore replacement.

SusChunk marks the surface perimeter of active chunks with red blocks. Activity means repeated movement of remote players observed in this session; this does not claim to detect old bases or historical/unobserved activity. Name Tags is on by default; disabling hides entity labels. Pearl Trajectory previews the held pearl's flight, block/entity collisions, and first impact; random launch spread and future entity movement can alter the actual throw.

Freecam uses the current movement bindings (normally WASD / Space / Shift), holds player input still, and disarms after disconnect/death. The player remains subject to world simulation. Freelook changes the camera direction without changing player aim. Sprint engages when forward movement and normal hunger/movement conditions allow it.

Aim Assist uses line-of-sight, range, cone, motion prediction, and a stronger adjustable turn limit. Each correction is proportional and bounded so it approaches the target without snapping. Automace can select a hotbar mace for a target in reach; its default falling-only option requires an actual fall. It uses ordinary attacks and does not manufacture height or bypass server reach checks. No Hit Delay removes the client miss timer; server damage cooldowns still apply. Autoclicker repeats attacks at the configured CPS, requires held left mouse by default, and leaves continuous block mining alone.

Auto Eat holds use until food is consumed, then releases it and restores the slot. Auto Clutch selects a suitable hotbar item, checks interaction reach, aims at the landing surface, uses buckets through their actual use action and blocks through placement, and retries failed actions. It prefers water outside the Nether, then web/powder snow or available landing blocks. Timing, terrain, and server rules still affect success. Auto Inv Totem operates only while the player's inventory is open; Auto Totem operates during gameplay. Neither moves inventory items while the cursor holds a stack.

Fake Pay intercepts `/pay` (including namespaced variants) before sending and displays a **local preview** notification/chat line. Fake Stats replaces only the local sidebar; edit the title and separate rows with `|`. Neither changes the server's balances or statistics. Weather notifications report changes with an adjustable cooldown. Module changes include the module description in a centered notification; eating, totem swaps, clutch attempts, and mace attacks also produce brief notifications.

## Entertainment

Discord Presence connects to the desktop Discord IPC service using application ID **1555301670643310712**. The ID can be edited in the menu. Only the client name and a generic menu/in-game activity are sent, not server addresses or account credentials. Enable activity sharing in Discord if needed. Connection status appears in the module's settings. Connection work stays off the render thread, retries after disconnects, and clears activity when disabled.

Radio plays a direct HTTP(S) Ogg/Vorbis stream through Minecraft's sound system. Play, Stop, Reconnect and live volume are available in its settings. YouTube playlist pages are not direct audio streams and cannot be played by this native player. The supplied playlist has not been converted into an audio catalog; an audio stream URL is still needed. No songs are bundled.


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
- `fabric/build/libs/plutonium-client-fabric-1.0.0.jar`: Plutonium as a Fabric client mod, installed automatically in `%APPDATA%\.minecraft\mods`.
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
