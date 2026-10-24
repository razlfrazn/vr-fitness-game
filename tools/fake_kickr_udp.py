#!/usr/bin/env python3
"""
Fake KICKR bridge: sends the same JSON that kickr-bridge/kickr_bridge.py sends, one object per
UDP datagram, so you can test the Unity scene without the trainer.

    python tools/fake_kickr_udp.py                  # ride-like data, 4 Hz, until Ctrl+C
    python tools/fake_kickr_udp.py --duration 10    # stop after 10 seconds
    python tools/fake_kickr_udp.py --mode handturn  # like spinning the flywheel by hand:
                                                    # speed changes, cadence/power stay 0
    python tools/fake_kickr_udp.py --port 5005 --rate 4

Keys: t, speed_kmh, cadence_rpm, power_w, distance_m, elapsed_s (same names as the bridge).
Only uses the Python standard library. It never talks to the trainer.
"""
import argparse, json, math, random, socket, time


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5005)
    ap.add_argument("--rate", type=float, default=4.0, help="packets per second (the KICKR sends ~4)")
    ap.add_argument("--duration", type=float, default=0, help="seconds to run, 0 = forever")
    ap.add_argument("--mode", choices=["ride", "handturn", "constant"], default="ride")
    ap.add_argument("--speed", type=float, default=25.0, help="km/h for --mode constant")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    start = time.time()
    distance = 0.0
    period = 1.0 / args.rate
    sent = 0
    print(f"Sending fake KICKR data to {target[0]}:{target[1]} at {args.rate} Hz (mode={args.mode}). Ctrl+C to stop.")
    try:
        while True:
            now = time.time()
            el = now - start
            if args.duration and el >= args.duration:
                break
            if args.mode == "constant":
                speed, cadence, power = args.speed, args.speed * 3.4, 6.0 * args.speed
            elif args.mode == "handturn":
                # A hand spin every ~6 s that coasts down; the trainer reports 0 rpm / 0 W.
                phase = el % 6.0
                speed = max(0.0, 22.0 * math.exp(-phase / 1.8) - 0.5)
                cadence, power = 0.0, 0
            else:
                # Gentle intervals: 20-32 km/h, cadence ~85-95 rpm, power roughly follows speed.
                speed = 26.0 + 6.0 * math.sin(el / 8.0) + random.uniform(-0.4, 0.4)
                cadence = 90.0 + 5.0 * math.sin(el / 5.0) + random.uniform(-1.5, 1.5)
                power = 2.2 * speed ** 1.45 + random.uniform(-8, 8)
            distance += speed / 3.6 * period
            row = {
                "t": round(now, 3),
                "speed_kmh": round(speed, 2),
                "cadence_rpm": round(cadence * 2) / 2,     # FTMS resolution is 0.5 rpm
                "power_w": int(round(power)),
                "distance_m": int(distance),
                "elapsed_s": int(el),
            }
            sock.sendto(json.dumps(row).encode(), target)
            sent += 1
            if not args.quiet:
                print(row)
            time.sleep(max(0.0, period - (time.time() - now)))
    except KeyboardInterrupt:
        pass
    print(f"Sent {sent} packets.")


if __name__ == "__main__":
    main()
