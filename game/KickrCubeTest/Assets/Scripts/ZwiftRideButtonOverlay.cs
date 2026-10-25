using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>One input the Zwift Ride protocol defines (opcode 0x23 notifications).</summary>
    public sealed class ZwiftRideButton
    {
        public readonly string Name;          // listener name, e.g. "A", "LEFT_PADDLE"
        public readonly string Label;         // short HUD label
        public readonly string Side;          // "left" / "right"
        public readonly uint Mask;            // field 1 bit (cleared = pressed); 0 for the paddles
        public readonly int AnalogLocation;   // field 3 location for the paddles; -1 for buttons

        public ZwiftRideButton(string name, string label, string side, uint mask, int analogLocation = -1)
        {
            Name = name; Label = label; Side = side; Mask = mask; AnalogLocation = analogLocation;
        }

        public bool IsPaddle => AnalogLocation >= 0;
        /// <summary>"0x00010" for a button, "analog 0" for a paddle (same text the listener sends as "code").</summary>
        public string Code => IsPaddle ? "analog " + AnalogLocation : "0x" + Mask.ToString("x5");
    }

    /// <summary>
    /// Every input the Zwift Ride protocol defines, grouped by handlebar side. Mirrors RIDE_BUTTONS and
    /// ANALOG_LOC in tools/zwift_ride/zwift_play_listen.py. Plain C#.
    /// </summary>
    public static class ZwiftRideButtonMap
    {
        public const int PaddleThreshold = 25;

        public static readonly IReadOnlyList<ZwiftRideButton> All = new[]
        {
            new ZwiftRideButton("DPAD_UP",          "D-pad up",    "left",  0x00002),
            new ZwiftRideButton("DPAD_DOWN",        "D-pad down",  "left",  0x00008),
            new ZwiftRideButton("DPAD_LEFT",        "D-pad left",  "left",  0x00001),
            new ZwiftRideButton("DPAD_RIGHT",       "D-pad right", "left",  0x00004),
            new ZwiftRideButton("LEFT_SHIFT_UP",    "Shift up",    "left",  0x00100),
            new ZwiftRideButton("LEFT_SHIFT_DOWN",  "Shift down",  "left",  0x00200),
            new ZwiftRideButton("LEFT_POWERUP",     "Power-up",    "left",  0x00400),
            new ZwiftRideButton("LEFT_ON_OFF",      "On/off",      "left",  0x00800),
            new ZwiftRideButton("LEFT_PADDLE",      "Paddle",      "left",  0, 0),
            new ZwiftRideButton("A",                "A",           "right", 0x00010),
            new ZwiftRideButton("B",                "B",           "right", 0x00020),
            new ZwiftRideButton("Y",                "Y",           "right", 0x00040),
            new ZwiftRideButton("Z",                "Z",           "right", 0x00080),
            new ZwiftRideButton("RIGHT_SHIFT_UP",   "Shift up",    "right", 0x01000),
            new ZwiftRideButton("RIGHT_SHIFT_DOWN", "Shift down",  "right", 0x02000),
            new ZwiftRideButton("RIGHT_POWERUP",    "Power-up",    "right", 0x04000),
            new ZwiftRideButton("RIGHT_ON_OFF",     "On/off",      "right", 0x08000),
            new ZwiftRideButton("RIGHT_PADDLE",     "Paddle",      "right", 0, 1),
        };

        public static List<ZwiftRideButton> ForSide(string side)
        {
            var list = new List<ZwiftRideButton>();
            foreach (var b in All)
                if (string.Equals(b.Side, side, StringComparison.OrdinalIgnoreCase)) list.Add(b);
            return list;
        }

        /// <summary>The input with this name (case-insensitive), or null.</summary>
        public static ZwiftRideButton Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var b in All)
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        public static bool IsKnown(string name) => Find(name) != null;

        /// <summary>Name the listener uses for a field-1 bit outside the 16 known ones.</summary>
        public static string UnknownName(uint mask) => "UNKNOWN_0x" + mask.ToString("x5");

        /// <summary>
        /// Buttons pressed in a field-1 bitmask (a cleared bit means pressed), as the listener decodes
        /// it: the 16 known bits by name, and any other cleared bit below the mask's highest set bit as
        /// UNKNOWN_0x.... (bits above the highest set bit are just not sent, so they don't count).
        /// </summary>
        public static List<string> PressedInBitmap(uint bitmap)
        {
            var pressed = new List<string>();
            uint known = 0;
            foreach (var b in All)
            {
                if (b.IsPaddle) continue;
                known |= b.Mask;
                if ((bitmap & b.Mask) == 0) pressed.Add(b.Name);
            }
            int width = 0;
            for (uint v = bitmap; v != 0; v >>= 1) width++;
            for (int bit = 0; bit < width; bit++)
            {
                uint m = 1u << bit;
                if ((known & m) == 0 && (bitmap & m) == 0) pressed.Add(UnknownName(m));
            }
            return pressed;
        }
    }

    /// <summary>One cell of the overlay: a button and whether it is lit.</summary>
    public struct ZwiftOverlayCell
    {
        public ZwiftRideButton Button;
        public bool Lit;
    }

    /// <summary>What the overlay draws, from a <see cref="ZwiftButtonState"/>. Plain C#.</summary>
    public static class ZwiftRideOverlayModel
    {
        /// <summary>The side's buttons in map order; lit while held on any unit.</summary>
        public static List<ZwiftOverlayCell> Cells(ZwiftButtonState state, string side)
        {
            var cells = new List<ZwiftOverlayCell>();
            foreach (var b in ZwiftRideButtonMap.ForSide(side))
                cells.Add(new ZwiftOverlayCell { Button = b, Lit = state != null && state.IsHeld(b.Name) });
            return cells;
        }

        /// <summary>Held inputs the map doesn't know, shown raw: the listener's code ("0x10000") if it sent one, else the name.</summary>
        public static List<string> UnknownHeld(ZwiftButtonState state)
        {
            var list = new List<string>();
            if (state == null) return list;
            foreach (var name in state.HeldButtons())
            {
                if (ZwiftRideButtonMap.IsKnown(name)) continue;
                string code = state.CodeOf(name);
                list.Add(string.IsNullOrEmpty(code) ? name : code);
            }
            return list;
        }

        /// <summary>Names of the lit known buttons (both sides), for tests and logging.</summary>
        public static List<string> LitNames(ZwiftButtonState state)
        {
            var list = new List<string>();
            foreach (var side in new[] { "left", "right" })
                foreach (var c in Cells(state, side))
                    if (c.Lit) list.Add(c.Button.Name);
            return list;
        }
    }

    /// <summary>
    /// HUD box with every Zwift Ride input, Left and Right columns, each lit while held. Driven by the
    /// <see cref="ZwiftRideUdpReceiver"/> (UDP 5006 events from zwift_play_listen.py). Inputs the map
    /// doesn't know are shown raw (code and bytes). Same style and screen-pixel scale as the other
    /// boxes (<see cref="KickrHud"/>).
    /// </summary>
    public class ZwiftRideButtonOverlay : MonoBehaviour
    {
        public ZwiftRideUdpReceiver buttons;
        public bool show = true;

        static readonly Color LitColor = new Color(0.20f, 0.85f, 0.35f, 0.95f);
        static readonly Color IdleColor = new Color(0.25f, 0.25f, 0.25f, 0.85f);

        GUIStyle _box, _header, _chip, _line;
        Texture2D _litTex, _idleTex;

        const float Pad = 8f, RowH = 24f, RowGap = 3f;

        void Awake()
        {
            if (buttons == null) buttons = FindAnyObjectByType<ZwiftRideUdpReceiver>();
        }

        void OnDestroy()
        {
            if (_litTex != null) Destroy(_litTex);
            if (_idleTex != null) Destroy(_idleTex);
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (_box != null) return;
            _box = KickrHud.CreateBoxStyle();
            _header = new GUIStyle(GUI.skin.label) { fontSize = KickrHud.FontSize, richText = true, alignment = TextAnchor.UpperLeft };
            _line = new GUIStyle(_header) { fontSize = KickrHud.FontSize - 3, clipping = TextClipping.Clip, wordWrap = false };
            _litTex = Solid(LitColor);
            _idleTex = Solid(IdleColor);
            _chip = new GUIStyle(GUI.skin.label) { fontSize = KickrHud.FontSize - 3, alignment = TextAnchor.MiddleCenter, richText = true };
        }

        void OnGUI()
        {
            if (!show) return;
            EnsureStyles();
            Rect r = KickrHud.ButtonMapBox;
            var state = buttons != null ? buttons.State : null;
            string status = buttons == null ? "no receiver"
                : buttons.IsListening ? $"UDP {buttons.port}"
                : $"<color=#ff6666>UDP {buttons.port} off</color>";
            GUI.Box(r, "", _box);
            GUI.Label(new Rect(r.x + Pad, r.y + 3, r.width - 2 * Pad, 28), $"<b>Zwift Ride buttons</b>  <size={KickrHud.FontSize - 4}>{status}</size>", _header);

            float columnW = (r.width - 3 * Pad) / 2f;
            DrawColumn("Left", "left", state, r.x + Pad, r.y + 32, columnW);
            DrawColumn("Right", "right", state, r.x + 2 * Pad + columnW, r.y + 32, columnW);

            var unknown = ZwiftRideOverlayModel.UnknownHeld(state);
            string held = unknown.Count > 0 ? "<color=#55ff55>" + string.Join(" ", unknown) + "</color>" : "none held";
            string last = buttons != null && !string.IsNullOrEmpty(buttons.LastUnknown) ? buttons.LastUnknown : "-";
            GUI.Label(new Rect(r.x + Pad, r.yMax - 52, r.width - 2 * Pad, 24), "Unknown: " + held, _line);
            GUI.Label(new Rect(r.x + Pad, r.yMax - 28, r.width - 2 * Pad, 24), "Last: " + last, _line);
        }

        void DrawColumn(string title, string side, ZwiftButtonState state, float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, width, 26), "<b>" + title + "</b>", _header);
            var cells = ZwiftRideOverlayModel.Cells(state, side);
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = new Rect(x, y + 27 + i * (RowH + RowGap), width, RowH);
                GUI.DrawTexture(cell, cells[i].Lit ? _litTex : _idleTex);
                string label = cells[i].Lit ? "<b><color=black>" + cells[i].Button.Label + "</color></b>" : cells[i].Button.Label;
                GUI.Label(cell, label, _chip);
            }
        }
    }
}
