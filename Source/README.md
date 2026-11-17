For the complete current architecture, behavior, startup, controls, StreamToEarn map, voice, save locations, tests, and limitations, read G:\pvz\PROJECT_GUIDE.md.

# PvZ base

Everything for this setup lives under G:\pvz:

- Game\ — the Plants vs. Zombies GOTY installation. The old G:\Plants.Vs.Zombies.GOTY path is a junction to this folder so existing shortcuts still work.
- Controller\PvZController-Smart\ — the current controller application and its PvZ watcher. This is the only release to run.
- Source\ — the controller source and live-check project for future changes.

Windows starts the watcher at sign-in using the current user's Run startup entry, PvZControllerWatcher. The watcher stays in the background and opens the controller when PlantsVsZombies.exe runs. The controller connects to the verified GOTY build, enables auto-collect and background play, and starts its bot automatically. The controller's local StreamToEarn bridge listens on 127.0.0.1:8082.

Run Source\build.ps1 to build future source changes. Update the installed release in Controller\PvZController-Smart after publishing and testing; preserve Watch-PvZ.ps1 there. StreamToEarn itself is a separate app and still needs its own TikTok connection.

The controller now has an always-on, free Windows voice with a mute checkbox. It speaks brief, rate-limited lines for PvZ gameplay and StreamToEarn effects. Speech runs on a separate thread and does not require an API key. StreamToEarn's free game-effect trigger does not provide live comment text to this controller, so the voice does not read or answer comments.


The voice now renders to a local WAV, applies limiting/amplification, then plays it through Windows desktop audio. It rotates among 524 cocky/playfully competitive lines and avoids the five most recent, with ~8 s normal / ~5.5 s urgent gaps and no overlap. A click-through subtitle appears above PvZ (Display Capture includes it; Game Capture omits it).

Uninterrupted Day (2026-09-23): When the “Uninterrupted Day” checkbox is checked (default), battle stays on one Day lawn (5 rows, no water) with native wave timer frozen (BoardZombieCountDown/HugeWave/NextSurvival/ProgressMeter suppressed) and no Crazy Dave / “More zombies” / chooser / flag HUD. Zombies are spawned continuously by UninterruptedSpawner (≤32 live, 2.6→1.1 s intervals, tiered pools) for a rising difficulty curve until loss or exit. Unchecking falls back to classic Survival flag-advance.


