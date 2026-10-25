# KickrCubeTest

A gray-box Unity scene driven by the Wahoo KICKR Core 2: when the flywheel turns, an orange cube
rides a 500 m straight course with hills and valleys. The slope under the cube sets the trainer's
resistance (uphill harder, downhill easier), and green/red zones on the road give a short speed boost
or slowdown. Floating gems along the road give you boosts, and you use them with the **A** button on
the **Zwift Ride** handlebar controller.

- Unity **6000.6.7f1**, Universal Render Pipeline (URP "Universal 3D" template)
- Scene: `Assets/Scenes/KickrCubeTest.unity` (first scene in Build Settings)
- No VR yet — this runs in the normal Unity Game view on the PC.

## What's in here

| File | What it does |
|---|---|
| `Assets/Scripts/KickrUdpReceiver.cs` | Listens on UDP port **5005** (changeable in the Inspector) on a background thread, parses the bridge's JSON, and exposes speed, cadence, power, packet count and "seconds since last packet". Closes the socket when you leave Play mode. |
| `Assets/Scripts/KickrCubeMover.cs` | Moves the cube along the course at the trainer speed (km/h ÷ 3.6 = m/s, × **Speed Multiplier** × any active boost/slowdown), keeps it on the road height, loops back to the start at the end, rolls it like a wheel, and draws the on-screen readout. |
| `Assets/Scripts/TrackProfile.cs`, `KickrTrack.cs` | The course as a list of sections (length + grade %). Gives height, grade and pitch at any distance. Edit the sections on the **Track** object. |
| `Assets/Scripts/TerrainResistanceMapper.cs` | Grade → trainer resistance level (see table below), plus the rate limiter. Plain C#, unit tested. |
| `Assets/Scripts/KickrResistanceSender.cs` | On the **TrainerResistance** object. Reads the grade under the cube, adds the zone offset (below) and sends `{"cmd":"resistance","level":N}` to the bridge on UDP **5007**. Untick **Send To Trainer** to only show the value. Sends the baseline again when you leave Play. |
| `Assets/Scripts/ZoneResistance.cs` | Zone resistance: red zone = terrain level **+ Slowdown Offset**, green zone = terrain level **− Boost Offset**, and the controller that makes sure zone enter/leave changes are never dropped by the rate limiter. Plain C#; the UDP sender sits behind `IResistanceCommandSender` so tests use a mock. |
| `Assets/Scripts/SpeedEffects.cs`, `SpeedZone.cs` | Boost (green, ×1.5) and slowdown (red, ×0.6) zones. Riding into one starts a timed effect (3 s); riding into the same kind again restarts the timer. |
| `Assets/Scripts/BoostGems.cs`, `BoostController.cs` | Boost gems and boost count. The gem layout (10 per lap, clear of zones, at least 2 per climb), distance-crossing pickup, the boost inventory (start 2, +1 per gem, −1 per use) and the A-button edge. On the **BoostGems** object: settings, `boostButton` / `boostButtonSide` (A / right) and the Space test fallback. A boost is −15 resistance and ×1.3 speed for 10 s. |
| `Assets/Scripts/ZwiftRideUdpReceiver.cs` | On the **ZwiftRideButtons** object. Listens on UDP **5006** (loopback) for the Ride button JSON from the forwarder, and tracks which buttons are held per controller unit. |
| `Assets/Scripts/FollowCamera.cs`, `TrackMarkers.cs` | Chase camera, and marker posts along the course (every 10 m, taller every 50 m). |
| `Assets/Editor/KickrSceneBuilder.cs` | Builds the whole scene from code (road, embankment, zones, markers). Menu **Kickr → Rebuild KickrCubeTest Scene**. |
| `Assets/Tests/` | Automated EditMode + PlayMode tests (Unity Test Framework). |

## The V1 course (500 m)

| Distance | Section | Grade | Resistance level |
|---|---|---|---|
| 0–40 m | Start flat | 0 % | 30 |
| 40–100 m | Climb 1 | +6 % | 48 |
| 100–130 m | Summit 1 | 0 % | 30 |
| 130–190 m | Descent 1 | −8 % | 6 |
| 190–230 m | Valley | 0 % | 30 |
| 230–290 m | Climb 2 | +10 % | 60 |
| 290–320 m | Summit 2 | 0 % | 30 |
| 320–380 m | Descent 2 | −5 % | 15 |
| 380–440 m | Run-out | −3 % | 21 |
| 440–500 m | Finish straight | 0 % | 30 |

Zones (4 m long, centred at): **boost** 70 m, 260 m, 470 m; **slowdown** 160 m, 350 m.

**Zone resistance** (Inspector on **TrainerResistance → Zone Resistance**): while the cube is inside a
**red** zone the trainer gets terrain level **+ 20** (*Slowdown Offset*); inside a **green** zone it gets
terrain level **− 20** (*Boost Offset*). Both offsets are 0–100 and the result is clamped to 0–100.
On leaving, the terrain level for the current grade is sent again. Examples: red on the flat 30 → 50 → 30;
green on the +10 % climb 60 → 40 → 60; green on the −8 % descent 6 → 0 → 6. Entering a zone is sent on
the next allowed send (within 0.5 s); the restore on leaving is sent straight away, even inside the
0.5 s gap. The speed effects (×1.5 / ×0.6 for 3 s) are unchanged. The zones are only 4 m long, so at
36 km/h you are inside one for about 0.4 s.

**Resistance mapping** (Inspector on **TrainerResistance → Mapping**): level = 30 + 3 × grade %
(separate uphill and downhill gains, both 3), rounded and clamped to 0–100. Commands are only sent when
the level changes by at least 1, and at most twice a second.

**Boost gems** (cyan cubes floating over the road): 10 per lap at 31, 64, 97.5, 154, 219, 254, 286.5,
344, 407 and 464 m. Riding past one collects it and adds a boost. You start each session with **2**.
Press **A on the right Zwift Ride controller** to use one: for **10 s** the trainer gets **15 easier**
(added on top of terrain and zones, clamped 0–100) and the cube goes **×1.3** faster. The HUD box under
Distance shows `Boosts: N` and the seconds left. While a boost is running, more presses do nothing; at
0 boosts, A does nothing. All values are on the **BoostGems** object. **Space** does the same as A, for
testing without the controller.

## 1. Open the project

1. Open **Unity Hub** → **Projects** → **Add** → **Add project from disk**.
2. Pick this folder: `vr-fitness-game/game/KickrCubeTest`.
3. Open it with Unity **6000.6.7f1**. The first open takes a few minutes while Unity builds its `Library/` cache.
4. In the **Project** window, double-click `Assets/Scenes/KickrCubeTest`.

## 2. Start the data source (pick one)

**Real trainer** — close Zwift / the Wahoo app first (the trainer only allows a couple of Bluetooth connections), give the flywheel a spin to wake it, then in a terminal:

```powershell
cd C:\Users\odder\Projects\kickr-bridge
python kickr_bridge.py
```

Wait for `Streaming.` and `Listening for commands on UDP 127.0.0.1:5007`. Don't type numbers into the bridge window: a number + Enter sets the trainer's
target power (ERG mode). Just leave it running; `q` + Enter quits.

**No trainer? Use the fake sender** (plain Python, no installs needed), from the repo root:

```powershell
python tools\fake_kickr_udp.py                    # ride-like data: ~20-32 km/h, ~90 rpm, power
python tools\fake_kickr_udp.py --mode handturn    # like spinning the flywheel by hand (power/cadence 0)
python tools\fake_kickr_udp.py --mode constant --speed 15 --duration 30
```

Press **Ctrl+C** to stop it.

**Zwift Ride buttons (for the A boost)** — in a second terminal, from the repo root:

```powershell
cd tools\zwift_ride
py -m pip install bleak          # once
python zwift_play_listen.py --left-only
```

Wait for the handshake, then press A: you should see `PRESS   A` in the window. The script sends each
press and release to Unity on UDP **127.0.0.1:5006**. Ctrl+C stops it. Keep it out of automated tests:
the tests use their own fake sender.

## 3. Press Play

Press the ▶ **Play** button at the top of the editor. The top-left readout shows:

- **state** — `waiting for bridge…` (listening, no data yet), `LIVE`, or `STALE` (no data for 2 s)
- speed (km/h), cadence (rpm), power (W), packet count and how long ago the last packet came in

- the current section, grade, resistance level being sent (with a RED +20 / GREEN −20 tag while inside a zone), and any active BOOST / SLOW effect

Spin the flywheel (or run the fake sender) and the cube rolls forward over the hills. With the bridge
running, the trainer gets heavier on climbs and lighter on descents. With no bike on the
trainer, hand-spinning gives speed but power and cadence usually read 0, which is normal.

Tip: select **KickrCube** in the Hierarchy and raise **Speed Multiplier** to exaggerate the motion.
Press Play again to stop; the UDP port is released automatically.

**Readout says NOT LISTENING / "Could not bind UDP port 5005"?** Something else already has the
port: usually a second Unity editor or a built player that's still running. Close it, or change
**Port** on the **KickrReceiver** object (and send to that port with `--port`). The bridge itself
only *sends* to 5005, so it never blocks the port.

**Zwift Ride buttons stop responding / no `PRESS` lines?** The Ride needs its **daily unlock**: connect the
bike to the **free Zwift app** about once every 24 hours (sign in, pair the controllers and ride or sit on
the pairing screen for a moment), then close Zwift and restart `zwift_play_listen.py`. Without it, the
controllers hide their Zwift service and the buttons do nothing in this game. Also check that Zwift is
closed while you play (it holds the Bluetooth connection), and that the Console has no
`[ZwiftRideUdpReceiver] Could not bind UDP port 5006` warning (another editor or player has the port).

**Audio is turned off** (Project Settings → Audio → *Disable Unity Audio*). The game has no sound yet,
and on ProjectX the only audio device is RDP's *Remote Audio*, which made Unity's audio system crash the
editor when entering Play mode. Untick it when sound is added and a real audio device is present.

## Run the tests

In Unity: **Window → General → Test Runner**, then run **EditMode** and **PlayMode**. From a terminal, with the editor closed:

```powershell
unity test . --mode EditMode --output editmode-results.xml
unity test . --mode PlayMode --output playmode-results.xml
```

The PlayMode tests send real UDP packets to a test port (5098, so they never clash with the bridge),
check the parsed values, check that the cube moved and spun, follows the terrain height and gets boosted
by a zone, and check that the port is freed again. EditMode tests cover terrain sampling, the grade →
resistance mapping, the rate limiter, the command JSON, boost/slowdown timing and the scene wiring.
The zone-resistance tests (EditMode, plus one PlayMode test that drives the real component) use a mock
bridge sender that records the levels, so **no command ever reaches the real trainer** during tests.
The boost tests cover the gem layout, pickup (lap wrap and big steps), boost count, the A-button edge,
the ±15 resistance and the 5006 button JSON. One PlayMode test sends fake Ride A presses over UDP to a test
port (5094) and checks one boost, 30 → 15 → 30 through the mock bridge, and the gem pickups.
The forwarder's own tests need no hardware: `cd tools\zwift_ride; python -m unittest -v`.

## Not done yet: VR / Quest 3

Nothing here is wired to the headset yet. Next steps, roughly in order:

1. Add XR support: **Edit → Project Settings → XR Plug-in Management** → install it, enable
   **OpenXR** (with the Meta Quest feature group), or use Meta's **Meta XR SDK** packages instead.
2. Quickest way to try it: **Quest Link** (USB-C cable) or **Air Link** from the Meta Quest
   Link app on the PC. Unity's Play button then renders in the headset while the bridge keeps
   running on the PC.
3. Later, a standalone **Android build** for the Quest. The trainer data then has to reach the
   headset over Wi-Fi (send the UDP to the Quest's IP instead of 127.0.0.1), or the headset
   talks to the trainer's Bluetooth directly.
