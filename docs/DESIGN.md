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
- **Target controller: Zwift Ride** (the handlebar controllers above). Zwift Play is not the target; the listener also decodes it, as a bonus only.
- **Requirement (daily unlock):** the rider must connect the bike to the **free Zwift app about once every 24 hours**. Ride firmware 1.3 or later stays unlocked for about a day after that; otherwise the controllers hide their Zwift service and **the handlebar buttons stop responding** in our game (no button events reach UDP 5006). This is a known requirement we accept, not a bug to fix. [BikeControl notes](https://bikecontrol.app/blog/zwift-click-v2-with-other-trainer-apps/#zwift-ride-v2)
- **Button forwarder:** `tools/zwift_ride/zwift_play_listen.py` (working copy: `C:\Users\odder\Projects\zwift-play-test\`). Python + bleak. It connects to the Ride, logs to `events.log` next to the script and sends one UDP JSON message per press and release to `127.0.0.1:5006`:
  `{"kind":"press","btn":"A","button":"A","side":"right","unit":"LEFT","pressed":true,"address":"CC:A3:D1:BA:46:D6","device":"Ride","raw":"23 08 ef ...","t":...}`
  - `side` is the **physical** handlebar side of the button (A/B/Y/Z and right shift/power-up = `right`; D-pad and left shift/power-up = `left`). `unit` is the controller unit that reported it.
  - Because the left unit relays the right unit, one A press can arrive twice (from `LEFT` and `RIGHT`, about 4 ms apart). Unity keeps a held state per unit and treats a button as held while **any** unit holds it, so a press is still one press.
  - Run it with `python zwift_play_listen.py --left-only`. The left unit relays every right-unit input (A, B, Y, Z, right shift, power-up, paddle) one way, so connecting only the left unit still delivers the right-side A. It also avoids the duplicate events and uses one Bluetooth connection instead of two. It refuses port 5005 (bridge data) and `--no-udp` turns the forwarding off.
  - Each press/release also carries `code`: the bit mask (`"0x00010"`) or analog location (`"analog 0"`). A cleared field-1 bit outside the 16 known ones is forwarded as `UNKNOWN_0x10000` (code `0x10000`) and an unknown analog location as `ANALOG_n`, instead of being dropped. An unknown opcode is forwarded as `{"kind":"message","name":"UNKNOWN","op":...,"raw":...}`.
- **Button map overlay:** the **ZwiftRideButtons** object also has `ZwiftRideButtonOverlay`, a HUD box to the right of the Distance and Boost boxes (`KickrHud.ButtonMapBox`, 302 x 356 px at x 790; same style and pixel scale, fits the 1102 x 505 editor Game view). It shows all 18 Ride inputs in Left and Right columns, and each one lights green while held on either unit. Inputs the map doesn't know are listed raw on the `Unknown:` line (held codes, plus the last unknown code or message with its bytes). The map is `ZwiftRideButtonMap`, which mirrors `RIDE_BUTTONS` / `ANALOG_LOC` in the forwarder.

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
- **A (right):** use a gem boost (see Section 3). This is mapping `boostButton = "A"`, `boostButtonSide = "right"` on the **BoostGems** object (`BoostController`). Any button name from the table works, e.g. `"Y"`, `"RIGHT_POWERUP"`; the side can be `right`, `left` or `any`. **Space** on the keyboard is a hidden test fallback (`keyboardFallback`).

---

## 2. Trainer and bike data

### Hardware
- **Wahoo KICKR Core 2**, BLE name `KICKR CORE C561`, address `E5:5E:61:99:89:FE`. There is no bike on it yet, so testing is done by hand-turning the flywheel.

### KICKR bridge (`razlfrazn/kickr-bridge`)
- Python with bleak, running on ProjectX. It reads FTMS Indoor Bike Data (`0x2AD2`) and streams **speed (km/h), cadence (rpm) and power (W)** as UDP JSON to `127.0.0.1:5005` at about 1 Hz. It also logs a CSV.
- On connect it sends FTMS Request Control (`0x00`); the trainer replies `80 00 01`, which means success.
- **Limitation:** while the bridge is connected, Windows won't let a second process reach the FTMS service. All trainer control therefore has to go through the bridge.
- **Command port (done):** the bridge listens on UDP `127.0.0.1:5007` for JSON `{"cmd":"resistance","level":N}`. It clamps N to 0–100, writes FTMS `04 N` to the control point and logs the reply (`80 04 01` = success). If commands pile up, only the newest is written. Typing watts into the bridge (ERG) still works. Target power and simulation commands are not on the port yet.
- **Terrain → resistance mapping (V1, set in the Unity Inspector):** level = **30** (flat baseline) + **3 per % grade** uphill and **3 per % grade** downhill, rounded, clamped **0–100**. Sent only when the level changes by **≥1**, at most **2 per second** (0.5 s minimum gap). Leaving Play sends the baseline (30) again.
  - Examples on the V1 course: +6 % → 48, +10 % → 60, −3 % → 21, −5 % → 15, −8 % → 6.
- **Zone resistance (set in the Unity Inspector, `ZoneResistance`):** inside a red slowdown zone, level = terrain level **+ 20**; inside a green boost zone, level = terrain level **− 20**. Offsets 0–100, result clamped 0–100. Overlapping zones of the same colour don't stack; red and green overlapping cancel out.
  - Rate limiting still applies, but zone edges are never dropped: entering a zone is held and sent on the next allowed send (≤0.5 s); leaving sends the recomputed terrain level immediately, even inside the 0.5 s gap. If you leave before the zone level went out, nothing is sent (the trainer is already at the terrain level).
- **Verified live (2026-10-25):** level 40 then 30 via UDP 5007; both replied `80 04 01`.

### FTMS control (confirmed on this trainer)
- **Feature flags:** machine `0x00004003`, target settings `0x0000600c`. Resistance target, power target and indoor bike simulation are all supported.
- **Three control modes:**
  1. **Resistance level** (`0x04`). The supported range is 0 to 100, step 1. Setting 60 returned `80 04 01` (success). **Confirmed working.**
  2. **Target power / ERG** (`0x05`), in watts. The supported range is 0 to 2000 W, step 1.
  3. **Simulation** (`0x11`): grade (slope), wind speed, rolling resistance (Crr) and wind resistance (Cw).
- **There is no gear command in FTMS.** Gears are simulated in-game by changing simulated slope or resistance per gear. Zwift's own virtual shifting uses a proprietary Wahoo/Zwift channel that hasn't been decoded; we're not using it.
- The trainer doesn't report its current resistance level in Indoor Bike Data. Confirm changes through speed and power instead.

### Unity test project (`vr-fitness-game/game/KickrCubeTest`)
- Unity 6000.6.7f1 (Unity 6.6), URP. `KickrUdpReceiver` listens on UDP 5005. `KickrCubeMover` moves the cube forward along the course at km/h ÷ 3.6 × `speedMultiplier` × the active boost/slowdown multiplier. There is no reverse. `KickrResistanceSender` sends resistance levels to UDP 5007.
- **Verified live:** it rolled about 33 m on hand-turned flywheel data, with no console errors. 8 automated tests pass.

---

## 3. Gameplay features

### V1 scope
- **Course:** a straight path with hills and valleys.
- **Terrain to resistance:** path pitch feeds the trainer's resistance (FTMS simulation grade or resistance level). Uphill is harder, downhill is easier.
- **Speed-boost and slowdown sprites:** trigger zones on the path. Riding through one gives a temporary speed boost, or slows you down.
- **Art:** gray-box (placeholder geometry only).
- **Controls:** D-pad steering, shift buttons for virtual gears, paddles as the combined brake.

### Built so far (V1 gray-box, 2026-10-25)
- **Course:** 500 m straight road along +Z, 10 sections, each flat or a constant grade, gray-box slabs on an embankment with a start and finish line and marker posts every 10 m. The cube follows the road height and loops back to the start at the end.
  - 0–40 flat · 40–100 +6 % · 100–130 flat · 130–190 −8 % · 190–230 flat · 230–290 +10 % · 290–320 flat · 320–380 −5 % · 380–440 −3 % · 440–500 flat. Highest point 4.8 m, lowest −1.2 m.
- **Terrain → resistance:** uses the resistance level (`0x04`) mode, not simulation, for V1. Mapping values are in Section 2.
- **Boost and slowdown zones:** translucent boxes on the road. Green = boost, ×1.5 speed for 3 s. Red = slowdown, ×0.6 for 3 s. Multipliers and durations are set per zone in the Inspector. Entering a zone of the same kind again restarts its timer; a boost and a slowdown at the same time multiply. Zones trigger when the cube's distance crosses the zone start (no physics).
  - Boost at 70 m, 260 m, 470 m. Slowdown at 160 m, 350 m.
- **Zones also change resistance:** red = terrain level +20 (harder), green = terrain level −20 (easier) while the cube is inside the 4 m zone, then back to the terrain level. Example on the +10 % climb: 60 → 40 in a green zone → 60. Values are in Section 2.
- **Distance tracker:** the KICKR reports no distance, so distance comes from the cube's progress along the course: total = completed laps × 500 m + position on the current lap (horizontal course metres, `CourseDistance` + `KickrCubeMover.CourseDistanceMeters`). A "Distance" HUD box to the right of the KICKR box shows `Total: 1.23 km  (1234 m)` and `Lap 3: 234 / 500 m`. All HUD boxes share one style and layout (`KickrHud`: font size 20, unscaled screen pixels), shown 1:1 at Game view Scale 1x.
- **Boost gems and boost count (V1):**
  - **Gems:** 10 per lap (`gemsPerLap`), spread about every 50 m over the lap. Climbs count double when spacing them, so each climb gets at least two. Each gem is kept at least 4 m (`zoneClearance`) from the green and red zones. V1 positions: 31, 64, 97.5, 154, 219, 254, 286.5, 344, 407, 464 m (climb 1 gets 64 and 97.5; climb 2 gets 254 and 286.5).
  - **Look:** gray-box emissive cyan cubes floating 0.9 m above the road height (`TrackProfile`), with a slow spin and bob. They are spawned at runtime from the profile and come back every lap.
  - **Collection:** a gem is collected when the course distance crosses its position. This handles the 500 m lap wrap, never skips a gem on a big step and never collects one twice per lap. A pickup pops the gem (scale up plus a light flash) with no audio.
  - **Boost count:** each session starts with 2 boosts (`startingBoosts`); each gem adds `gemValue` (1). It carries across laps, with no meter. The HUD box under Distance shows `Boosts: N`, plus `BOOST x.x s` while a boost runs and `Gems this lap: a / 10`.
  - **Using a boost:** press **A on the right controller** when the count is at least 1 and no boost is running. It reacts to the edge, so holding A uses one boost. For **10 s** (`boostDuration`) the trainer gets **terrain level + zone offsets − 15** (`boostResistanceReduction`), clamped 0–100, and the cube gets **×1.3** speed (`speedMultiplier`). When it ends, the recomputed level is sent straight away; like a zone edge, it bypasses the 0.5 s gap. Examples: flat 30 → 15 → 30; +10 % climb 60 → 45; boost inside a red zone 30 + 20 − 15 = 35.
  - **Input path:** Ride → listener (BLE) → UDP JSON `127.0.0.1:5006` → `ZwiftRideUdpReceiver` (loopback only) → `BoostController`.
- **Not done yet:** steering, gears and brake. All Ride buttons reach Unity and show on the button map overlay, but only A drives gameplay so far.

---

## 4. Future work
- **Real steering:** read handlebar rotation with an IMU or encoder (for example an ESP32 add-on) for twistable bars, replacing the D-pad stand-in.
- Add target-power and simulation (grade) commands to the bridge's 5007 command port (resistance level is done). Consider switching terrain to FTMS simulation grade once a real bike is mounted.
- Confirm the left power-up button, and test both on/off buttons.
- Wire the rest of the Ride input (already arriving on UDP 5006) to the gears, brake and steering. The paddles need the analog values forwarded too.
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
- 2026-10-25: V1 gray-box built: 500 m course (+6/+10 % climbs, −8/−5/−3 % descents), 3 boost (×1.5, 3 s) and 2 slowdown (×0.6, 3 s) zones; re-entering refreshes the timer.
- 2026-10-25: Terrain drives the trainer via FTMS resistance level: 30 + 3 per % grade, clamped 0–100, sent on change ≥1 at ≤2 Hz from Unity to the bridge on UDP 5007.
- 2026-10-25: Zones change resistance too: red slowdown zones send terrain level +20, green boost zones terrain level −20 (Inspector offsets, clamped 0–100), restored to the recomputed terrain level on exit. Zone enters wait for the next allowed send; restores bypass the 0.5 s limit. Speed multipliers unchanged (×1.5 / ×0.6).
- 2026-10-25: Distance tracker added: course-derived (laps × 500 m + lap position), not from the KICKR; shown in a HUD box sharing the KICKR/Terrain box style (`KickrHud`).
- 2026-10-25: Target controller is the **Zwift Ride** (handlebar units), not Zwift Play (Play decoding kept as a bonus). **Known requirement:** the rider connects the bike to the free Zwift app about once every 24 h (daily unlock), or the handlebar buttons stop responding.
- 2026-10-25: Boost gems added: 10 per lap (about every 50 m, at least 2 per climb, at least 4 m from zones), collected by distance crossing, respawning each lap. Boost count starts at 2, +1 per gem, −1 per use.
- 2026-10-25: Zwift Ride button map overlay: every Ride input (16 buttons + 2 paddles) grouped Left/Right, lit while held, unknown codes shown raw; driven by the UDP 5006 events. The forwarder now forwards unknown bits and opcodes raw instead of dropping them.
- 2026-10-25: Right **A** button = use a boost. Edge-triggered, only with at least 1 boost and none running. Effect: −15 resistance on top of terrain and zones (clamped 0–100) and ×1.3 speed for 10 s. Ride buttons reach Unity as UDP JSON on 127.0.0.1:5006 from the Python forwarder; Space is a hidden test fallback.
