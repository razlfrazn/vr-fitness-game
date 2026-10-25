using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Scene wrapper around a <see cref="TrackProfile"/>. The track starts at this object's
    /// position and runs along world +Z. Movers sample it for height and grade.
    /// </summary>
    public class KickrTrack : MonoBehaviour
    {
        public TrackProfile profile = TrackProfile.CreateV1();

        public float StartWorldZ => transform.position.z;
        public float EndWorldZ => transform.position.z + profile.Length;

        public float HeightAtWorldZ(float worldZ) => transform.position.y + profile.HeightAt(worldZ - transform.position.z);
        public float GradePercentAtWorldZ(float worldZ) => profile.GradePercentAt(worldZ - transform.position.z);
        public float PitchDegreesAtWorldZ(float worldZ) => profile.PitchDegreesAt(worldZ - transform.position.z);

        public string SectionNameAtWorldZ(float worldZ)
        {
            int i = profile.SectionIndexAt(worldZ - transform.position.z);
            return i < 0 ? "-" : profile.sections[i].name;
        }
    }
}
