using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Reads the course grade under the mover, maps it to a KICKR resistance level and sends
    /// {"cmd":"resistance","level":N} over UDP to kickr_bridge.py (default 127.0.0.1:5007).
    /// Rate-limited (change >= 1 level, at most 2 Hz by default). Sends the baseline when it is
    /// disabled (leaving Play mode) so the trainer isn't left on a hill.
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

        public bool logCommands = true;
        public bool showReadout = true;

        public float CurrentGradePercent { get; private set; }
        public int TargetLevel { get; private set; } = -1;
        public int LastSentLevel { get; private set; } = -1;
        public int CommandsSent { get; private set; }
        public string LastError { get; private set; } = "";

        UdpClient _udp;
        GUIStyle _style;

        public static string BuildCommandJson(int level) => "{\"cmd\":\"resistance\",\"level\":" + level + "}";

        void OnEnable()
        {
            rateLimit.Reset();
            CommandsSent = 0;
            LastSentLevel = -1;
        }

        void Update()
        {
            if (mover == null || track == null) return;
            CurrentGradePercent = track.GradePercentAtWorldZ(mover.transform.position.z);
            TargetLevel = mapping.LevelForGrade(CurrentGradePercent);
            if (sendToTrainer && rateLimit.ShouldSend(TargetLevel, Time.realtimeSinceStartupAsDouble))
                Send(TargetLevel);
        }

        void OnDisable()
        {
            if (resetToBaselineOnDisable && sendToTrainer && CommandsSent > 0 && LastSentLevel != mapping.baselineLevel)
                Send(mapping.LevelForGrade(0f));
            try { _udp?.Close(); } catch { }
            _udp = null;
        }

        public void Send(int level)
        {
            try
            {
                if (_udp == null) _udp = new UdpClient();
                byte[] bytes = Encoding.UTF8.GetBytes(BuildCommandJson(level));
                _udp.Send(bytes, bytes.Length, host, port);
                LastSentLevel = level;
                CommandsSent++;
                LastError = "";
                if (logCommands) Debug.Log($"[KickrResistance] grade {CurrentGradePercent:+0.0;-0.0;0}% -> resistance {level} (UDP {host}:{port})");
            }
            catch (Exception e)
            {
                LastError = e.Message;
                try { _udp?.Close(); } catch { }
                _udp = null;
            }
        }

        void OnGUI()
        {
            if (!showReadout) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.UpperLeft, richText = true };
            string section = track != null && mover != null ? track.SectionNameAtWorldZ(mover.transform.position.z) : "-";
            string err = string.IsNullOrEmpty(LastError) ? "" : $"\n<color=orange>{LastError}</color>";
            string text =
                $"<b>Terrain</b>  {section}  grade {CurrentGradePercent:+0.0;-0.0;0}%\n" +
                $"Resistance: target {TargetLevel}  sent {LastSentLevel}  ({CommandsSent} cmds{(sendToTrainer ? "" : ", sending OFF")})\n" +
                $"Bridge cmd port: UDP {port}" + err;
            GUI.Box(new Rect(10, 270, 460, 100), text, _style);
        }
    }
}
