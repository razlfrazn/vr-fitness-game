"""Unit tests for the pure parts of zwift_play_listen.py (no Bluetooth, no hardware).

    py -m unittest test_zwift_play_listen -v
"""
import json
import os
import socket
import sys
import types
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zwift_play_listen as z  # noqa: E402

# Sample Zwift Ride CONTROLLER_NOTIFICATION from events.log (A pressed: bit 0x10 cleared).
A_PRESS = bytes.fromhex("23 08 ef ff ff ff 0f 1a 04 08 00 10 00".replace(" ", ""))
ALL_UP = bytes.fromhex("23 08 ff ff ff ff 0f".replace(" ", ""))


class Args:
    log_idle = False
    log_repeats = False


class FakeUdp:
    def __init__(self):
        self.sent = []

    def sendto(self, data, addr):
        self.sent.append((json.loads(data.decode()), addr))


class ListenerTests(unittest.TestCase):
    def setUp(self):
        self._log_path = z.LOG_PATH
        z.LOG_PATH = os.devnull
        self.udp = FakeUdp()
        z._udp, z._udp_addr = self.udp, ("127.0.0.1", 5006)

    def tearDown(self):
        z.LOG_PATH = self._log_path
        z._udp = z._udp_addr = None

    def test_decode_ride_a(self):
        pressed, analog, bmap = z.decode_ride(A_PRESS[1:])
        self.assertEqual(pressed, {"A"})
        self.assertEqual(bmap & 0x10, 0)

    def test_physical_side(self):
        for b in ("A", "B", "Y", "Z", "RIGHT_SHIFT_UP", "RIGHT_POWERUP"):
            self.assertEqual(z.physical_side(b), "right", b)
        for b in ("DPAD_UP", "DPAD_LEFT", "LEFT_SHIFT_DOWN", "LEFT_POWERUP"):
            self.assertEqual(z.physical_side(b), "left", b)
        self.assertEqual(z.physical_side("ANALOG_UP"), "")

    def test_button_event_shape(self):
        e = z.button_event("press", "A", "LEFT", "CC:A3:D1:BA:46:D6", "Ride")
        self.assertEqual(e["btn"], "A")
        self.assertEqual(e["side"], "right")
        self.assertEqual(e["unit"], "LEFT")
        self.assertIs(e["pressed"], True)
        self.assertIs(z.button_event("release", "A", "RIGHT")["pressed"], False)

    def _controller(self, dev_type):
        c = z.Controller.__new__(z.Controller)
        c.address, c.name, c.dev_type, c.args = "AA:BB", "Zwift Ride", dev_type, Args()
        c.family, c.side = z.DEV_TYPES[dev_type]
        c.tag = "[test]"
        c.pressed, c.last_rx, c.idle_count, c.handshake_ok = set(), 0.0, 0, False
        c.last_batt, c.last_batt_log = None, 0.0
        return c

    def test_relayed_a_press_and_release_forwarded_as_udp_json(self):
        left = self._controller(0x08)    # left unit relays the right unit's A
        left.handle("async", bytearray(A_PRESS))
        left.handle("async", bytearray(A_PRESS))   # repeat: no second press
        left.handle("async", bytearray(ALL_UP))
        events = [e for e, _ in self.udp.sent]
        self.assertEqual([(e["kind"], e["btn"], e["side"], e["unit"], e["pressed"]) for e in events],
                         [("press", "A", "right", "LEFT", True), ("release", "A", "right", "LEFT", False)])
        self.assertTrue(all(addr == ("127.0.0.1", 5006) for _, addr in self.udp.sent))

    def test_play_right_pad_a_is_right(self):
        # Zwift Play opcode 0x07: field1=0 (right pad), field4 (A)=0 pressed, others 1.
        body = bytes([0x08, 0x00, 0x10, 0x01, 0x18, 0x01, 0x20, 0x00, 0x28, 0x01])
        pressed, _, _ = z.decode_play(body)
        self.assertIn("A", pressed)
        self.assertEqual(z.physical_side("A"), "right")

    # --- button map overlay: codes, simultaneous presses, unknown bits ---

    def test_every_ride_button_has_a_code(self):
        self.assertEqual(len(z.RIDE_BUTTONS), 16)
        self.assertEqual(z.RIDE_KNOWN_MASK, 0xFFFF)
        self.assertEqual(z.button_code("A"), "0x00010")
        self.assertEqual(z.button_code("RIGHT_ON_OFF"), "0x08000")
        self.assertEqual(z.button_code("LEFT_PADDLE"), "analog 0")
        self.assertEqual(z.button_code("UNKNOWN_0x10000"), "0x10000")
        self.assertEqual(z.button_code("ANALOG_7"), "analog 7")
        self.assertEqual(z.button_event("press", "A", "LEFT")["code"], "0x00010")

    def test_simultaneous_presses_in_one_bitmap(self):
        bmap = 0xFFFFFFFF & ~0x10 & ~0x02 & ~0x1000   # A + DPAD_UP + RIGHT_SHIFT_UP
        pressed, _, _ = z.decode_ride(b"\x08" + varint(bmap))
        self.assertEqual(pressed, {"A", "DPAD_UP", "RIGHT_SHIFT_UP"})

    def test_unknown_bit_is_forwarded_raw(self):
        bmap = 0xFFFFFFFF & ~0x10000                  # a bit outside the 16 known buttons
        pressed, _, _ = z.decode_ride(b"\x08" + varint(bmap))
        self.assertEqual(pressed, {"UNKNOWN_0x10000"})
        left = self._controller(0x08)
        left.handle("async", bytearray(b"\x23\x08" + varint(bmap)))
        left.handle("async", bytearray(ALL_UP))
        events = [e for e, _ in self.udp.sent]
        self.assertEqual([(e["kind"], e["btn"], e["code"], e["side"]) for e in events],
                         [("press", "UNKNOWN_0x10000", "0x10000", ""), ("release", "UNKNOWN_0x10000", "0x10000", "")])
        self.assertTrue(events[0]["raw"].startswith("23 08"))

    def test_short_bitmap_has_no_phantom_unknowns(self):
        pressed, _, _ = z.decode_ride(b"\x08" + varint(0xFFFF))   # only 16 bits sent, none cleared
        self.assertEqual(pressed, set())

    def test_unknown_analog_location_and_paddle(self):
        # field 3 entries: {loc 0, value 50 (zigzag 100)} and {loc 7, value -30 (zigzag 59)}
        body = b"\x08" + varint(0xFFFFFFFF) + b"\x1a\x04\x08\x00\x10" + varint(100) + b"\x1a\x04\x08\x07\x10" + varint(59)
        pressed, analog, _ = z.decode_ride(body)
        self.assertEqual(pressed, {"LEFT_PADDLE", "ANALOG_7"})
        self.assertEqual(analog, {"LEFT_PADDLE": 50, "ANALOG_7": -30})

    def test_unknown_opcode_forwarded_as_message_with_raw(self):
        left = self._controller(0x08)
        left.handle("async", bytearray(b"\x77\x01\x02"))
        e = self.udp.sent[-1][0]
        self.assertEqual((e["kind"], e["name"], e["op"], e["raw"]), ("message", "UNKNOWN", 0x77, "77 01 02"))



def varint(v):
    out = bytearray()
    while True:
        b = v & 0x7F
        v >>= 7
        if v:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


if __name__ == "__main__":
    unittest.main()
