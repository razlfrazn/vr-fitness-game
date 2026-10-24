# KickrCubeTest

A tiny Unity scene that proves the KICKR → PC → Unity data path works: when the
Wahoo KICKR Core 2 flywheel turns, an orange cube rolls forward along a track.

- Unity **6000.6.7f1**, Universal Render Pipeline (URP "Universal 3D" template)
- Scene: `Assets/Scenes/KickrCubeTest.unity` (first scene in Build Settings)
- No VR yet — this runs in the normal Unity Game view on the PC.

## What's in here

| File | What it does |
|---|---|
| `Assets/Scripts/KickrUdpReceiver.cs` | Listens on UDP port **5005** (changeable in the Inspector) on a background thread, parses the bridge's JSON, and exposes speed, cadence, power, packet count and "seconds since last packet". Closes the socket when you leave Play mode. |
| `Assets/Scripts/KickrCubeMover.cs` | Moves the cube forward at the trainer speed (km/h ÷ 3.6 = m/s, times **Speed Multiplier**), rolls it like a wheel, and draws the on-screen readout. |
| `Assets/Scripts/FollowCamera.cs`, `TrackMarkers.cs` | Chase camera, and a ground plane + marker posts that keep up with the cube so you can see it move. |
| `Assets/Editor/KickrSceneBuilder.cs` | Builds the scene from code. Menu **Kickr → Rebuild KickrCubeTest Scene**. |
| `Assets/Tests/` | Automated EditMode + PlayMode tests (Unity Test Framework). |

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

Wait for `Streaming.` Don't type numbers into the bridge window: a number + Enter sets the trainer's
target power (ERG mode). Just leave it running; `q` + Enter quits.

**No trainer? Use the fake sender** (plain Python, no installs needed), from the repo root:

```powershell
python tools\fake_kickr_udp.py                    # ride-like data: ~20-32 km/h, ~90 rpm, power
python tools\fake_kickr_udp.py --mode handturn    # like spinning the flywheel by hand (power/cadence 0)
python tools\fake_kickr_udp.py --mode constant --speed 15 --duration 30
```

Press **Ctrl+C** to stop it.

## 3. Press Play

Press the ▶ **Play** button at the top of the editor. The top-left readout shows:

- **state** — `waiting for bridge…` (listening, no data yet), `LIVE`, or `STALE` (no data for 2 s)
- speed (km/h), cadence (rpm), power (W), packet count and how long ago the last packet came in

Spin the flywheel (or run the fake sender) and the cube rolls forward. With no bike on the
trainer, hand-spinning gives speed but power and cadence usually read 0, which is normal.

Tip: select **KickrCube** in the Hierarchy and raise **Speed Multiplier** to exaggerate the motion.
Press Play again to stop; the UDP port is released automatically.

**Readout says NOT LISTENING / "Could not bind UDP port 5005"?** Something else already has the
port: usually a second Unity editor or a built player that's still running. Close it, or change
**Port** on the **KickrReceiver** object (and send to that port with `--port`). The bridge itself
only *sends* to 5005, so it never blocks the port.

## Run the tests

In Unity: **Window → General → Test Runner**, then run **EditMode** and **PlayMode**. From a terminal, with the editor closed:

```powershell
unity test . --mode EditMode --output editmode-results.xml
unity test . --mode PlayMode --output playmode-results.xml
```

The PlayMode tests send real UDP packets to a test port (5098, so they never clash with the bridge),
check the parsed values, check that the cube moved and spun, and check that the port is freed again.

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
