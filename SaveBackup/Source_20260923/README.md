For the complete current architecture, behavior, startup, controls, StreamToEarn map, voice, save locations, tests, and limitations, read G:\pvz\PROJECT_GUIDE.md.

# PvZ base

Everything for this setup lives under G:\pvz:

- Game\ — the Plants vs. Zombies GOTY installation. The old G:\Plants.Vs.Zombies.GOTY path is a junction to this folder so existing shortcuts still work.
- Controller\PvZController-Smart\ — the current controller application and its PvZ watcher. This is the only release to run.
- Source\ — the controller source and live-check project for future changes.

Windows starts the watcher at sign-in using the current user's Run startup entry, PvZControllerWatcher. The watcher stays in the background and opens the controller when PlantsVsZombies.exe runs. The controller connects to the verified GOTY build, enables auto-collect and background play, and starts its bot automatically. The controller's local StreamToEarn bridge listens on 127.0.0.1:8082.

Run Source\build.ps1 to build future source changes. Update the installed release in Controller\PvZController-Smart after publishing and testing; preserve Watch-PvZ.ps1 there. StreamToEarn itself is a separate app and still needs its own TikTok connection.

The controller now has an always-on, free Windows voice with a mute checkbox. It speaks brief, rate-limited lines for PvZ gameplay and StreamToEarn effects. Speech runs on a separate thread and does not require an API key. StreamToEarn's free game-effect trigger does not provide live comment text to this controller, so the voice does not read or answer comments.


The voice now renders to a local WAV, applies limiting/amplification, then plays it through Windows desktop audio. It rotates lines and avoids the five most recent lines. A click-through subtitle appears near the top of the PvZ window while PvZ is foreground; use display capture in the streaming app if the subtitle must appear on stream. Survival Endless still has its built-in between-flags transition, but the bot immediately reuses a filled seed tray and presses Let's Rock automatically. The game's own transition may briefly appear; no unsafe wave-state patch is applied.


Survival round fix (2026-09-23): The controller now initializes the endless round counter only when it is zero; it never writes it back to 1 on later plant-selection screens. Survival layout changes are guarded to run only in the seed chooser, not during battle. The controller attempts to advance the built-in between-flags message and automatically resumes the next battle. PvZ may still briefly draw its normal transition image. The controller UI displays the current endless round for verification.


