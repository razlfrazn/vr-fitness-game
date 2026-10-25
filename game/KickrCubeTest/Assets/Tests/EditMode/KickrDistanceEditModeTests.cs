using NUnit.Framework;

namespace Kickr.Tests
{
    /// <summary>Course-based distance (no trainer involved).</summary>
    public class CourseDistanceTests
    {
        const float Eps = 1e-3f;
        const float Lap = 500f;

        [Test]
        public void ZeroAtStart()
        {
            Assert.AreEqual(0f, CourseDistance.LapPosition(0f, 0f, Lap), Eps);
            Assert.AreEqual(0f, CourseDistance.Total(0, 0f, Lap), Eps);
            StringAssert.Contains("Total: 0.00 km  (0 m)", CourseDistance.Label(0, 0f, Lap));
            StringAssert.Contains("Lap 1: 0 / 500 m", CourseDistance.Label(0, 0f, Lap));
        }

        [Test]
        public void V1CourseLapIs500m()
        {
            Assert.AreEqual(Lap, TrackProfile.CreateV1().Length, Eps);
        }

        [Test]
        public void TotalContinuesAcrossLaps()
        {
            Assert.AreEqual(250f, CourseDistance.Total(0, 250f, Lap), Eps);
            Assert.AreEqual(750f, CourseDistance.Total(1, 250f, Lap), Eps);
            Assert.AreEqual(1234.5f, CourseDistance.Total(2, 234.5f, Lap), Eps);
            StringAssert.Contains("Total: 1.23 km  (1234 m)", CourseDistance.Label(2, 234.4f, Lap));
            StringAssert.Contains("Lap 3: 234 / 500 m", CourseDistance.Label(2, 234.4f, Lap));
        }

        [Test]
        public void WrapsAroundAt500m()
        {
            // Just before the line on lap 1, then just after it on lap 2: total keeps rising.
            float before = CourseDistance.Total(0, 499.9f, Lap);
            float after = CourseDistance.Total(1, 0.1f, Lap);
            Assert.AreEqual(499.9f, before, Eps);
            Assert.AreEqual(500.1f, after, Eps);
            Assert.Greater(after, before);

            CourseDistance.Split(500f, Lap, out int laps, out float pos);
            Assert.AreEqual(1, laps);
            Assert.AreEqual(0f, pos, Eps);
            CourseDistance.Split(1234.5f, Lap, out laps, out pos);
            Assert.AreEqual(2, laps);
            Assert.AreEqual(234.5f, pos, Eps);
            CourseDistance.Split(499.9f, Lap, out laps, out pos);
            Assert.AreEqual(0, laps);
            Assert.AreEqual(499.9f, pos, Eps);
        }

        [Test]
        public void LapPositionIsRelativeToTrackStartAndClamped()
        {
            Assert.AreEqual(120f, CourseDistance.LapPosition(130f, 10f, Lap), Eps);
            Assert.AreEqual(0f, CourseDistance.LapPosition(-5f, 0f, Lap), Eps);
            Assert.AreEqual(Lap, CourseDistance.LapPosition(600f, 0f, Lap), Eps);
        }

        [Test]
        public void DistanceBoxDoesNotOverlapOtherHudBoxes()
        {
            Assert.IsFalse(KickrHud.DistanceBox.Overlaps(KickrHud.KickrBox), "overlaps KICKR box");
            Assert.IsFalse(KickrHud.DistanceBox.Overlaps(KickrHud.TerrainBox), "overlaps Terrain box");
            Assert.IsFalse(KickrHud.KickrBox.Overlaps(KickrHud.TerrainBox), "KICKR overlaps Terrain box");
        }
    }
}
