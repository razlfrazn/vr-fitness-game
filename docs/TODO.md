# VR Exercise Game Build: To-Do List
Recumbent bike + Meta Quest 3 + PC

## Hardware and connection
- [ ] 1. Unbox and set up the recumbent bike, then plug it in.
- [ ] 2. Make sure the PC's Bluetooth is turned on.
- [ ] 3. Download a free Bluetooth device scanner (for example nRF Connect or Bluetooth LE Explorer) and confirm it detects the bike.
- [ ] 4. Pair the bike to the PC over Bluetooth and verify real-time data (cadence, power, resistance) is coming through.

## Unity and Quest setup
- [ ] 5. Install Unity and create a new VR project from the Quest headset template.
- [ ] 6. Connect the Quest 3 to the PC (Wi-Fi via Air Link, or a USB-C cable via Quest Link) and confirm the VR camera rig works in a default scene.

## Bike-to-game bridge
- [ ] 7. Build or get a small bridge program that reads the bike's Bluetooth data (cadence to speed, resistance to drag) and sends it to Unity over a socket or named pipe. Test it with a rolling cube.
- [ ] 8. Send resistance control back through the same bridge so the game can change the bike's difficulty.

## Game content
- [ ] 9. Build the first track: model a road, trees, a swamp and a hill. Import AI-generated assets if you're using them.
- [ ] 10. Place pickups (glowing orbs) along the track that trigger power-ups on collision, with steering from the Quest controllers.
- [ ] 11. Script the chaser enemy so it speeds up when cadence drops and falls back when you boost.
- [ ] 12. Mount or hold the Quest 3 controllers for steering, and feed that input into the same data pipe.

## Playtest
- [ ] 13. Playtest the full loop: ride, steer, get chased, grab boosts and feel resistance change with the terrain.

## Notes
- Open-source FTMS (the standard Bluetooth profile for fitness machines) projects can shortcut the bridge in step 7. Examples are pycycling (https://github.com/zacharyedwardbull/pycycling) for Python reading and control, and QZ / qdomyos-zwift (https://github.com/cagnulein/qdomyos-zwift).
- Heart rate can be added later from a chest strap or watch through the standard Bluetooth Heart Rate Service.
- Before relying on step 8, check that your bike accepts FTMS resistance commands. Many recumbents only send data.
