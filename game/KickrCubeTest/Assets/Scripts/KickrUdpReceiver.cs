using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kickr
{
    /// <summary>
    /// One datagram from kickr_bridge.py. Field names match the bridge's JSON keys exactly so
    /// JsonUtility can fill them. The bridge only includes the keys the trainer reported in that
    /// FTMS notification, so a missing key is NOT the same as zero (see KickrUdpReceiver.Apply).
    /// </summary>
    [Serializable]
    public class KickrPacket
    {
        public double t;                 // bridge wall-clock time (unix seconds)
        public float speed_kmh;
        public float avg_speed_kmh;
        public float cadence_rpm;
        public float avg_cadence_rpm;
        public int distance_m;
        public int resistance_level;
        public int power_w;
        public int avg_power_w;
        public int energy_kcal;
        public int heart_rate;
        public int elapsed_s;
    }

    public enum KickrConnectionState
    {
        NotListening,   // socket not open (disabled, or the port could not be bound)
        WaitingForData, // listening, nothing received yet
        Live,           // a packet arrived within staleAfterSeconds
        Stale           // we had data, but nothing recently
    }

    /// <summary>
    /// Listens for KICKR bridge JSON on UDP (default 127.0.0.1:5005) on a background thread and
    /// exposes the latest values thread-safely. The socket and thread are closed in OnDisable,
    /// so entering/leaving Play mode never leaves the port bound.
    /// </summary>
    public class KickrUdpReceiver : MonoBehaviour
    {
        [Tooltip("UDP port the bridge sends to (kickr_bridge.py uses 5005).")]
        public int port = 5005;

        [Tooltip("After this many seconds without a packet the state becomes Stale.")]
        public float staleAfterSeconds = 2f;

        [Tooltip("Log every received packet to the Console (noisy).")]
        public bool logPackets = false;

        readonly object _lock = new object();
        KickrPacket _latest = new KickrPacket();
        string _lastRaw = "";
        string _lastError = "";
        long _lastPacketTimestamp;   // Stopwatch ticks, 0 = never
        int _packetsReceived;
        int _parseErrors;

        UdpClient _client;
        Thread _thread;
        volatile bool _running;
        volatile bool _listening;

        // ---- Public, thread-safe read API ------------------------------------------------

        public float SpeedKmh { get { lock (_lock) return _latest.speed_kmh; } }
        public float CadenceRpm { get { lock (_lock) return _latest.cadence_rpm; } }
        public int PowerW { get { lock (_lock) return _latest.power_w; } }
        public int DistanceM { get { lock (_lock) return _latest.distance_m; } }
        public int PacketsReceived => Interlocked.CompareExchange(ref _packetsReceived, 0, 0);
        public int ParseErrors => Interlocked.CompareExchange(ref _parseErrors, 0, 0);
        public bool IsListening => _listening;
        public string LastError { get { lock (_lock) return _lastError; } }
        public string LastRawJson { get { lock (_lock) return _lastRaw; } }

        /// <summary>Copy of the latest merged packet (safe to keep and modify).</summary>
        public KickrPacket Latest
        {
            get { lock (_lock) return JsonUtility.FromJson<KickrPacket>(JsonUtility.ToJson(_latest)); }
        }

        /// <summary>Seconds since the last packet, or +Infinity if none has arrived.</summary>
        public float SecondsSinceLastPacket
        {
            get
            {
                long last = Interlocked.Read(ref _lastPacketTimestamp);
                if (last == 0) return float.PositiveInfinity;
                return (float)((Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency);
            }
        }

        public KickrConnectionState State
        {
            get
            {
                if (!_listening) return KickrConnectionState.NotListening;
                if (PacketsReceived == 0) return KickrConnectionState.WaitingForData;
                return SecondsSinceLastPacket <= staleAfterSeconds
                    ? KickrConnectionState.Live
                    : KickrConnectionState.Stale;
            }
        }

        // ---- Lifecycle --------------------------------------------------------------------

        void OnEnable() => StartListening();
        void OnDisable() => StopListening();
        void OnApplicationQuit() => StopListening();
        void OnDestroy() => StopListening();

        public void StartListening()
        {
            if (_running) return;
            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                _client.Client.ReceiveTimeout = 500; // lets the thread notice _running = false
            }
            catch (SocketException e)
            {
                lock (_lock) _lastError = $"Could not bind UDP port {port}: {e.Message}";
                Debug.LogWarning($"[KickrUdpReceiver] {_lastError} (is another Unity instance or app using it?)");
                _client = null;
                return;
            }

            _running = true;
            _listening = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "KickrUdpReceiver" };
            _thread.Start();
            Debug.Log($"[KickrUdpReceiver] Listening on UDP port {port}");
        }

        public void StopListening()
        {
            if (!_running && _client == null) return;
            _running = false;
            _listening = false;
            try { _client?.Close(); } catch { /* already closed */ }
            _client = null;
            if (_thread != null && _thread.IsAlive && !_thread.Join(1500))
                Debug.LogWarning("[KickrUdpReceiver] Receive thread did not stop in time");
            _thread = null;
            Debug.Log($"[KickrUdpReceiver] Stopped listening on UDP port {port}");
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try
                {
                    var c = _client;
                    if (c == null) break;
                    data = c.Receive(ref any);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue; // Windows reports ICMP port-unreachable this way; harmless
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (!_running) break; continue; }

                HandleDatagram(Encoding.UTF8.GetString(data));
            }
        }

        /// <summary>Parse one JSON datagram and merge it into the latest values. Public for tests.</summary>
        public bool HandleDatagram(string json)
        {
            KickrPacket p;
            try { p = JsonUtility.FromJson<KickrPacket>(json); }
            catch (Exception e)
            {
                Interlocked.Increment(ref _parseErrors);
                lock (_lock) _lastError = "Bad JSON: " + e.Message;
                return false;
            }
            if (p == null) { Interlocked.Increment(ref _parseErrors); return false; }

            lock (_lock)
            {
                Apply(json, p, _latest);
                _lastRaw = json;
            }
            Interlocked.Exchange(ref _lastPacketTimestamp, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _packetsReceived);
            if (logPackets) Debug.Log("[KickrUdpReceiver] " + json);
            return true;
        }

        // FTMS can split data over several notifications ("More Data" flag), so the bridge omits
        // keys it didn't get. Only overwrite the fields that are actually present in this JSON.
        static void Apply(string json, KickrPacket src, KickrPacket dst)
        {
            if (Has(json, "t")) dst.t = src.t;
            if (Has(json, "speed_kmh")) dst.speed_kmh = src.speed_kmh;
            if (Has(json, "avg_speed_kmh")) dst.avg_speed_kmh = src.avg_speed_kmh;
            if (Has(json, "cadence_rpm")) dst.cadence_rpm = src.cadence_rpm;
            if (Has(json, "avg_cadence_rpm")) dst.avg_cadence_rpm = src.avg_cadence_rpm;
            if (Has(json, "distance_m")) dst.distance_m = src.distance_m;
            if (Has(json, "resistance_level")) dst.resistance_level = src.resistance_level;
            if (Has(json, "power_w")) dst.power_w = src.power_w;
            if (Has(json, "avg_power_w")) dst.avg_power_w = src.avg_power_w;
            if (Has(json, "energy_kcal")) dst.energy_kcal = src.energy_kcal;
            if (Has(json, "heart_rate")) dst.heart_rate = src.heart_rate;
            if (Has(json, "elapsed_s")) dst.elapsed_s = src.elapsed_s;
        }

        static bool Has(string json, string key) => json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
    }
}
