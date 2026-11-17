# PvZ Controller — milestone 1

This Windows controller targets one verified executable only:

- Plants vs. Zombies GOTY English 1.2.0.1073
- SHA-256 `1FF5A2DCF009B453B8866783A68E8D04BD0D066ABDA3F8196D75A677366B2DFF`
- Expected path on this machine: `G:\pvz\Game\Plants Vs Zombies GOTY\Plants vs. Zombies\PlantsVsZombies.exe`

It will reject the outer EA launcher and any game executable with another fingerprint.

## Included

- Verified attach/detach and 500 ms state display
- Read and set sun
- Auto-collect patch with original-byte verification and restoration
- Automatic collection of the end-of-level money bag
- Balanced row formation with automatic shovel cleanup after the lawn is built
- Free-planting flag
- Place selected plants through PvZ's own 1.2.0.1073 game function
- Spawn selected zombies through PvZ's own 1.2.0.1073 game function
- Autonomous plant selection and formation building with Start/Pause/Stop
- Emergency stop and action log

The current strategy builds two Sunflower columns, three attack columns, a Wall-nut line, and two Repeater columns. It adds Lily Pads on Pool/Fog water rows and Flower Pots on Roof before the chosen plant. The TikTok Live connector is not part of this milestone.

## Build

Run `dotnet build PvZController.slnx -c Release`, or use `build.ps1`. The output is under `PvZController\bin\Release\net10.0-windows`.

For placement controls, start the actual game, enter seed selection or a battle, connect the controller, then choose a row and column. Row 6 is accepted only for Pool and Fog scenes.

The controller restores patches it owns when Disconnect, Emergency Stop, or normal window close occurs. A process crash can prevent cleanup, so every patch is verified against its expected original byte before it is enabled.

