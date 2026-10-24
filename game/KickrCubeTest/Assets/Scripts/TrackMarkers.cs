using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Keeps an endless-looking track around the target: the ground follows the target in
    /// spacing-sized steps, and marker posts that fall behind are moved to the front.
    /// </summary>
    public class TrackMarkers : MonoBehaviour
    {
        public Transform target;
        public Transform ground;
        public List<Transform> markers = new List<Transform>();
        public float spacing = 5f;
        public float keepBehind = 15f;

        void LateUpdate()
        {
            if (target == null) return;
            float z = target.position.z;

            if (ground != null)
            {
                var g = ground.position;
                g.z = Mathf.Floor(z / spacing) * spacing;
                ground.position = g;
            }

            if (markers.Count == 0) return;
            // Each side has markers.Count/2 posts; posts are recycled in pairs-agnostic fashion.
            float span = spacing * Mathf.Ceil(markers.Count / 2f);
            foreach (var m in markers)
            {
                if (m == null) continue;
                if (m.position.z < z - keepBehind)
                {
                    var p = m.position;
                    p.z += span;
                    m.position = p;
                }
            }
        }
    }
}
