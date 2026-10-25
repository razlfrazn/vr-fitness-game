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


if __name__ == "__main__":
    unittest.main()
