# Plutonium verification — 2 October 2026

Minecraft 1.21.11, actual Java 21 runtime, Windows. Standalone client and Fabric 0.19.5 were tested separately in isolated local worlds at a 2560×1440 framebuffer resolution.

- Build: passed for both artifacts.
- Automated tests: 29 passed, 0 failures/errors.
- Standalone integration run: PASS.
- Fabric integration run: PASS.

The in-game assertions cover F8/ESC, continuing world ticks while the menu is open, mouse toggles, right-click settings, sliders, RGB controls, text replacement, persistence, freecam motion and cleanup, smooth bounded aim adjustment, X-ray filtering, block shape alignment, fullbright hooks, eating and slot restoration, inventory totems, a water clutch without health loss, negative coordinates, and a mace attack that damages a server-side target. Screenshots also cover nighttime lighting, the new centered panel layout, notifications, and pearl trajectories.

The clutch fixture was isolated from hostile mobs before measuring its minimum health; otherwise ambient damage could be mistaken for fall damage. An outdated slider-click coordinate was corrected. The standalone radio stream hook was added to match the Fabric integration.

## Assessment

A functional prototype with working core features in the tested local conditions. This is not evidence of reliability on every multiplayer server, under high latency, with third-party rendering mods, or during long sessions. No sustained performance benchmark was conducted. Discord presence and audible live radio playback were not verified end to end. Native Radio requires a direct Ogg/Vorbis stream; the supplied YouTube playlist is not a supported stream URL.

Existing internal IDs, the legacy settings path, and game directory are retained to preserve compatibility. Visible branding, launcher profile names, Discord activity details, and build artifact names now use Plutonium. Run INSTALL.cmd to install the rebuilt profiles.

## Evidence

- `build/reports/tests/test/index.html`
- `run/smoke/smoke-result.txt` and `run/smoke/stdout.log`
- `run/fabric-smoke/smoke-result.txt` and `run/fabric-smoke/stdout.log`
- `run/smoke/screenshots/` and `run/fabric-smoke/screenshots/`

## Artifact SHA-256

- `build/client/plutonium-1.21.11.jar`: `c21ead6f9f8630986ded9fb1f2e581862f43c8d94cf71be20e88e6f33ae1cfad`
- `fabric/build/libs/plutonium-client-fabric-1.0.0.jar`: `90edfc6f10d9c1e448912d0ffdf43462ded495dd3725aec32c31d197fd939742`
