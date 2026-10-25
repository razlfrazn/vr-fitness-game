# vr-fitness-game

An open-source VR fitness game for Meta Quest 3, driven by a real exercise machine (starting with a Wahoo KICKR Core 2 bike trainer; rower and recumbent support planned).

The game reads live power, cadence and speed from the machine (via the separate `kickr-bridge` project) and can change the machine's resistance during play: hills, power-ups, and 4D effects like a fan or water spray driven by ESP32 add-ons.

## Status
Planning. The game engine choice (Unity or the fully open-source Godot) is still being decided.

## Folders
- `docs/` - project notes, research and the build to-do list
- `game/` - the game project goes here once the engine is chosen
- `tools/` - the fake KICKR data sender, and `zwift_ride/`, which forwards Zwift Ride button presses to the game on UDP 5006

## Related
- `kickr-bridge` - reads the trainer and sends data to this game over UDP port 5005

## Troubleshooting
- **Zwift Ride handlebar buttons stop responding:** connect the bike to the free Zwift app about once every 24 hours (the Ride's daily unlock), then close Zwift and restart `tools/zwift_ride/zwift_play_listen.py`. See `docs/DESIGN.md` (Controls).
