"""Zwift Play / Zwift Ride controller listener (BLE, bleak).

Connects to every Zwift controller advertising Zwift's manufacturer ID (0x094A),
does the plain "RideOn" handshake (unencrypted mode), subscribes to the
async (notify) + sync TX (indicate) characteristics, decodes button messages and
logs them to stdout + events.log, and forwards them as UDP JSON to 127.0.0.1:5006
(the Unity game's ZwiftRideUdpReceiver), e.g.
    {"kind": "press", "btn": "A", "side": "right", "unit": "LEFT", "pressed": true, ...}
"side" is the PHYSICAL handlebar side of the button; "unit" is the controller unit that
reported it (the Ride's left unit relays the right unit's buttons, so one press can arrive
from both units ~4 ms apart; the game treats a button as held while any unit holds it).
Target hardware: Zwift Ride (handlebar controllers). Zwift Play also works.
Ride firmware >= 1.3 needs the daily Zwift app unlock (connect the bike to the free Zwift
app about once every 24 hours) or the buttons stop responding.

Never connects to anything without Zwift manufacturer data (company 0x094A).
"""
import argparse
import asyncio
import json
import os
import socket
import sys
import time
from datetime import datetime

try:
    from bleak import BleakClient, BleakScanner
except ImportError:  # unit tests of the pure helpers don't need bleak
    BleakClient = BleakScanner = None

ZWIFT_CID = 0x094A
SVC_RC1 = "00000001-19ca-4651-86e5-fa29dcdd09d1"   # Zwift Play / Click v1 service
SVC_RIDE = "0000fc82-0000-1000-8000-00805f9b34fb"  # Zwift Ride / newer firmware service
CH_ASYNC = "00000002-19ca-4651-86e5-fa29dcdd09d1"   # notify: button/battery/idle
CH_SYNC_RX = "00000003-19ca-4651-86e5-fa29dcdd09d1" # write: commands to device
CH_SYNC_TX = "00000004-19ca-4651-86e5-fa29dcdd09d1" # indicate/read: command responses
CH_BATT = "00002a19-0000-1000-8000-00805f9b34fb"
CH_FW = "00002a26-0000-1000-8000-00805f9b34fb"

RIDE_ON = b"RideOn"
DEV_TYPES = {0x02: ("Play", "RIGHT"), 0x03: ("Play", "LEFT"), 0x09: ("Click", "-"),
             0x0A: ("ClickV2", "RIGHT"), 0x0B: ("ClickV2", "LEFT"),
             0x07: ("Ride", "RIGHT"), 0x08: ("Ride", "LEFT"), 0x0E: ("PlayFw2", "?")}

# Zwift Ride (and Play fw2 / Click v2) CONTROLLER_NOTIFICATION (opcode 0x23):
# protobuf field 1 = ButtonMap bitmask, bit CLEARED (0) = pressed.
RIDE_BUTTONS = [
    (0x00001, "DPAD_LEFT"), (0x00002, "DPAD_UP"), (0x00004, "DPAD_RIGHT"), (0x00008, "DPAD_DOWN"),
    (0x00010, "A"), (0x00020, "B"), (0x00040, "Y"), (0x00080, "Z"),
    (0x00100, "LEFT_SHIFT_UP"), (0x00200, "LEFT_SHIFT_DOWN"),
    (0x00400, "LEFT_POWERUP"), (0x00800, "LEFT_ON_OFF"),
    (0x01000, "RIGHT_SHIFT_UP"), (0x02000, "RIGHT_SHIFT_DOWN"),
    (0x04000, "RIGHT_POWERUP"), (0x08000, "RIGHT_ON_OFF"),
]
# field 3 = repeated {1: location enum, 2: sint32 analog value (-100..100)}
ANALOG_LOC = {0: "LEFT_PADDLE", 1: "RIGHT_PADDLE", 2: "ANALOG_UP", 3: "ANALOG_DOWN"}
ANALOG_THRESHOLD = 25

OPCODES = {0x15: "IDLE", 0x19: "BATTERY_NOTIF", 0x1A: "BATTERY_STATUS", 0x23: "CONTROLLER_NOTIFICATION",
           0x07: "PLAY_KEYPAD_STATUS", 0x37: "CLICK_NOTIFICATION", 0x2A: "LOG_DATA", 0x3C: "GET_RESPONSE",
           0x3E: "STATUS_RESPONSE", 0x40: "SET_RESPONSE", 0x52: "RIDE_ON", 0xFE: "LOST_CONTROL",
           0xFF: "VENDOR_MESSAGE"}

RIGHT_BUTTONS = {"A", "B", "Y", "Z"}


def physical_side(button):
    """Handlebar side a button is on: A/B/Y/Z and RIGHT_* = right; DPAD_* and LEFT_* = left; else ''."""
    b = (button or "").upper()
    if b in RIGHT_BUTTONS or b.startswith("RIGHT"):
        return "right"
    if b.startswith("DPAD") or b.startswith("LEFT"):
        return "left"
    return ""


def button_event(kind, button, unit, address="", device="", **kw):
    """The UDP JSON dict for a press/release: btn + physical side + pressed, plus which unit sent it."""
    return dict(kind=kind, btn=button, button=button, side=physical_side(button), unit=unit,
                pressed=(kind == "press"), address=address, device=device, **kw)


HERE = os.path.dirname(os.path.abspath(__file__))
LOG_PATH = os.path.join(HERE, "events.log")
_udp = None
_udp_addr = None


def ts():
    return datetime.now().isoformat(timespec="milliseconds")


def log(line, event=None):
    out = f"{ts()} {line}"
    print(out, flush=True)
    with open(LOG_PATH, "a", encoding="utf-8") as f:
        f.write(out + "\n")
    if event is not None and _udp is not None:
        try:
            event = dict(event, t=time.time())
            _udp.sendto(json.dumps(event).encode(), _udp_addr)
        except OSError:
            pass


# ---------- minimal protobuf decoding ----------
def read_varint(b, i):
    shift = val = 0
    while True:
        c = b[i]; i += 1
        val |= (c & 0x7F) << shift
        shift += 7
        if not c & 0x80:
            return val, i


def zigzag(n):
    return (n >> 1) ^ -(n & 1)


def pb_fields(b):
    """Return list of (field_no, wire_type, value). value = int or bytes."""
    out, i = [], 0
    while i < len(b):
        key, i = read_varint(b, i)
        f, wt = key >> 3, key & 7
        if wt == 0:
            v, i = read_varint(b, i)
        elif wt == 2:
            ln, i = read_varint(b, i)
            v = bytes(b[i:i + ln]); i += ln
        elif wt == 5:
            v = int.from_bytes(b[i:i + 4], "little"); i += 4
        elif wt == 1:
            v = int.from_bytes(b[i:i + 8], "little"); i += 8
        else:
            raise ValueError(f"bad wire type {wt} at {i}")
        out.append((f, wt, v))
    return out


def decode_ride(payload):
    pressed, analog = set(), {}
    bmap = None
    for f, wt, v in pb_fields(payload):
        if f == 1 and wt == 0:
            bmap = v
        elif f == 3 and wt == 2:
            loc, val = 0, 0
            for f2, wt2, v2 in pb_fields(v):
                if f2 == 1: loc = v2
                elif f2 == 2: val = zigzag(v2)
            name = ANALOG_LOC.get(loc, f"ANALOG_{loc}")
            analog[name] = val
            if abs(val) >= ANALOG_THRESHOLD:
                pressed.add(name)
    if bmap is not None:
        for mask, name in RIDE_BUTTONS:
            if bmap & mask == 0:
                pressed.add(name)
    return pressed, analog, bmap


PLAY_FIELDS = {2: ("Y", "DPAD_UP"), 3: ("Z", "DPAD_LEFT"), 4: ("A", "DPAD_RIGHT"), 5: ("B", "DPAD_DOWN"),
               6: ("RIGHT_ON_OFF", "LEFT_ON_OFF"), 7: ("RIGHT_SHIFT", "LEFT_SHIFT")}


def decode_play(payload):
    """Zwift Play (RC1) opcode 0x07: field1 0=right pad 1=left pad; buttons 0=pressed."""
    fields = {f: v for f, wt, v in pb_fields(payload)}
    right = fields.get(1, 0) == 0
    pressed, analog = set(), {}
    for f, names in PLAY_FIELDS.items():
        if fields.get(f, 1) == 0:
            pressed.add(names[0] if right else names[1])
    lr = zigzag(fields.get(8, 0)); brake = fields.get(9, 0)
    side = "RIGHT" if right else "LEFT"
    analog[f"{side}_PADDLE_LR"] = lr; analog[f"{side}_PADDLE_BRAKE"] = brake
    if abs(lr) >= ANALOG_THRESHOLD: pressed.add(f"{side}_PADDLE")
    if brake >= ANALOG_THRESHOLD: pressed.add(f"{side}_PADDLE_BRAKE")
    return pressed, analog, None


class Controller:
    def __init__(self, address, name, dev_type, args):
        self.address, self.name, self.dev_type, self.args = address, name, dev_type, args
        fam, side = DEV_TYPES.get(dev_type, ("Unknown", "?"))
        self.family, self.side = fam, side
        self.tag = f"[{side:5s} {address}]"
        self.client = None
        self.pressed = set()
        self.last_rx = 0.0
        self.idle_count = 0
        self.handshake_ok = False
        self.last_batt, self.last_batt_log = None, 0.0
        self.disconnected = asyncio.Event()

    def ev(self, kind, **kw):
        return dict(kind=kind, side=self.side, unit=self.side, address=self.address, device=self.family, **kw)

    def button_ev(self, kind, button, **kw):
        return button_event(kind, button, self.side, self.address, self.family, **kw)

    def on_disconnect(self, _client):
        log(f"{self.tag} DISCONNECTED", self.ev("disconnected"))
        self.disconnected.set()

    def handle(self, char_name, data: bytearray):
        self.last_rx = time.time()
        raw = data.hex(" ")
        if not data:
            return
        if bytes(data).startswith(RIDE_ON):
            self.handshake_ok = True
            log(f"{self.tag} HANDSHAKE_RESPONSE via {char_name}: {data!r} hex={raw}",
                self.ev("handshake", raw=raw))
            return
        op = data[0]
        body = bytes(data[1:])
        if op == 0x15:
            self.idle_count += 1
            if self.args.log_idle:
                log(f"{self.tag} IDLE raw={raw}")
            return
        if op in (0x23, 0x07, 0x37):
            try:
                if op == 0x07 and self.family == "Play":
                    pressed, analog, bmap = decode_play(body)
                else:
                    pressed, analog, bmap = decode_ride(body)
            except Exception as e:  # noqa
                log(f"{self.tag} DECODE_ERROR op=0x{op:02x} {e} raw={raw}", self.ev("decode_error", raw=raw))
                return
            downs = sorted(pressed - self.pressed)
            ups = sorted(self.pressed - pressed)
            self.pressed = pressed
            bm = f" map=0x{bmap:05x}" if bmap is not None else ""
            an = " analog=" + ",".join(f"{k}:{v}" for k, v in analog.items() if v) if any(analog.values()) else ""
            for b in downs:
                log(f"{self.tag} PRESS   {b}{bm}{an} raw={raw}", self.button_ev("press", b, raw=raw, analog=analog))
            for b in ups:
                log(f"{self.tag} RELEASE {b}{bm} raw={raw}", self.button_ev("release", b, raw=raw))
            if not downs and not ups and (self.args.log_repeats or (analog and any(analog.values()))):
                log(f"{self.tag} STATE   held={sorted(pressed)}{bm}{an} raw={raw}",
                    self.ev("state", held=sorted(pressed), raw=raw, analog=analog))
            return
        if op == 0x19:
            pct = None
            try:
                for f, wt, v in pb_fields(body):
                    if f == 2: pct = v
            except Exception:
                pass
            if pct is None and len(body) >= 2:
                pct = body[1]
            now = time.time()
            if pct != self.last_batt or now - self.last_batt_log >= 300:
                self.last_batt, self.last_batt_log = pct, now
                log(f"{self.tag} BATTERY {pct}% raw={raw}", self.ev("battery", percent=pct, raw=raw))
            return
        name = OPCODES.get(op, "UNKNOWN")
        log(f"{self.tag} MSG {name} (0x{op:02x}) via {char_name} raw={raw}", self.ev("message", op=op, name=name, raw=raw))

    async def send(self, payload: bytes):
        ch = self.client.services.get_characteristic(CH_SYNC_RX)
        wnr = "write-without-response" in ch.properties
        await self.client.write_gatt_char(ch, payload, response=not wnr)
        log(f"{self.tag} SENT -> sync_rx ({'no-rsp' if wnr else 'with-rsp'}): {payload.hex(' ')}")

    async def run_once(self):
        dev = await BleakScanner.find_device_by_address(self.address, timeout=15.0)
        if dev is None:
            log(f"{self.tag} not found while scanning (press a button to wake it)")
            return
        self.disconnected.clear()
        self.pressed = set()
        self.handshake_ok = False
        async with BleakClient(dev, disconnected_callback=self.on_disconnect, timeout=20.0) as client:
            self.client = client
            svcs = {s.uuid.lower(): s for s in client.services}
            log(f"{self.tag} CONNECTED name={self.name!r} type=0x{self.dev_type:02x} ({self.family} {self.side}) "
                f"services={sorted(svcs)}", self.ev("connected"))
            fw = None
            try:
                fw = (await client.read_gatt_char(CH_FW)).decode(errors="replace").strip("\x00")
                log(f"{self.tag} firmware={fw}")
            except Exception as e:
                log(f"{self.tag} firmware read failed: {e}")
            try:
                b = await client.read_gatt_char(CH_BATT)
                log(f"{self.tag} BATTERY {b[0]}% (0x2A19 read)", self.ev("battery", percent=b[0]))
            except Exception as e:
                log(f"{self.tag} battery read failed: {e}")
            if SVC_RIDE not in svcs and SVC_RC1 not in svcs:
                log(f"{self.tag} ERROR: Zwift custom service hidden/missing. On Zwift Ride fw>=1.3 (Ride V2) this means "
                    f"it is LOCKED until unlocked via the Zwift app (daily).", self.ev("error", msg="service hidden"))
                await asyncio.sleep(5)
                return
            try:
                r = await client.read_gatt_char(CH_SYNC_TX)
                log(f"{self.tag} sync_tx initial read: {bytes(r)!r} hex={bytes(r).hex(' ')}")
            except Exception as e:
                log(f"{self.tag} sync_tx read failed: {e}")
            await client.start_notify(CH_ASYNC, lambda _c, d: self.handle("async", d))
            await client.start_notify(CH_SYNC_TX, lambda _c, d: self.handle("sync_tx", d))
            await self.send(RIDE_ON)
            # Ride V2 (fw >= 1.3) gated handshake as done by BikeControl: RideOn then FF 04 00
            if self.family == "Ride" and fw:
                try:
                    parts = [int(x) for x in fw.split(".")[:2]]
                    if parts >= [1, 3]:
                        log(f"{self.tag} firmware {fw} >= 1.3 => Ride V2: needs daily Zwift unlock; sending FF 04 00")
                        await self.send(bytes([0xFF, 0x04, 0x00]))
                except ValueError:
                    pass
            t0 = time.time()
            last_idle_report = time.time()
            while not self.disconnected.is_set():
                await asyncio.sleep(1)
                now = time.time()
                if not self.handshake_ok and now - t0 > 5 and now - t0 < 7:
                    log(f"{self.tag} WARNING: no RideOn response after 5 s")
                if now - last_idle_report >= self.args.idle_report:
                    log(f"{self.tag} alive: {self.idle_count} idle(0x15) msgs in last {int(now - last_idle_report)} s")
                    self.idle_count = 0
                    last_idle_report = now
                # keepalive: references show none required; if the device goes silent, re-send RideOn
                if self.last_rx and now - self.last_rx > self.args.silence_rehandshake:
                    log(f"{self.tag} silent for {int(now - self.last_rx)} s -> re-sending RideOn")
                    self.last_rx = now
                    try:
                        await self.send(RIDE_ON)
                    except Exception as e:
                        log(f"{self.tag} resend failed: {e}")
                        break

    async def run_forever(self):
        while True:
            try:
                await self.run_once()
            except asyncio.CancelledError:
                if self.client is not None and self.client.is_connected:
                    await self.client.disconnect()
                raise
            except Exception as e:
                log(f"{self.tag} ERROR {type(e).__name__}: {e}", self.ev("error", msg=str(e)))
            log(f"{self.tag} reconnecting in 3 s")
            await asyncio.sleep(3)


async def discover(timeout):
    found = {}

    def cb(d, adv):
        md = adv.manufacturer_data or {}
        if ZWIFT_CID in md and md[ZWIFT_CID]:
            found[d.address] = (adv.local_name or d.name or "", md[ZWIFT_CID][0], adv.rssi, bytes(md[ZWIFT_CID]).hex())

    async with BleakScanner(detection_callback=cb):
        await asyncio.sleep(timeout)
    return found


async def main():
    global _udp, _udp_addr
    ap = argparse.ArgumentParser()
    ap.add_argument("--duration", type=float, default=1800, help="seconds to run, then exit")
    ap.add_argument("--scan", type=float, default=10, help="discovery scan seconds")
    ap.add_argument("--address", action="append", help="only these addresses (repeatable)")
    ap.add_argument("--left-only", action="store_true", help="Ride: connect only the left unit (BikeControl style)")
    ap.add_argument("--udp-port", type=int, default=5006, help="UDP JSON target port on 127.0.0.1 (never 5005)")
    ap.add_argument("--no-udp", action="store_true")
    ap.add_argument("--log-idle", action="store_true")
    ap.add_argument("--log-repeats", action="store_true")
    ap.add_argument("--idle-report", type=float, default=60)
    ap.add_argument("--silence-rehandshake", type=float, default=15)
    args = ap.parse_args()
    if BleakClient is None:
        sys.exit("bleak is not installed: py -m pip install bleak")
    if args.udp_port == 5005:
        sys.exit("refusing UDP port 5005 (KICKR bridge)")
    if not args.no_udp:
        _udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)  # send-only, never binds a fixed port
        _udp_addr = ("127.0.0.1", args.udp_port)

    log(f"=== zwift_play_listen start pid={os.getpid()} duration={args.duration}s udp={'off' if args.no_udp else _udp_addr} ===")
    found = {}
    for attempt in range(2):
        found = await discover(args.scan)
        if found:
            break
        log("no Zwift controllers found; press a button to wake them... retrying")
    for a, (n, t, r, md) in found.items():
        log(f"found {a} name={n!r} type=0x{t:02x} {DEV_TYPES.get(t)} rssi={r} mfr={md}")
    ctrls = []
    for a, (n, t, r, md) in found.items():
        if args.address and a.upper() not in [x.upper() for x in args.address]:
            continue
        if args.left_only and t == 0x07:
            continue
        ctrls.append(Controller(a, n, t, args))
    if not ctrls:
        log("no controllers to connect; exiting")
        return
    tasks = []
    for c in ctrls:
        tasks.append(asyncio.create_task(c.run_forever()))
        await asyncio.sleep(2)  # stagger connects on the Windows BLE stack
    try:
        await asyncio.wait_for(asyncio.gather(*tasks), timeout=args.duration)
    except asyncio.TimeoutError:
        pass
    finally:
        for t in tasks:
            t.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
        log("=== zwift_play_listen stop ===")


if __name__ == "__main__":
    asyncio.run(main())
