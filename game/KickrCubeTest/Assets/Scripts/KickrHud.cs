using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Shared look and layout for the OnGUI stats boxes (KICKR, Terrain, Distance, Boost, Ride buttons), so every box
    /// uses the same font size and screen-pixel scale. Change <see cref="FontSize"/> here to resize
    /// them all together. Box rects are in unscaled screen pixels (GUI.matrix is never changed), so
    /// the Game view's Scale slider at 1x shows them 1:1.
    /// </summary>
    public static class KickrHud
    {
        public const int FontSize = 20;

        public static readonly Rect KickrBox = new Rect(10, 10, 460, 250);
        public static readonly Rect TerrainBox = new Rect(10, 270, 560, 100);
        /// <summary>To the right of the KICKR box, above the Terrain box's right end.</summary>
        public static readonly Rect DistanceBox = new Rect(480, 10, 300, 100);
        /// <summary>Boost count box, under the distance box.</summary>
        public static readonly Rect BoostBox = new Rect(480, 120, 300, 75);
        /// <summary>
        /// Zwift Ride button map, right of the Distance/Boost boxes. Needs a Game view of at least
        /// 1092 x 366 px (the editor's Free Aspect view on ProjectX is 1102 x 505).
        /// </summary>
        public static readonly Rect ButtonMapBox = new Rect(790, 10, 302, 356);

        /// <summary>Every HUD box, for overlap tests.</summary>
        public static Rect[] AllBoxes => new[] { KickrBox, TerrainBox, DistanceBox, BoostBox, ButtonMapBox };

        public static GUIStyle CreateBoxStyle() =>
            new GUIStyle(GUI.skin.box) { fontSize = FontSize, alignment = TextAnchor.UpperLeft, richText = true };
    }
}
