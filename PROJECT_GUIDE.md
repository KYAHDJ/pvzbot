# PvZ all-in-one controller — complete project guide

Last checked: 2026-09-23. Canonical project root: `G:\pvz`.

This document describes the **current implementation**, rather than a proposed feature list. The game is PopCap's Plants vs. Zombies GOTY; the controller is our separate C# application. The controller does not contain a copy of the game's source or replace the game executable.

## 1. What is in `G:\pvz`

| Path | Purpose | Keep? |
| --- | --- | --- |
| `Game\` | The installed PvZ GOTY distribution, including the launcher, game executable, data, support files, and redistributables. | Yes. Do not remove files selectively. |
| `Game\Plants Vs Zombies GOTY\Plants vs. Zombies\PlantsVsZombies.exe` | The verified game executable that the controller attaches to. | Yes. |
| `Controller\PvZController-Smart\` | **The only installed controller release.** Contains `PvZController.exe`, its self-contained .NET runtime files, language resources, and `Watch-PvZ.ps1`. | Yes. Launch this release, not a build output in `Source`. |
| `Source\PvZController\` | Editable WPF controller source. | Yes; this is the base for future changes. |
| `Source\PvZController.LiveCheck\` | Read-only and targeted test utility source. Some diagnostic switches can change the game; inspect them before use. | Yes. |
| `Source\PvZController.slnx`, `Source\build.ps1` | Solution and release build command. | Yes. |
| `Source\README.md` | Short operational notes and recent changes. | Yes. |
| `SaveBackup\2026-09-23\` | Snapshot of the four PvZ userdata files, copied here on 2026-09-23. **Not** the active save location. | Yes. |
| `PROJECT_GUIDE.md` | This detailed guide. | Yes. |

`G:\Plants.Vs.Zombies.GOTY` is a **junction** pointing to `G:\pvz\Game`, not a second game installation. It preserves old shortcuts. Opening the old path still opens the files now stored in `G:\pvz\Game`.

The projectless Codex working folder under Documents was emptied after the migration. The current source and installed controller are in `G:\pvz`; future code work should start there.

### Files that must stay outside this folder

The game's **active** user profile and saves are maintained at `C:\ProgramData\PopCap Games\PlantsVsZombies\userdata`. This is PvZ's own path. A snapshot is under `SaveBackup`, but copying it there does not redirect live saves. Do not edit or replace live `.dat` files while the game is running. Windows stores the startup entry in the current user's registry. StreamToEarn and TikTok Live Studio are separate third-party applications and have not been moved into this project folder. Windows temporary WAV files created for bot speech are deleted after each spoken line.

## 2. Normal use and automatic startup

1. Sign in to Windows. The current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry named `PvZControllerWatcher` starts a hidden Windows PowerShell process running `G:\pvz\Controller\PvZController-Smart\Watch-PvZ.ps1`.
2. The watcher checks for a `PlantsVsZombies` process every **2 seconds**. A named mutex prevents duplicate watchers. When the game exists and `PvZController` does not, it opens `G:\pvz\Controller\PvZController-Smart\PvZController.exe` visibly.
3. The controller polls for the game about every **500 ms**. It verifies the exact executable build before connecting. It enables automatic pickup of sun/coins and background running, then starts the combat bot. No Start button is needed.
4. The bot waits for plant selection or battle. In plant selection it fills/uses the tray and presses **Let's Rock** automatically. In battle it plants, shovels, fires a Cob Cannon when useful, and handles rewards and losses.
5. If the controller is closed while PvZ remains open, the watcher reopens it. If PvZ closes, the controller stays open and waits for the next game process.

The old launcher path also works because of the junction. The verified game EXE's SHA-256 is `1FF5A2DCF009B453B8866783A68E8D04BD0D066ABDA3F8196D75A677366B2DFF`. The controller refuses an unrecognized executable rather than guessing memory offsets.

The watcher starts **at Windows sign-in**, not as a Windows service before sign-in. If the registry startup entry is removed or blocked, opening the game alone will not start the controller. The controller release can be opened manually. If you want it to stay closed during a running game, stop the watcher or remove its startup entry first; otherwise it will reopen the controller.

## 3. Architecture

```text
PvZ GOTY process
   ↑ game state reads / direct game calls / small guarded patches
PvZController.exe (WPF, .NET 10, x64)
   ├─ PvzProcess: verified connection and game operations
   ├─ AutoPlantStrategy + VirtualArsenal: combat decisions and cooldowns
   ├─ UninterruptedSpawner: continuous Day zombie spawns with difficulty curve
   ├─ StreamToEarnBridge: local event input on 127.0.0.1:8082
   ├─ VoicePersona + PersonaLines: local voice and line selection
   └─ SubtitleWindow: click-through desktop subtitle above PvZ
Watch-PvZ.ps1: sign-in watcher that launches the controller
```

The controller is a self-contained `net10.0-windows` WPF application built for x64. The published release includes its .NET runtime; the ordinary user does not need to install the .NET SDK to run the release. `Source\build.ps1` uses the .NET SDK for development. The project file declares no third-party NuGet packages. The game and StreamToEarn remain separate products.

### Important source files

| File under `Source\PvZController\` | Role |
| --- | --- |
| `App.xaml`, `App.xaml.cs` | WPF application startup. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Controller UI, automatic connection, bot loop, StreamToEarn queue, voice triggers, action log. |
| `PvzProcess.cs` | PvZ connection, state reads, seed choice, planting, shoveling, zombie spawning, round handling, reward/retry actions, uninterrupted suppression, patch restoration. |
| `Pvz1073Profile.cs` | Fixed offsets, internal call addresses, and expected bytes for the verified 1.2.0.1073 executable (including board wave HUD suppression offsets). |
| `UninterruptedSpawner.cs` | Time-based spawner that keeps live zombies bounded (≤32) and rises from ~2.6 s to ~1.1 s intervals with tiered zombie pools. |
| `NativeMethods.cs`, `X86Builder.cs` | Windows process/window APIs and the small x86 call stubs used to invoke game routines. |
| `AutoPlantStrategy.cs` | Per-lane threat scoring and plant choice rules. |
| `VirtualArsenal.cs` | Day-lawn virtual plant availability and per-type cooldowns. |
| `StreamToEarnBridge.cs` | Loopback HTTP listener for StreamToEarn game effects. |
| `VoicePersona.cs`, `PersonaLines.cs` | Local Windows speech, amplification, queue limits, personality lines (524 lines), recent-line avoidance. |
| `SubtitleWindow.cs` | Top-of-game, click-through caption overlay above PvZ (Display Capture includes it; Game Capture omits it). |
| `GameItem.cs` | UI item model. |

`Source\PvZController.LiveCheck\Program.cs` contains diagnostics and strategy checks. Its `--rounds` and `--status` switches read live state. Other switches may perform direct game actions; review their code before invoking them on a valuable run.

## 4. How the controller interacts with PvZ

The controller finds the running `PlantsVsZombies.exe`, checks its SHA-256 and PE timestamp, opens it with Windows process-memory access, and locates the matching game window. `PvzProcess` reads the game UI state, mode, board, sun, plants, zombies, and seed packets. It bulk-reads plant/zombie arrays during scans to avoid many individual Windows calls. The exact memory profile is in `Pvz1073Profile.cs`.

There are three interaction mechanisms:

1. **State reads and bounded writes.** The controller reads board structures and writes sun, certain mode/scene data, virtual-arsenal placements, and controlled entity fields. It limits array scans and validates cells/types.
2. **Calls to the game's own functions.** Small x86 stubs invoke verified functions for planting, zombie spawning, seed selection, and Let's Rock. The virtual arsenal uses the game's AddPlant routine and tracks its own cooldowns. It does not require 36 visible seed cards.
3. **Window messages sent only to PvZ.** Chooser fallback clicks, resume/retry actions, and key presses use messages addressed to the game window. The controller does **not** move or click the user's real mouse cursor.

Automatic pickup and background play use byte patches only at addresses whose original bytes match the verified build. The controller records patches it owns and restores them on normal disconnect/exit where the game is still available. The optional Free planting checkbox controls another game flag. An Emergency Stop button is present, although the always-on auto-attach loop can reconnect afterward; to keep the controller off while PvZ is open, stop its watcher and close the controller. Do not treat the red button as a persistent disable switch.

The controller hides the seed bank and shovel during Day battle for a cleaner view; it restores the HUD in plant selection. The bot can still shovel through a direct game action. Changing the game build or replacing the EXE requires a newly researched profile; the present addresses are not portable.

## 5. Uninterrupted Day battle (replaces classic Survival intermission)

The bot is configured for **Uninterrupted Survival: Day Endless** via the `Uninterrupted Day: one endless battle` checkbox (checked by default). At the chooser it sets GameMode 11, five dry-land rows, no water, and initializes the endless round counter only if zero/negative. It preserves a positive count if you already had progress.

During **battle (GameUi 3)** the controller no longer does the old “advance the ‘More zombies approaching!’ message and click Let's Rock” dance. Instead, every ~200 ms it:

* writes board fields to keep the level from ever signalling `LevelComplete`, `BoardFadeOut`, or `NextSurvivalStage` (offsets `BoardLevelComplete 0x5614`, `BoardBoardFadeOutCounter 0x5618`, `BoardNextSurvivalStageCounter 0x561C`);
* freezes the engine's own wave timer (`BoardZombieCountDown/Start 0x55B4/0x55B8` → 7500, `BoardHugeWaveCountDown 0x55BC` → 9999, `BoardCurrentWave 0x5594` → 0) so the native wave/huge-wave advice and Crazy Dave dialogue between flags never fire;
* hides the bottom wave/flag HUD (`BoardProgressMeterWidth 0x5628` and `BoardFlagRaiseCounter 0x562C` → 0, plus SeedBank Y −100 and shovel hidden);
* drives `UninterruptedSpawner` which spawns one zombie at column 8 on a random row when live zombies < 32. Intervals start ~2.6 s and tighten to ~1.1 s over 20 minutes with ±140 ms jitter and +500–1200 ms back-pressure when crowded. Tiers progress: early normal/cone/newspaper/pole → bucket/football → jack/balloon/digger/bungee/ladder → catapult/gargantuar/gigagargantuar. This keeps difficulty rising without unbounded entity growth; StreamToEarn `PutZombie` still respects the 45-live cap and the 256-event queue.

Result: **one continuous Day lawn, five land rows, no water, no Crazy Dave, no “More zombies approaching!” overlay, no between-flag plant-selection screen, no wave/flag progress display, and no cutscenes.** The lawn, projectiles, collisions and house-loss remain vanilla; the controller only suppresses the survival stage transition and replaces the native wave spawner. At `GameUi == 4` it still waits ~8 s then retries every ~2 s.

If the checkbox is unchecked, the controller falls back to classic Survival Endless: it advances the built-in survival message via `AdvanceSurvivalMessage()` and preserves flag progress as before. The project distinguishes the two modes; the new mode is not a fast intermission skip.

## 6. Bot gameplay decisions

`AutoPlantStrategy` is a deterministic rule-based controller, not a trained AI or an online model. It reads each row's zombies and plants and chooses one action at a time. The general order is:

1. Stop a near-house breach before routine construction.
2. Clear active Balloon Zombies with Blover and provide Cactus coverage in every row.
3. Slow large/high-health threats with Snow Pea when there is room behind them.
4. Spend Cherry Bomb, Jalapeño, Squash, Doom-shroom, or Ice-shroom when a lane is collapsing, a serious breach forms, or a horde pushes close enough. Per-row delays and adjacent Cherry splash tracking reduce wasteful stacking.
5. Put Wall-nut/Tall-nut barriers near the front, not at the back; reserve a Cob Cannon when a safe two-column space exists.
6. Fill rows with fewer sustained attackers first. Zombie crowding gives a thin row extra priority. Use Winter Melon, Melon-pult, Repeater, Gatling Pea, and other attackers when available.
7. Add forward spikes and Pumpkin protection, then clean up misplaced rear walls or duplicate Cactus plants only when it is safe. Use the shovel to make room for stronger plants.

The controller keeps sun near **5,000,000** and auto-collects pickups. Sun producers such as Sunflower are not part of the Day virtual arsenal's preferred fighting force. The virtual arsenal contains 36 useful Day dry-land plant types; water-only and roof-only plants are excluded from that Day list. Each virtual type has its own cooldown: 7.5 seconds normally, 30 seconds for slower types, and 50 seconds for very slow/explosive/upgraded types. Day mushrooms require Coffee Bean, which also has a cooldown. In non-Day scenes the strategy reads real seed packets and their actual readiness instead.

The bot's main timer is about **200 ms**, but actions are spaced by roughly 300–400 ms and expensive board scans are skipped while waiting. Cob Cannon firing has a separate roughly 35-second gate. Action reasons appear in the controller log. These rules improve coverage but cannot guarantee a win against every combination of high zombie counts and viewer effects.

## 7. StreamToEarn connection

The controller starts a loopback HTTP listener at `127.0.0.1:8082`. The `/version` endpoint returns version `1`; StreamToEarn sends `POST /trigger_effect` with JSON containing an `effect_id` and optional fields. An example payload is `{"effect_id":"PutZombie","type":"Football Zombie"}`. The bridge is local to this PC and permits the StreamToEarn app origin in its CORS response.

| Implemented effect ID | Controller action |
| --- | --- |
| `EasyStart` | Ensures the bot timer is running. |
| `SetSun` | Sets sun to the supplied `value`, defaulting to 5,000,000. |
| `FreePlantsOn`, `FreePlantsOff` | Toggle the game's Free planting flag. |
| `KillAllZombies` | Sets health of live zombies to zero. |
| `ClearAllPlants` | Marks live plants removed. |
| `PutZombie` | Spawns the requested/random zombie on the right. Refuses a new viewer spawn once 45 living zombies are already on screen. |
| `PutPlant` | Places the requested/random plant in a free cell. |

Unknown effect IDs are logged as unimplemented. Incoming effects are queued up to **256**; the UI drains roughly one every **80 ms**, and overload drops are logged. This bounds controller-side work during bursts. It cannot cap PvZ's natural wave size, prevent all engine crashes, or guarantee that every event is applied when overloaded.

**StreamToEarn is not TikTok chat access.** This controller receives game-effect triggers, not the text of Live comments. It cannot read or answer comments today. StreamToEarn must be running and connected to TikTok separately; launching PvZ does not automatically connect TikTok. Do not enter TikTok credentials into this controller: it has no credential setting or account login flow.

## 8. Voice and subtitles

The voice is enabled by default and can be muted with the **Bot voice (free local speech)** checkbox. It uses installed Windows SAPI speech voices; no API key, subscription, or cloud model is required. Speech is rendered locally to a temporary WAV, limited/amplified beyond ordinary SAPI playback level, played through Windows desktop audio, and deleted. It runs on a separate STA thread so speaking does not block planting. The queue holds at most two pending lines; urgent lines wait ~5.5 s, normal lines ~8 s, giving a 5–10 s quiet gap after each completed line with no overlap and priority for breach/loss reactions.

`PersonaLines.cs` contains **524** prewritten, confident/playful lines across 12 categories (`connect`, `start`, `loss`, `breach`, `horde`, `ordinary`, `PutZombie`, `PutPlant`, `ClearAllPlants`, `KillAllZombies`, `SetSun`, `other`). It avoids the five most recently chosen lines and never harasses individual viewers or uses threats/slurs. These are **not** dynamically generated AI responses and do not refer to individual commenters.

`SubtitleWindow` shows the spoken line in a click-through desktop overlay above the PvZ window. It is transparent, does not take focus, and hides after 4.5–9 s. **Game Capture** in OBS/TikTok LIVE Studio will omit it because it is a separate window — use **Display Capture** if you need captions on stream, or keep voice audio only. The streaming app still needs desktop/system audio capture for voice. The dedicated `CompositeStreamWindow` single-source capture was removed in this build to reduce lag and window count; only PvZ and the controller are now required.

## 9. Controller window and visible controls

The header shows connection state and game details: sun, UI, mode, scene, and Endless round. Resources show always-on auto-collect and the 5,000,000-sun target; the Free planting checkbox is optional. Bot control shows current bot status, the automatic virtual arsenal setting, voice mute, and the uninterrupted Day setting. Manual battlefield controls can choose a plant or zombie, row, and column, then place/spawn it. The action log shows bot actions, voice lines, StreamToEarn effects, and errors. The red Emergency Stop button attempts to restore controller-owned changes and disconnect.

Connect/Disconnect and Start/Pause/Stop buttons still exist in source but are hidden in the current always-on interface. Do not infer they are visible user controls. The controller automatically connects and runs when PvZ is open. The manual plant/zombie buttons are separate from the autonomous bot. Placing/spawning entities through either path changes the current game directly.

## 10. Building, checks, and updating the release

From `G:\pvz\Source`, run `./build.ps1` in PowerShell. This builds the solution in Release mode and requires an installed .NET 10 SDK. The installed controller under `Controller\PvZController-Smart` is self-contained and does not rely on that SDK. `dotnet publish PvZController\PvZController.csproj -c Release -r win-x64 --self-contained true -o <temporary-folder>` creates a new release candidate.

To deploy a new release, first test it, stop the watcher and controller while leaving PvZ alone, copy the published files into `Controller\PvZController-Smart`, **preserve `Watch-PvZ.ps1`**, verify the deployed DLL hash, remove the temporary publish folder, and restart the watcher. The watcher will reopen the controller if PvZ is still running. Do not copy into an actively running EXE directory and assume every file updated. The current installed controller DLL SHA-256 is `6A3448D60AA3A89102E7024A0A90AA51440A4B19503248D4841F66FEA5F8CD49` (EXE 162304 bytes); it will change after future builds.

The `PvZController.LiveCheck` utility includes synthetic checks for defense priorities, all-plant cooldowns, layout, Cob Cannon, horde behavior, heavy-zombie tactics, and flying-zombie response. A live `--rounds` check reports UI/mode/endless round read-only. Prefer read-only checks during valuable gameplay. A prior test that called the Survival setup while already in battle caused an access-violation crash; that test was removed and the setup routine now has a chooser-only guard. A post-fix live run stayed in battle (UI 3) for >15 minutes at endlessRound 1 with continuous spawns and no chooser.

## 11. Troubleshooting

| Symptom | First check |
| --- | --- |
| Controller does not appear after launching PvZ | Confirm Windows sign-in startup entry `PvZControllerWatcher`; check `Watch-PvZ.ps1` is running and the EXE exists in `Controller\PvZController-Smart`. The watcher only runs after user sign-in. |
| Controller says disconnected | Check the game process path and SHA-256 against the verified build. A different PvZ edition needs a separate profile. |
| Bot waits at menu | Enter Choose Your Plants or battle. The controller does not navigate the game's main menu for you. |
| Round looks reset | Check `Endless round` in the controller. Uninterrupted mode intentionally keeps it at 1 (native wave timer frozen); difficulty is via spawner, not flag count. Old lost progress is not reconstructed. |
| Voice is silent/quiet on stream | Check Bot voice checkbox, Windows output device and volume mixer, and that the streaming app captures desktop audio. The controller amplifies the local WAV before playback. |
| Subtitle not on stream | Subtitles are a desktop overlay (`SubtitleWindow`); **Game Capture** will omit them — use **Display Capture** if you need captions on stream, or rely on voice audio. |
| StreamToEarn event does nothing | Confirm the controller reports its bridge ready on port 8082, StreamToEarn is open and connected, and the effect ID is one of the implemented IDs above. |
| Crowded game slows or crashes | Reduce incoming StreamToEarn event volume and zombie effects. The controller caps its queue (256) and viewer spawns (45) and uninterrupted spawner caps live zombies at 32, but PvZ's own engine still has finite limits. Check Windows Application event log if PvZ exits unexpectedly. |

## 12. Ownership, limits, and future work

The controller source in `Source` is the custom implementation to modify. The game distribution in `Game` and the separate StreamToEarn installation are third-party components; do not copy their proprietary code or assets into the controller. The 1.2.0.1073 offsets and call targets are interoperability information for this exact build. The app is not a generalized PvZ mod loader.

Current limitations: no TikTok comment text feed, no dynamic language model, no guarantee of a human-like voice, no guarantee against engine crashes under extreme crowd (even with 32-live cap), and no in-game subtitle for Game Capture (desktop overlay only). The old Survival flag image never appears in uninterrupted mode, but the first seed chooser still briefly appears when starting Survival; it is auto-filled and not repeated. A future feature can add a verified comment source and local moderation before any chat-aware voice response. Another future change could make Emergency Stop persist until an explicit user restart, rather than allowing the always-on attach loop to reconnect.

When changing the project, update this guide and the short `Source\README.md` together. Test on the exact game build before replacing the one installed controller release.
