using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>One straight piece of the course: a horizontal length and a constant grade.</summary>
    [Serializable]
    public class TrackSection
    {
        public string name;
        [Tooltip("Horizontal length along +Z (metres).")]
        public float length;
        [Tooltip("Grade in percent: +6 = 6 m up per 100 m forward, negative = downhill.")]
        public float gradePercent;

        public TrackSection() { }
        public TrackSection(string name, float length, float gradePercent)
        {
            this.name = name;
            this.length = length;
            this.gradePercent = gradePercent;
        }
    }

    /// <summary>
    /// Height profile of a straight course along +Z, made of constant-grade sections. Plain C#
    /// (no MonoBehaviour), so it is unit-testable. Distances are local: z = 0 is the start line,
    /// height 0 is the start height. Before the start and after the end the course is flat.
    /// </summary>
    [Serializable]
    public class TrackProfile
    {
        public List<TrackSection> sections = new List<TrackSection>();

        /// <summary>The V1 gray-box course: 500 m with two climbs, two descents and a valley.</summary>
        public static TrackProfile CreateV1()
        {
            var p = new TrackProfile();
            p.sections.Add(new TrackSection("Start flat", 40f, 0f));      //   0- 40  h 0.0
            p.sections.Add(new TrackSection("Climb 1", 60f, 6f));         //  40-100  h 0.0 -> 3.6
            p.sections.Add(new TrackSection("Summit 1", 30f, 0f));        // 100-130  h 3.6
            p.sections.Add(new TrackSection("Descent 1", 60f, -8f));      // 130-190  h 3.6 -> -1.2
            p.sections.Add(new TrackSection("Valley", 40f, 0f));          // 190-230  h -1.2
            p.sections.Add(new TrackSection("Climb 2", 60f, 10f));        // 230-290  h -1.2 -> 4.8
            p.sections.Add(new TrackSection("Summit 2", 30f, 0f));        // 290-320  h 4.8
            p.sections.Add(new TrackSection("Descent 2", 60f, -5f));      // 320-380  h 4.8 -> 1.8
            p.sections.Add(new TrackSection("Run-out", 60f, -3f));        // 380-440  h 1.8 -> 0.0
            p.sections.Add(new TrackSection("Finish straight", 60f, 0f)); // 440-500  h 0.0
            return p;
        }

        /// <summary>Total horizontal length (metres).</summary>
        public float Length
        {
            get
            {
                float total = 0f;
                foreach (var s in sections) total += Mathf.Max(0f, s.length);
                return total;
            }
        }

        /// <summary>Local start z of section i.</summary>
        public float SectionStart(int index)
        {
            float z = 0f;
            for (int i = 0; i < index && i < sections.Count; i++) z += Mathf.Max(0f, sections[i].length);
            return z;
        }

        /// <summary>Height at the start of section i.</summary>
        public float SectionStartHeight(int index)
        {
            float h = 0f;
            for (int i = 0; i < index && i < sections.Count; i++)
                h += Mathf.Max(0f, sections[i].length) * sections[i].gradePercent / 100f;
            return h;
        }

        /// <summary>Index of the section containing local z (a boundary belongs to the next section), or -1 outside.</summary>
        public int SectionIndexAt(float z)
        {
            if (z < 0f) return -1;
            float start = 0f;
            for (int i = 0; i < sections.Count; i++)
            {
                float len = Mathf.Max(0f, sections[i].length);
                if (z < start + len) return i;
                start += len;
            }
            return -1;
        }

        /// <summary>Course height (metres) at local z. Flat before the start and after the end.</summary>
        public float HeightAt(float z)
        {
            if (z <= 0f) return 0f;
            float start = 0f, h = 0f;
            foreach (var s in sections)
            {
                float len = Mathf.Max(0f, s.length);
                float g = s.gradePercent / 100f;
                if (z < start + len) return h + (z - start) * g;
                start += len;
                h += len * g;
            }
            return h;
        }

        /// <summary>Grade (percent) at local z; 0 outside the course.</summary>
        public float GradePercentAt(float z)
        {
            int i = SectionIndexAt(z);
            return i < 0 ? 0f : sections[i].gradePercent;
        }

        /// <summary>Pitch of the course at local z, in degrees (positive = uphill).</summary>
        public float PitchDegreesAt(float z) => GradeToPitchDegrees(GradePercentAt(z));

        public static float GradeToPitchDegrees(float gradePercent) => Mathf.Atan(gradePercent / 100f) * Mathf.Rad2Deg;

        public float MinHeight
        {
            get
            {
                float min = 0f;
                for (int i = 0; i <= sections.Count; i++) min = Mathf.Min(min, SectionStartHeight(i));
                return min;
            }
        }

        public float MaxHeight
        {
            get
            {
                float max = 0f;
                for (int i = 0; i <= sections.Count; i++) max = Mathf.Max(max, SectionStartHeight(i));
                return max;
            }
        }
    }
}
