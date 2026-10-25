using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Kickr
{
    /// <summary>One datagram from zwift_play_listen.py (field names match its JSON keys).</summary>
    [Serializable]
    public class ZwiftButtonPacket
    {
        public string kind;      // "press", "release", "connected", "disconnected", "battery", ...
        public string btn;       // button name, e.g. "A" (new listener)
        public string button;    // same, older listener versions
        public string side;      // physical side of the button: "right" / "left" (older listener: unit side)
        public string unit;      // which controller unit sent it: "RIGHT" / "LEFT"
        public bool pressed;
        public double t;
    }

    /// <summary>A parsed button change.</summary>
    public struct ZwiftButtonEvent
    {
        public string Button;    // upper case, e.g. "A"
        public string Side;      // lower case physical side: "right", "left" or ""
        public string Unit;      // upper case unit that reported it: "RIGHT", "LEFT" or ""
        public bool Pressed;
    }

    /// <summary>Parses the listener's UDP JSON. Plain C#.</summary>
    public static class ZwiftButtonMessage
    {
        /// <summary>
        /// Physical side of a Zwift Ride / Play button by name: A, B, Y, Z and RIGHT_* are on the
        /// right handlebar; DPAD_* and LEFT_* on the left. Others (e.g. ANALOG_UP) return "".
        /// </summary>
        public static string PhysicalSide(string button)
        {
            if (string.IsNullOrEmpty(button)) return "";
            string b = button.ToUpperInvariant();
            if (b == "A" || b == "B" || b == "Y" || b == "Z" || b.StartsWith("RIGHT", StringComparison.Ordinal)) return "right";
            if (b.StartsWith("DPAD", StringComparison.Ordinal) || b.StartsWith("LEFT", StringComparison.Ordinal)) return "left";
            return "";
        }

        public static bool TryParse(string json, out ZwiftButtonEvent ev) => TryParse(json, out ev, out _);

        /// <summary>
        /// True for a button press/release. Accepts {"btn":"A","side":"right","pressed":true} and the
        /// listener's full form {"kind":"press","btn":"A","button":"A","side":"right","unit":"LEFT","pressed":true,...}.
        /// Without "pressed", kind press/release decides. Without "unit" (older listener), "side" was the
        /// unit, so the unit is taken from it and the side from the button name.
        /// </summary>
        public static bool TryParse(string json, out ZwiftButtonEvent ev, out ZwiftButtonPacket packet)
        {
            ev = default;
            packet = null;
            if (string.IsNullOrEmpty(json)) return false;
            try { packet = JsonUtility.FromJson<ZwiftButtonPacket>(json); }
            catch (Exception) { packet = null; return false; }
            if (packet == null) return false;

            string name = !string.IsNullOrEmpty(packet.btn) ? packet.btn : packet.button;
            if (string.IsNullOrEmpty(name)) return false;

            bool pressed;
            if (Has(json, "pressed")) pressed = packet.pressed;
            else if (packet.kind == "press") pressed = true;
            else if (packet.kind == "release") pressed = false;
            else return false;   // e.g. "state" without a pressed flag

            string side = packet.side ?? "";
            string unit = packet.unit ?? "";
            if (!Has(json, "unit"))
            {
                unit = side;
                side = PhysicalSide(name);
            }
            if (string.IsNullOrEmpty(side)) side = PhysicalSide(name);

            ev = new ZwiftButtonEvent
            {
                Button = name.ToUpperInvariant(),
                Side = side.ToLowerInvariant(),
                Unit = unit.ToUpperInvariant(),
                Pressed = pressed,
            };
            return true;
        }

        static bool Has(string json, string key) => json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Which buttons are held, per unit. The Zwift Ride's left unit relays the right unit's buttons,
    /// so one A press can arrive from both units about 4 ms apart: a button counts as held while ANY
    /// unit reports it held, so the duplicate never makes a second press. Plain C#.
    /// </summary>
    public class ZwiftButtonState
    {
        readonly HashSet<string> _held = new HashSet<string>();

        static string Key(string unit, string button, string side) => (unit ?? "") + "|" + (button ?? "").ToUpperInvariant() + "|" + (side ?? "").ToLowerInvariant();

        public void Apply(ZwiftButtonEvent e)
        {
            string k = Key(e.Unit, e.Button, e.Side);
            if (e.Pressed) _held.Add(k); else _held.Remove(k);
        }

        /// <summary>Held on any unit. side "" (or "any") matches either side.</summary>
        public bool IsHeld(string button, string side = "")
        {
            if (string.IsNullOrEmpty(button)) return false;
            string b = "|" + button.ToUpperInvariant() + "|";
            string s = string.IsNullOrEmpty(side) || side.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : side.ToLowerInvariant();
            foreach (var k in _held)
            {
                int i = k.IndexOf(b, StringComparison.Ordinal);
                if (i < 0) continue;
                if (s == null || k.Substring(i + b.Length) == s) return true;
            }
            return false;
        }

        /// <summary>Forget everything a unit was holding (it disconnected).</summary>
        public void ClearUnit(string unit)
        {
            string p = (unit ?? "").ToUpperInvariant() + "|";
            _held.RemoveWhere(k => k.StartsWith(p, StringComparison.Ordinal));
        }

        public void Clear() => _held.Clear();
        public int HeldCount => _held.Count;
    }

    /// <summary>
    /// Listens for Zwift Ride (or Play) button events from zwift_play_listen.py on UDP (default
    /// 127.0.0.1:5006). The socket thread only queues datagrams; they are applied on the main thread
    /// in <see cref="Pump"/>. Closes the socket when disabled (leaving Play mode).
    /// </summary>
    public class ZwiftRideUdpReceiver : MonoBehaviour
    {
        [Tooltip("UDP port zwift_play_listen.py sends to (default 5006; never 5005, that's the KICKR).")]
        public int port = 5006;
        public bool logEvents = false;

        readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        UdpClient _client;
        Thread _thread;
        volatile bool _running;

        public ZwiftButtonState State { get; } = new ZwiftButtonState();
        public bool IsListening { get; private set; }
        public int PacketsReceived { get; private set; }
        public int ButtonEvents { get; private set; }
        public string LastEvent { get; private set; } = "";
        public string LastError { get; private set; } = "";

        void OnEnable() => StartListening();
        void OnDisable() => StopListening();
        void OnDestroy() => StopListening();
        void Update() => Pump();

        public void StartListening()
        {
            if (_running) return;
            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                _client.Client.ReceiveTimeout = 500;
            }
            catch (SocketException e)
            {
                LastError = $"Could not bind UDP port {port}: {e.Message}";
                Debug.LogWarning("[ZwiftRideUdpReceiver] " + LastError);
                _client = null;
                return;
            }
            _running = true;
            IsListening = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ZwiftRideUdpReceiver" };
            _thread.Start();
            Debug.Log($"[ZwiftRideUdpReceiver] Listening for controller buttons on UDP port {port}");
        }

        public void StopListening()
        {
            if (!_running && _client == null) return;
            _running = false;
            IsListening = false;
            try { _client?.Close(); } catch { }
            _client = null;
            if (_thread != null && _thread.IsAlive) _thread.Join(1500);
            _thread = null;
            State.Clear();
            Debug.Log($"[ZwiftRideUdpReceiver] Stopped listening on UDP port {port}");
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var c = _client;
                    if (c == null) break;
                    _queue.Enqueue(Encoding.UTF8.GetString(c.Receive(ref any)));
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut || e.SocketErrorCode == SocketError.ConnectionReset) { }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (!_running) break; }
            }
        }

        /// <summary>Applies queued datagrams (main thread). Safe to call more than once per frame.</summary>
        public void Pump()
        {
            while (_queue.TryDequeue(out var json)) HandleDatagram(json);
        }

        /// <summary>Applies one datagram. Public for tests.</summary>
        public bool HandleDatagram(string json)
        {
            PacketsReceived++;
            if (ZwiftButtonMessage.TryParse(json, out var ev, out var packet))
            {
                State.Apply(ev);
                ButtonEvents++;
                LastEvent = $"{ev.Button} ({ev.Side}) {(ev.Pressed ? "down" : "up")} via {(string.IsNullOrEmpty(ev.Unit) ? "?" : ev.Unit)}";
                if (logEvents) Debug.Log("[ZwiftRideUdpReceiver] " + LastEvent);
                return true;
            }
            if (packet != null && packet.kind == "disconnected")
                State.ClearUnit(string.IsNullOrEmpty(packet.unit) ? packet.side : packet.unit);
            return false;
        }
    }
}
