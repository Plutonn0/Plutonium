# Plutonium 1.1.1

Minecraft Java 1.21.11 · Java 21 · Windows x64 launcher

- Modern black-and-white launcher pages, aligned title-bar labels, a sharp 1080×1080 pixel-scaled skin head, and the Plutonium icon.
- Fixed premature/stale Playing status: the launcher waits for a game window, polls the process, detects exits, reports startup errors and offers Stop Minecraft.
- Installs into Windows Apps and the Start menu. Encrypted account sign-ins survive restarts and upgrades.
- Automatic client and launcher updates use verified GitHub release assets. Manual error reports open a previewable email draft.
- Fixed the standalone custom-font namespace; crisp menu and HUD text, monochrome coordinates/notifications, and restored right-click settings with toggles, sliders, colors and text fields.
- Crosshair-anchored tracers and third-person Freelook while holding Alt, restoring the previous perspective on release.
- Storage ESP no longer highlights Ender Chests. Added hopper, furnace, dispenser/dropper filters and optional separate colors for each container type.
- Added Fly, Elytra Glide and Inventory Move. Fly works in singleplayer or with server-granted flight; Elytra Glide stabilizes pitch during existing elytra flight; inventory movement suspends while typing.
- Multiplayer Auto Clutch uses the actual aimed landing surface and ordinary item interactions instead of forced down/restore camera packets. Server anticheat compatibility is not guaranteed.
- Radio has explicit YouTube/browser and in-game Ogg/Vorbis stream modes. Browser playback uses the browser's controls; a YouTube playlist URL is not decoded as an audio stream.
- Included required build tooling in source control and removed generated binaries from the source tree.

Validation: 32 Java tests, 24 launcher tests, and standalone/Fabric in-game smoke tests. Tests include custom-font metrics, live module settings, Freelook perspective restoration, client repair, account persistence and update integrity. Multiplayer server anticheat behavior and the availability of the supplied YouTube playlist have not been verified.

Download **Plutonium.Client.exe** and run it to install/update the launcher. Close an older running launcher first. The first game launch may download Java and Minecraft dependencies. Use a Microsoft account that owns Minecraft Java for normal play.
