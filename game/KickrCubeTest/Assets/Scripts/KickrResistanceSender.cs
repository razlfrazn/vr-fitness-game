using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Kickr
{
    /// <summary>Sends {"cmd":"resistance","level":N} over UDP to kickr_bridge.py.</summary>
    public class UdpResistanceCommandSender : IResistanceCommandSender, IDisposable
    {
        readonly string _host;
        readonly int _port;
        UdpClient _udp;

        public UdpResistanceCommandSender(string host, int port) { _host = host; _port = port; }

        public void SendResistance(int level)
        {
            try
            {
                if (_udp == null) _udp = new UdpClient();
                byte[] bytes = Encoding.UTF8.GetBytes(KickrResistanceSender.BuildCommandJson(level));
                _udp.Send(bytes, bytes.Length, _host, _port);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            try { _udp?.Close(); } catch { }
            _udp = null;
        }
    }

    /// <summary>
    /// Reads the course grade under the mover, maps it to a KICKR resistance level, adds the
    /// red-zone offset (or subtracts the green-zone offset) while the cube is inside a zone, and sends it to the bridge
    /// (default UDP 127.0.0.1:5007). The decision logic lives in <see cref="ResistanceController"/>
    /// (plain C#). Sends the baseline when disabled (leaving Play mode) so the trainer isn't left on a hill.
    /// Tests can set <see cref="CommandSender"/> to a mock so nothing reaches the real trainer.
    /// </summary>
    public class KickrResistanceSender : MonoBehaviour
    {
        public KickrCubeMover mover;
        public KickrTrack track;

        [Header("Bridge command port")]
        public string host = "127.0.0.1";
        public int port = 5007;
        [Tooltip("Untick to compute levels without sending anything to the trainer.")]
        public bool sendToTrainer = true;
        public bool resetToBaselineOnDisable = true;

        [Header("Grade -> resistance")]
        public TerrainResistanceMapper mapping = new TerrainResistanceMapper();
        public ResistanceRateLimiter rateLimit = new ResistanceRateLimiter();

        [Header("Zone resistance (red = terrain + offset, green = terrain - offset)")]
        public ZoneResistance zoneResistance = new ZoneResistance();
        [Tooltip("Zones to check. Empty = use the mover's zones.")]
        public List<SpeedZone> zones = new List<SpeedZone>();

        public bool logCommands = true;
        public bool showReadout = true;

        public float CurrentGradePercent { get; private set; }
        public int TerrainLevel => _controller != null ? _controller.TerrainLevel : -1;
        public int TargetLevel => _controller != null ? _controller.TargetLevel : -1;
        public bool InSlowdownZone => _controller != null && _controller.InSlowdownZone;
        public bool InBoostZone => _controller != null && _controller.InBoostZone;
        public int LastSentLevel { get; private set; } = -1;
        public int CommandsSent { get; private set; }
        public string LastError { get; private set; } = "";

        /// <summary>Where levels go. Defaults to UDP to host:port; set a mock in tests.</summary>
        public IResistanceCommandSender CommandSender
        {
            get { return _commandSender ?? (_commandSender = _udpSender = new UdpResistanceCommandSender(host, port)); }
            set { _commandSender = value; }
        }

        IResistanceCommandSender _commandSender;
        UdpResistanceCommandSender _udpSender;
        ResistanceController _controller;
        GUIStyle _style;

        public static string BuildCommandJson(int level) => "{\"cmd\":\"resistance\",\"level\":" + level + "}";

        sealed class Relay : IResistanceCommandSender
        {
            readonly KickrResistanceSender _owner;
            public Relay(KickrResistanceSender owner) { _owner = owner; }
            public void SendResistance(int level) { if (_owner.sendToTrainer) _owner.Send(level); }
        }

        void OnEnable()
        {
            _controller = new ResistanceController(mapping, rateLimit, zoneResistance, new Relay(this));
            _controller.Reset();
            CommandsSent = 0;
            LastSentLevel = -1;
        }

        void Update()
        {
            if (mover == null || track == null || _controller == null) return;
            float z = mover.transform.position.z;
            CurrentGradePercent = track.GradePercentAtWorldZ(z);
            var list = zones.Count > 0 ? zones : mover.zones;
            _controller.Tick(CurrentGradePercent, ZoneResistance.InsideAnySlowdown(list, z), ZoneResistance.InsideAnyBoost(list, z), Time.realtimeSinceStartupAsDouble);
        }

        void OnDisable()
        {
            if (resetToBaselineOnDisable && sendToTrainer && CommandsSent > 0 && LastSentLevel != mapping.baselineLevel)
                Send(mapping.LevelForGrade(0f));
            _udpSender?.Dispose();
            if (ReferenceEquals(_commandSender, _udpSender)) _commandSender = null;
            _udpSender = null;
        }

        public void Send(int level)
        {
            try
            {
                CommandSender.SendResistance(level);
                LastSentLevel = level;
                CommandsSent++;
                LastError = "";
                if (logCommands)
                    Debug.Log($"[KickrResistance] grade {CurrentGradePercent:+0.0;-0.0;0}%{ZoneLabel(false)} -> resistance {level} ({(_commandSender is UdpResistanceCommandSender ? $"UDP {host}:{port}" : "custom sender")})");
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
        }

        string ZoneLabel(bool rich)
        {
            string s = "";
            if (InSlowdownZone) s += rich ? $"  <color=red>RED +{zoneResistance.slowdownOffset}</color>" : $" red zone +{zoneResistance.slowdownOffset}";
            if (InBoostZone) s += rich ? $"  <color=lime>GREEN -{zoneResistance.boostOffset}</color>" : $" green zone -{zoneResistance.boostOffset}";
            return s;
        }

        void OnGUI()
        {
            if (!showReadout) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.UpperLeft, richText = true };
            string section = track != null && mover != null ? track.SectionNameAtWorldZ(mover.transform.position.z) : "-";
            string err = string.IsNullOrEmpty(LastError) ? "" : $"\n<color=orange>{LastError}</color>";
            string red = ZoneLabel(true);
            string text =
                $"<b>Terrain</b>  {section}  grade {CurrentGradePercent:+0.0;-0.0;0}%\n" +
                $"Resistance: target {TargetLevel}  sent {LastSentLevel}  ({CommandsSent} cmds{(sendToTrainer ? "" : ", sending OFF")}){red}\n" +
                $"Bridge cmd port: UDP {port}" + err;
            GUI.Box(new Rect(10, 270, 560, 100), text, _style);
        }
    }
}
