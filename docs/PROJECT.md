# VR Fitness Gaming Project: Research Organization
Last updated: Oct 20, 2026 (Central Time)
Stack: exercise machine + Meta Quest 3 + PC (Unity). The bridge reads Bluetooth/serial data and sends it to Unity; control is written back through the same bridge.
To-do list for the bike build: vr-bike-todo.md (in this folder)

Shared notes, all categories:
- FTMS (Bluetooth Fitness Machine Service) spec is free: https://www.bluetooth.com/specifications/specs/fitness-machine-service-1-0/
  - Software that just connects to a machine through the OS Bluetooth reportedly needs no qualification.
  - Shipping your own Bluetooth hardware needs Bluetooth SIG qualification: $12,000 per design for free Adopter members, from Mar 2026 (https://www.bluetooth.com/develop-with-bluetooth/qualification-listing/qualification-listing-fees/).
- CSAFE: wired, royalty-free standard from FitLinxx (1997). Has read commands plus set commands for level, power and grade; each maker decides which it honors. Command list: https://gist.github.com/marcelrv/a05ff45e91f05e64188f6f36562c73de
- Useful libraries:
  - pycycling (Python FTMS read/control): https://github.com/zacharyedwardbull/pycycling
  - QZ / qdomyos-zwift: https://github.com/cagnulein/qdomyos-zwift
  - TrackMyIndoorWorkout: https://github.com/TrackMyIndoorWorkout/TrackMyIndoorWorkout
  - GoldenCheetah: https://github.com/GoldenCheetah/GoldenCheetah
- Heart rate can be added later through the standard Bluetooth Heart Rate Service (chest strap or watch).

==================================================================
## GAME DESIGN IDEAS: 4D effects and power-ups (Oct 20, 2026, applies to all machines)
- 4D add-ons: physical effects the game triggers in the room, like a fan for wind bonuses and squirt guns or water bombs. Build them as optional plug-in modules, likely ESP32 boards (Joseph already works with ESP32), that pair with the bridge so players can add other sensors and effects.
- Power-up reservoir: steer into power-ups, or hold an effort for a set time, to fill a reservoir. Then choose how to spend it:
  - Squirt water at a competitor, or at yourself as relief (cooling off).
  - Turn the fan on for yourself.
  - Add resistance to a competitor's machine.
- Next steps: define a simple message format from the bridge to the ESP32 add-ons (Wi-Fi or Bluetooth), and prototype a fan relay and a water pump valve.

### Zwift's game mechanics, for reference (Oct 20, 2026, from general knowledge; numbers not verified)
- Power-ups: you grab one by riding through an arch, then trigger it for a short burst. Feather (lighter), aero helmet (less drag), draft van (stronger draft), burrito (riders behind you can't draft; races), anvil (heavier for descents), ghost (invisible).
- Progression: XP and levels unlock gear. Drops currency (earned faster on climbs) buys gear in the Drop Shop that changes performance. Badges and achievements.
- Competition: timed sprint and climb segments with leaderboards and jerseys for the holder. Race categories A to E or a Racing Score. Leagues (Zwift Racing League). Drafting physics drive tactics.
- Social: group rides with a leader and a keep-together mode, meetups, pacer bots at set watts, Ride Ons (thumbs-ups) from the companion app, seasonal events like Tour de Zwift.
- Ideas for our game: drafting plus short power-ups adds tactics; spendable currency brings players back; pacer bots fill solo rides; our water and fan power-ups fit the grab-then-trigger model.
- Zwift size: about 1M subscribers ([Zwift Insider](https://zwiftinsider.com/zwift-rouvy-more/)), revenue estimate about $103M (2023, [GetLatka](https://getlatka.com/companies/zwift)), $1B valuation in 2020. It bought Rouvy in 2026.

## 1. ROWER
==================================================================
### Status
Research round 1 complete.

### Findings
- No mainstream home rower separates left and right force: Concept2 RowErg/Dynamic/SkiErg, Hydrow, Ergatta, Aviron, WaterRower, Echelon Row, RP3, Merach R26. Each has one chain or strap into one flywheel.
- Concept2 PM5 data comes over three channels:
  - Concept2's own BLE service: peak and average drive force, stroke power, drive length and timing, and a full force curve per stroke (force curve not on the first-generation PM5).
    - https://www.concept2.nl/files/pdf/us/monitors/PM5_BluetoothSmartInterfaceDefinition.pdf
  - CSAFE over USB/RS485/BLE: CSAFE_PM_GET_FORCEPLOTDATA (0x6B) and CSAFE_PM_GET_STROKESTATS (0x6E).
    - https://cms.concept2.com/sites/default/files/2026-03/Concept2%20PM%20CSAFE%20Communication%20Definition.pdf
  - FTMS Rower Data on firmware above 150 (hardware v2+), without the force curve.
  - All three channels carry combined force only.
- Machines that measure each side:
  - Biorower S1pro: live left/right force curves in its app, no API found. https://biorower.com/s1pro/
    - The S1club takes an optional second side. https://biorower.com/compare/
  - Vasa kayak ergometer: per-side average and peak force and stroke length. https://vasatrainer.com/wp-content/uploads/2016/01/ERG_kayak_manual2015.pdf
  - Dansprint PRO: left/right power balance only. https://dansprint.com/vare/dansprint-pro-kayak-ergometer/
  - On water: Peach PowerLine and NK EmPower (two oarlocks). CSV export only.
- DIY split-force hack:
  - Split the Concept2 handle into two cords, one inline tension load cell per side.
  - HX711 amplifiers into an ESP32, sent over BLE to the bridge; the PM5 gives total power.
  - No published open-source build found.
  - Precedents: UW-Madison footplate load-cell project (https://bmedesign.engr.wisc.edu/public/projects/f23/UW_Crew/file/view/551a026b-a55b-4245-8633-aa5bc03909b3/Final%20Report%20-%20Rowing%20Biomechanics%20.pdf) and OpenRowingMonitor (https://github.com/JaapvanEkris/openrowingmonitor).
- Control: no resistance control found on air or water rowers. The game would use data only.

### Open questions
- Prices for Biorower S1pro/S1club, Vasa and Dansprint (not verified).
- Does Biorower have any export or API for live data?
- Which FTMS rower fields does the PM5 actually fill?
- Load cell sizing and handle-split design for a Concept2.

### Next steps
- Read PM5 data in real time with a small Python/BLE script.
- Prototype the two-load-cell handle on an ESP32.
- Decide whether left/right is needed in v1, or only total force plus Quest hand tracking for timing.

==================================================================
## 2. UPRIGHT BIKE (smart bikes, spin bikes, trainers)
==================================================================
### Status
Research round 1 complete (Oct 20, 2026).

### Buildable: confirmed FTMS data and control (ERG = target power, SIM = hill simulation)
- Wahoo KICKR Bike Shift, $1,999.99 (out of stock): https://www.wahoofitness.com/devices/indoor-cycling/smart-bikes/kickr-bike-shift-buy
  - Electromagnetic brake, ERG/SIM/resistance, ±1%. US company. Top premium pick.
- Wahoo KICKR Bike Pro, $3,999.99: https://www.wahoofitness.com/devices/indoor-cycling/smart-bikes/kickr-bike-pro-buy
  - Physical tilt, ±1%.
- Zwift Ride + KICKR Core 2, $1,299.99: https://us.zwift.com/products/zwift-ride-kickr-core-2
  - ±2%, standard FTMS through the Core. US company. Mid-range pick.
- Wattbike Atom, £2,199 (UK, out of stock; USD price not shown): https://wattbike.com/products/wattbike-atom
  - FTMS + FE-C, ±1%, fast response. With two Bluetooth connections, the second app is the controller.
- Tacx NEO Bike Plus, $3,599.99: https://www.garmin.com/en-US/p/734868/
- Technogym Ride, about $7,300 (from a search result; page not checked).
- Stages SB200, $2,399.99: https://stagescycling.com/en_us/stages-sb200-indoor-smart-bike
  - FTMS + Wi-Fi. Early ERG overshoot (DC Rainmaker, Oct 2026); Stages is adding smoothing.
- Stages SB20: discontinued, used about $875–1,200. ERG can take up to 30 s to settle.
- Direct-drive trainers (need a bike):
  - KICKR Core 2, $549.99
  - Elite Suito-T, $549
  - Elite Direto XR-T, $699
  - Saris H3, $649.99
  - Tacx NEO 3M, $1,799.99
  - Tacx Flux 2, $699.99
  - Budget path: about $700–800 total with a used road bike.
- Renpho AI Smart Bike: community-grade only, with erratic resistance reports.

### Not buildable (closed, proprietary or data-only)
- Peloton Bike/Bike+: closed; no native FTMS.
  - Workarounds: FitSwitch dongle ($139.99), Grupetto, SmartSpin2k.
  - Peloton's Terms of Service prohibit unauthorized apps, and updates can break hacks.
- NordicTrack S22i: $1,499, sold out. https://checkout.nordictrack.com/exercise-bikes/s22i-studio-bike-NTEX02121
  - iFIT only; QZ Companion is fragile, and auto-resistance is listed as "not yet".
- Echelon Connect EX-5: $799.99. https://echelonfit.com/products/echelon-connect-ex-5-bike-special-offer
  - Proprietary protocol; QZ bridges it to FTMS with auto-resistance (32 levels).
  - Cheapest motorized option, but it depends on a bridge app.
- Schwinn IC4: $899, data only (manual magnet). https://www.schwinnfitness.com/products/schwinn-ic4-indoor-cycling-bike
  - SmartSpin2k (~$240) adds real control: https://docs.smartspin2k.com/compatibility.html
- Bowflex C6 and VeloCore, Horizon 7.0 IC, Yesoul, ICG IC7: data only.
- Keiser M3i: data only. Merach: control only through QZ.
- Rogue Echo Bike V3.0: $945, air, data only, made in USA. https://www.roguefitness.com/rogue-echo-bike
- Concept2 BikeErg: $1,100, air, data only. https://www.concept2.com/ergs/bikeerg

### Premium smart bike comparison (Oct 20, 2026)
- Stages SB200, $2,399.99 + $150 shipping: https://stagescycling.com/en_us/stages-sb200-indoor-smart-bike
  - Hardware: electromagnetic brake, high-inertia belt drive, ±1% (about 1.5% in testing), 2,500 W, 71 lb, no physical tilt.
  - Connections: Bluetooth FTMS (4 at once, 1 controller), Wi-Fi at 10 Hz, OpenBikeControl buttons, no ANT+.
  - ERG reaches the target in 2–4 s, then wobbles. Smoothing firmware is promised; wait for a reviewer to validate it.
  - Made in Taiwan (Giant/AIPS). Stages has been owned by Giant since 2024.
- Wahoo KICKR Bike Shift, $1,999.99 (out of stock). TOP PICK for the game.
  - ERG settles in about 3 s, it's silent, and its buttons can be read over reverse-engineered BLE.
  - No physical tilt. Simulated grade +20/−15%.
  - Probably about 1 Hz over Bluetooth.
- Wahoo KICKR Bike Pro, $3,999.99.
  - Motorized tilt from −15% to +20% grade, following the simulated grade from any FTMS/FE-C app when unlocked.
  - Tilt doesn't move in ERG mode, and no official way exists to tilt independently of resistance.
  - Has Zwift Play-style button pads.
- Tacx NEO Bike Plus, $3,599.99.
  - Slowest ERG history and 110 lb. Skip.
- Latency for design:
  - Expect 1–3 s from command to feel. Smooth power on screen and send grade changes early.
  - On Quest standalone, plan on Bluetooth FTMS.
- US involvement, most to least:
  1. Wahoo (Atlanta HQ, made in Vietnam)
  2. Zwift (Long Beach software, Ride made in China)
  3. Stages (US brand, Giant/Taiwan)
  4. Spirit (Arkansas HQ, Dyaco Taiwan/China)
  5. Tacx (made in the Netherlands, Garmin parent)
  6. Wattbike (UK design, made in Taiwan)
- Button input sources:
  - BikeControl: https://github.com/OpenBikeControl/bikecontrol
  - KICKR reverse-engineered GATT notes: https://github-wiki-see.page/m/StephenDone/kickr_bluetooth/wiki/KICKR-GATT-Profile

### Buying notes (Oct 20, 2026)
- KICKR Bike Shift (model WFBIKE2):
  - Wahoo direct: $1,999.99, out of stock. There's a back-in-stock email signup and no restock date.
  - Bike shops: $2,549.99, several in stock:
    - Bicycle House: https://www.bicycle-house.com/kickr-bike-shift-indoor-bike-trainer.html
    - Red Rock Bicycle: https://www.redrockbicycle.com/products/wahoo-kickr-bike-shift-trainer
    - RA Cycles: https://racycles.com/products/wahoo-kickr-bike-shift-trainer
    - Landry's: https://www.landrys.com/categories/wahoo-smart-trainers
  - REI: about $2,550, stock unknown.
  - Certified reconditioned: $2,299.99 at Revolution Bike Shop.
  - Still listed as a "current" product; no replacement announced.
- Zwift Ride V2 + KICKR Core 2: $1,299.99, available, $75 shipping. https://us.zwift.com/products/zwift-ride-kickr-core-2
  - Complete bike (frame, seat, bars with controllers, pedals, trainer). No own bike needed, and no membership included.
  - V2 launched Sept 9, 2026: compact frame, five crank lengths, seat fore-aft adjustment, rider fit 146–198 cm, 120 kg max.
  - FTMS control comes through the Core 2.
  - Handlebar buttons use Zwift's own protocol; see BikeControl.
  - Frame only: $799.99.

### Subscriptions and custom software (Oct 20, 2026)
- No subscription or account is needed for a custom app (Unity or bridge) to read data and control resistance on the KICKR Core 2 over Bluetooth FTMS. The bike works without Zwift. Test pairing early, because the Core 2 added secure (bonded) Bluetooth pairing for EU compliance ([DC Rainmaker](https://www.dcrainmaker.com/2026/02/quick-heads-up-wahoo-kickr-core-2-sale-for-399-zwift-cog-or-cassette.html)).
- Gearing: the Zwift Cog is a single sprocket. Virtual shifting only works under Zwift or Rouvy, so in our game it rides as one fixed gear. ERG and set-resistance modes are fine. For SIM mode we'd do our own virtual shifting by changing the grade or power per gear, like BikeControl does ([BikeControl](https://bikecontrol.app/virtual-shifting)).
- Zwift Ride handlebar buttons use encrypted Zwift protocol. Controller firmware 1.3+ (Ride V2) needs a daily unlock (open Zwift and connect about every 24h); a free Zwift account is enough. Don't update the controller firmware ([BikeControl blog](https://bikecontrol.app/blog/zwift-click-v2-with-other-trainer-apps/)). Fallback: use Quest controllers for input.
- Firmware: Core 2 updates over Wi-Fi; the Wahoo app needs a free account. Ride firmware updates through Zwift Companion (sign-in, no subscription).
- Prices: Zwift $19.99/mo or $199.99/yr. Wahoo app Base is free, Core $4.99/mo, Pro (SYSTM) $17.99/mo. MyWhoosh is free. Wahoo's bundle listing says Zwift membership is sold separately.
- Tacx NEO bundles with Cog and Click: NEO 2T $1,099.99, NEO 3M $1,799.99, and the kit alone $49.99. The Click buttons have the same encrypted, daily-unlock limits.

- KICKR Direct Connect ($99.99, [Wahoo](https://www.wahoofitness.com/devices/indoor-cycling/accessories/kickr-dircon-buy)) is a wired Ethernet adapter. It is NOT compatible with the KICKR Core 2 (no port; the Core 2 has built-in Wi-Fi instead). It works only with KICKR v5/v6, MOVE, ROLLR, and Bike v2/Shift/Pro. Skip it for the Zwift Ride. Consider it only on a Bike Shift if Bluetooth drops, and note it would need its own network protocol in the bridge.

### Seated approximation
- Recumbent bikes and trikes can mount on a direct-drive trainer. Users report KICKR ERG works this way; add a crank cadence sensor.
- Or put a DIY backrest on a smart bike.

### Open questions
- Which FTMS control opcodes each device actually accepts.
  - Read the Fitness Machine Feature characteristic (0x2ACC) at connect time.
  - Set Target Resistance Level is the least reliably supported.
- No published latency figures.

### Next steps
- Choose between the KICKR Bike Shift (premium) and Zwift Ride + Core 2 (mid-range).
- Test ERG and resistance from pycycling, then wire the results into the Unity bridge.

==================================================================
## 3. RECUMBENT BIKE
==================================================================
### Status
Research round 1 complete. No recumbent has confirmed FTMS resistance control yet.

### Candidates
- Spirit XBR55 (best premium bet), $1,999.99: https://www.spiritfitness.com/residential/xbr55
  - Dealer prices from search listings:
    - FitnessZone, $1,699: https://www.fitnesszone.com/product/spirit-fitness-xbr55-recumbent-bike.html
    - Forza, $1,600: https://forzafitnessusa.com/product/spirit-fitness-xbr55-recumbent-bike/
  - FTMS on units made after Nov 2019, electronic brake with 20 levels.
  - Spirit says Kinomap auto-adjusts it: https://www.spiritfitness.com/kinomap
  - A Zwift owner reported hills didn't change resistance: https://forums.zwift.com/t/spirit-xbr55-smart-recumbent-bike-and-zwift/555700
  - Sends a truncated FTMS feature report.
  - Spirit is based in Jonesboro, AR, owned by Dyaco (Taiwan), and manufactures in Taiwan and China.
- Spirit XBR95, $2,299.99: https://www.spiritfitness.com/residential/xbr95
  - FTMS per manual, 40 levels, self-powered. Control unverified.
- Schwinn 290, $899: https://www.schwinnfitness.com/products/schwinn-290-recumbent-bike
  - Full FTMS, servo-motor brake.
  - Only the JRNY app is proven to control it; the QZ wiki says no.

### Excluded (data-only or not recumbent)
- Keiser M3/M3i: upright, manual lever.
- Schwinn 230: partial FTMS, no control.
- Sole R92 and LCR: no auto-resistance, per Sole.
- Horizon 5.0R: no ERG, per Horizon.
- Merach: manual magnet.
- Spirit XBR25: no Bluetooth.

### Fallbacks with guaranteed control (serial, need a PC bridge)
- Lode Corival Recumbent: published RS232, set power in 1 W steps. Quote only. https://lode-ergometry.com/product/corival-recumbent-rehab/
- Used Kettler recumbents with a PC port.
  - Serial PW command.
  - kettlerUSB2BLE: https://github.com/360manu/kettlerUSB2BLE
- ergoline ergoselect 600 (spec on request).
- SCIFIT ISO7000R (CSAFE port, control unverified).

### Open questions
- Which FTMS control opcodes does the XBR55 accept: set resistance (0x04), target power (0x05) or simulation (0x11)?
- Does the Schwinn 290 expose the FTMS control point (0x2AD9)?
- Is there any budget recumbent with control?

### Next steps
- Buy the XBR55 from a dealer with a return window.
- Day-one test in nRF Connect:
  1. Read 0x2ACC (feature bits) and 0x2AD6 (resistance range).
  2. On 0x2AD9, write 0x00 (request control), then 0x04 plus a level.
  3. Feel whether the brake changes.
- If control fails, return it, or use it data-only and pair it with a serial-controlled ergometer.
