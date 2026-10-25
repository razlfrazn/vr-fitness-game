# Back To Back: Living Design Document

> **This is a living document.** Update it every time a decision is made about features, controls, or hardware. Put new decisions in the right section and add a line to the Decision Log at the bottom.

Last updated: 2026-10-25

---

## 1. Controls

### Hardware
- **Zwift Ride handlebar controllers:** two Bluetooth LE units, left and right, on firmware 1.3.0.
  - **Left:** `CC:A3:D1:BA:46:D6`, type byte `0x08`. **This is the primary unit.**
  - **Right:** `EA:5E:9D:20:12:BE`, type byte `0x07`.
- **The left unit relays the right unit's inputs. The relay is one-way:** the right unit never reports left-side inputs. The game can connect to the left unit alone and still get every input. Connecting to the right unit as well is optional, as a backup.
- **Protocol:** plain "RideOn" handshake. Write `52 69 64 65 4f 6e` to sync RX `00000003-19ca-4651-86e5-fa29dcdd09d1`, then `ff 04 00` (firmware 1.3 or later).
  - Notifications arrive on `00000002-19ca-…` and indications on `00000004-19ca-…`.
  - Button messages are type `0x23`. Field 1 is a bitmask where a **cleared bit means pressed**. Field 3 holds the analog paddles.
- **Daily unlock:** firmware 1.3 or later must be unlocked in the Zwift app (free account) about every 24 hours, or the controllers hide their Zwift service. [BikeControl notes](https://bikecontrol.app/blog/zwift-click-v2-with-other-trainer-apps/#zwift-ride-v2)
- **Listener (test):** `C:\Users\odder\Projects\zwift-play-test\zwift_play_listen.py`. It logs to `events.log` there and sends UDP JSON to `127.0.0.1:5006`.

### Input map (16 inputs, 15 confirmed so far, plus 2 untested on/off buttons)

| Side | Input | Kind | Bit or field | Status |
|---|---|---|---|---|
| Right | A | button | `0x10` | confirmed |
| Right | B | button | `0x20` | confirmed |
| Right | Y | button | `0x40` | confirmed |
| Right | Z | button | `0x80` | confirmed |
| Right | Shift up | button | `0x1000` | confirmed |
| Right | Shift down | button | `0x2000` | confirmed |
| Right | Power-up | button | `0x4000` | confirmed |
| Right | Right paddle | analog, 0 to 100 | field 3, location 1 | confirmed |
| Left | D-pad up | button | `0x2` | confirmed |
| Left | D-pad down | button | `0x8` | confirmed |
| Left | D-pad left | button | `0x1` | confirmed |
| Left | D-pad right | button | `0x4` | confirmed |
| Left | Shift up | button | `0x100` | confirmed |
| Left | Shift down | button | `0x200` | confirmed |
| Left | Left paddle | analog, 0 to −100 | field 3, location 0 | confirmed |
| Left | Power-up | button | `0x400` | **unconfirmed** (not seen yet) |
| Both | On/off | button | `0x800` / `0x8000` | **not tested yet** |

### Game control mapping (decisions)
- **Brake:** both paddles are combined into one brake value. Pulling both paddles stacks the braking.
- **Gears:** in-game virtual gears use the shift up/down buttons. There is no native gear command (see Section 2).
- **Steering, V1:** the D-pad stands in for steering. Real steering is not implemented yet; see Future Work.
- **Power-up buttons:** reserved for power-ups (see Section 3).

---

## 2. Trainer and bike data

### Hardware
- **Wahoo KICKR Core 2**, BLE name `KICKR CORE C561`, address `E5:5E:61:99:89:FE`. There is no bike on it yet, so testing is done by hand-turning the flywheel.

### KICKR bridge (`razlfrazn/kickr-bridge`)
- Python with bleak, running on ProjectX. It reads FTMS Indoor Bike Data (`0x2AD2`) and streams **speed (km/h), cadence (rpm) and power (W)** as UDP JSON to `127.0.0.1:5005` at about 1 Hz. It also logs a CSV.
- On connect it sends FTMS Request Control (`0x00`); the trainer replies `80 00 01`, which means success.
- **Limitation:** while the bridge is connected, Windows won't let a second process reach the FTMS service. All trainer control therefore has to go through the bridge. A to-do is to add resistance, power and simulation commands to it.

### FTMS control (confirmed on this trainer)
- **Feature flags:** machine `0x00004003`, target settings `0x0000600c`. Resistance target, power target and indoor bike simulation are all supported.
- **Three control modes:**
  1. **Resistance level** (`0x04`). The supported range is 0 to 100, step 1. Setting 60 returned `80 04 01` (success). **Confirmed working.**
  2. **Target power / ERG** (`0x05`), in watts. The supported range is 0 to 2000 W, step 1.
  3. **Simulation** (`0x11`): grade (slope), wind speed, rolling resistance (Crr) and wind resistance (Cw).
- **There is no gear command in FTMS.** Gears are simulated in-game by changing simulated slope or resistance per gear. Zwift's own virtual shifting uses a proprietary Wahoo/Zwift channel that hasn't been decoded; we're not using it.
- The trainer doesn't report its current resistance level in Indoor Bike Data. Confirm changes through speed and power instead.

### Unity test project (`vr-fitness-game/game/KickrCubeTest`)
- Unity 6000.6.7f1 (Unity 6.6), URP. `KickrUdpReceiver` listens on UDP 5005. `KickrCubeMover` moves a cube along +Z only, at km/h ÷ 3.6 × `speedMultiplier`. There is no reverse.
- **Verified live:** it rolled about 33 m on hand-turned flywheel data, with no console errors. 8 automated tests pass.

---

## 3. Gameplay features

### V1 scope
- **Course:** a straight path with hills and valleys.
- **Terrain to resistance:** path pitch feeds the trainer's resistance (FTMS simulation grade or resistance level). Uphill is harder, downhill is easier.
- **Speed-boost and slowdown sprites:** trigger zones on the path. Riding through one gives a temporary speed boost, or slows you down.
- **Art:** gray-box (placeholder geometry only).
- **Controls:** D-pad steering, shift buttons for virtual gears, paddles as the combined brake.

---

## 4. Future work
- **Real steering:** read handlebar rotation with an IMU or encoder (for example an ESP32 add-on) for twistable bars, replacing the D-pad stand-in.
- Add resistance, target-power and simulation commands to the bridge, so control doesn't require stopping it.
- Confirm the left power-up button, and test both on/off buttons.
- Bring the Zwift controller input into Unity (UDP 5006) and wire it to the gears, brake and steering.
- Mount a real bike and validate power and cadence.
- VR: OpenXR or the Meta XR SDK, Quest Link or Air Link, then a standalone Quest (Android) build.
- 4D effects and power-ups (ESP32 fan or water, an effort-filled reservoir, power-ups that affect a rival). See `docs/PROJECT.md`.

---

## Decision log
- 2026-10-24: KICKR bridge built. It streams to UDP 5005.
- 2026-10-24: Unity cube test project built and pushed.
- 2026-10-25: FTMS resistance control confirmed working (0 to 100 scale).
- 2026-10-25: No native gears. Gears will be simulated in-game.
- 2026-10-25: Zwift Ride controllers decoded. Left is primary and relays right, one-way.
- 2026-10-25: Both paddles combine into one brake value. The D-pad is the V1 steering stand-in.
- 2026-10-25: V1 scope set: a straight path with hills and valleys, pitch mapped to resistance, boost and slowdown trigger zones, gray-box art.
