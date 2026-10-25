using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kickr.Tests
{
    public class TrackProfileTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void V1CourseHasHillsAndValleys()
        {
            var p = TrackProfile.CreateV1();
            Assert.AreEqual(500f, p.Length, Eps);
            Assert.IsTrue(p.sections.Any(s => s.gradePercent > 0f), "needs an uphill");
            Assert.IsTrue(p.sections.Any(s => s.gradePercent < 0f), "needs a downhill");
            Assert.AreEqual(4.8f, p.MaxHeight, Eps);
            Assert.AreEqual(-1.2f, p.MinHeight, Eps);
            Assert.AreEqual(0f, p.HeightAt(p.Length), Eps, "finishes at start height");
        }

        [Test]
        public void HeightIsSampledLinearlyWithinSections()
        {
            var p = TrackProfile.CreateV1();
            Assert.AreEqual(0f, p.HeightAt(0f), Eps);
            Assert.AreEqual(0f, p.HeightAt(20f), Eps);        // start flat
            Assert.AreEqual(1.8f, p.HeightAt(70f), Eps);      // halfway up Climb 1 (6%)
            Assert.AreEqual(3.6f, p.HeightAt(100f), Eps);     // top of Climb 1
            Assert.AreEqual(3.6f, p.HeightAt(115f), Eps);     // Summit 1
            Assert.AreEqual(1.2f, p.HeightAt(160f), Eps);     // halfway down Descent 1 (-8%)
            Assert.AreEqual(-1.2f, p.HeightAt(210f), Eps);    // Valley
            Assert.AreEqual(4.8f, p.HeightAt(290f), Eps);     // top of Climb 2 (10%)
            Assert.AreEqual(1.8f, p.HeightAt(380f), Eps);     // bottom of Descent 2 (-5%)
        }

        [Test]
        public void HeightIsContinuousAcrossSectionBoundaries()
        {
            var p = TrackProfile.CreateV1();
            for (int i = 1; i < p.sections.Count; i++)
            {
                float z = p.SectionStart(i);
                Assert.AreEqual(p.HeightAt(z - 0.001f), p.HeightAt(z + 0.001f), 0.001f, $"jump at z={z}");
            }
        }

        [Test]
        public void GradeAndPitchMatchSections()
        {
            var p = TrackProfile.CreateV1();
            Assert.AreEqual(0f, p.GradePercentAt(10f));
            Assert.AreEqual(6f, p.GradePercentAt(50f));
            Assert.AreEqual(-8f, p.GradePercentAt(150f));
            Assert.AreEqual(10f, p.GradePercentAt(250f));
            Assert.AreEqual(6f, p.GradePercentAt(40f), "a boundary belongs to the next section");
            Assert.AreEqual(Mathf.Atan(0.10f) * Mathf.Rad2Deg, p.PitchDegreesAt(250f), Eps);
            Assert.Greater(p.PitchDegreesAt(50f), 0f);
            Assert.Less(p.PitchDegreesAt(150f), 0f);
        }

        [Test]
        public void OutsideTheCourseIsFlat()
        {
            var p = new TrackProfile();
            p.sections.Add(new TrackSection("up", 10f, 10f));
            Assert.AreEqual(0f, p.HeightAt(-5f), Eps);
            Assert.AreEqual(1f, p.HeightAt(50f), Eps);
            Assert.AreEqual(0f, p.GradePercentAt(-1f));
            Assert.AreEqual(0f, p.GradePercentAt(10f));
            Assert.AreEqual(-1, p.SectionIndexAt(12f));
        }
    }

    public class TerrainResistanceMapperTests
    {
        [Test]
        public void UphillAboveBaselineAboveDownhill()
        {
            var m = new TerrainResistanceMapper();
            int flat = m.LevelForGrade(0f), up = m.LevelForGrade(6f), down = m.LevelForGrade(-5f);
            Assert.AreEqual(30, flat);
            Assert.Greater(up, flat);
            Assert.Less(down, flat);
        }

        [Test]
        public void DefaultMappingValues()
        {
            var m = new TerrainResistanceMapper();   // 30 + 3 levels per % grade
            Assert.AreEqual(48, m.LevelForGrade(6f));
            Assert.AreEqual(60, m.LevelForGrade(10f));
            Assert.AreEqual(6, m.LevelForGrade(-8f));
            Assert.AreEqual(15, m.LevelForGrade(-5f));
            Assert.AreEqual(21, m.LevelForGrade(-3f));
        }

        [Test]
        public void ClampsToRange()
        {
            var m = new TerrainResistanceMapper();
            Assert.AreEqual(100, m.LevelForGrade(40f));
            Assert.AreEqual(0, m.LevelForGrade(-40f));
            m.minLevel = 10; m.maxLevel = 70;
            Assert.AreEqual(70, m.LevelForGrade(25f));
            m.maxLevel = 250;                      // out-of-range settings still clamp to 0-100
            Assert.AreEqual(100, m.LevelForGrade(50f));
            Assert.AreEqual(10, m.LevelForGrade(-25f));
        }

        [Test]
        public void SeparateUpAndDownGains()
        {
            var m = new TerrainResistanceMapper { baselineLevel = 40, uphillLevelsPerGradePercent = 5f, downhillLevelsPerGradePercent = 2f };
            Assert.AreEqual(60, m.LevelForGrade(4f));
            Assert.AreEqual(32, m.LevelForGrade(-4f));
        }
    }

    public class ResistanceRateLimiterTests
    {
        [Test]
        public void FirstLevelIsAlwaysSent()
        {
            var r = new ResistanceRateLimiter();
            Assert.AreEqual(-1, r.LastSentLevel);
            Assert.IsTrue(r.ShouldSend(30, 0.0));
            Assert.AreEqual(30, r.LastSentLevel);
        }

        [Test]
        public void SameLevelIsNotResent()
        {
            var r = new ResistanceRateLimiter();
            r.ShouldSend(30, 0.0);
            for (int i = 1; i < 100; i++) Assert.IsFalse(r.ShouldSend(30, i * 0.1), "no flooding");
        }

        [Test]
        public void AtMostTwoPerSecond()
        {
            var r = new ResistanceRateLimiter();   // 0.5 s
            Assert.IsTrue(r.ShouldSend(30, 0.0));
            Assert.IsFalse(r.ShouldSend(48, 0.2), "too soon");
            Assert.IsFalse(r.ShouldSend(48, 0.49), "too soon");
            Assert.IsTrue(r.ShouldSend(48, 0.5), "held-back change goes out when allowed");
            int sent = 0;
            int level = 0;
            for (int f = 0; f < 600; f++) if (r.ShouldSend(level = 1 - level, 1.0 + f / 60.0)) sent++; // 10 s of changes every frame
            Assert.LessOrEqual(sent, 21);
            Assert.GreaterOrEqual(sent, 19);
        }

        [Test]
        public void MinChangeIsRespected()
        {
            var r = new ResistanceRateLimiter { minChange = 5, minIntervalSeconds = 0f };
            r.ShouldSend(30, 0.0);
            Assert.IsFalse(r.ShouldSend(34, 1.0));
            Assert.IsTrue(r.ShouldSend(35, 1.0));
            r.Reset();
            Assert.IsTrue(r.ShouldSend(35, 1.0), "Reset forgets the last level");
        }

        [Test]
        public void CommandJsonMatchesBridgeProtocol()
        {
            Assert.AreEqual("{\"cmd\":\"resistance\",\"level\":48}", KickrResistanceSender.BuildCommandJson(48));
        }
    }

    public class SpeedEffectsTests
    {
        [Test]
        public void NoEffectsMeansOne()
        {
            Assert.AreEqual(1f, new SpeedEffects().GetMultiplier(0f));
        }

        [Test]
        public void BoostAppliesThenExpires()
        {
            var e = new SpeedEffects();
            e.Trigger("Boost", 1.5f, 3f, 10f);
            Assert.AreEqual(1.5f, e.GetMultiplier(10f), 1e-5f);
            Assert.AreEqual(1.5f, e.GetMultiplier(12.9f), 1e-5f);
            Assert.AreEqual(0.1f, e.Remaining("Boost", 12.9f), 1e-4f);
            Assert.AreEqual(1f, e.GetMultiplier(13f), 1e-5f);
            Assert.IsFalse(e.IsActive("Boost", 13f));
            Assert.AreEqual(0f, e.Remaining("Boost", 20f));
        }

        [Test]
        public void SlowdownAppliesThenExpires()
        {
            var e = new SpeedEffects();
            e.Trigger("Slowdown", 0.6f, 3f, 0f);
            Assert.AreEqual(0.6f, e.GetMultiplier(1f), 1e-5f);
            Assert.AreEqual(1f, e.GetMultiplier(3.01f), 1e-5f);
        }

        [Test]
        public void RetriggerRefreshesTimer()
        {
            var e = new SpeedEffects();
            e.Trigger("Boost", 1.5f, 3f, 0f);
            e.Trigger("Boost", 1.5f, 3f, 2.5f);
            Assert.AreEqual(1.5f, e.GetMultiplier(5f), 1e-5f, "still boosted after the first timer would have ended");
            Assert.AreEqual(1f, e.GetMultiplier(5.6f), 1e-5f);
        }

        [Test]
        public void DifferentKindsStackByMultiplying()
        {
            var e = new SpeedEffects();
            e.Trigger("Boost", 1.5f, 3f, 0f);
            e.Trigger("Slowdown", 0.6f, 1f, 0f);
            Assert.AreEqual(0.9f, e.GetMultiplier(0.5f), 1e-5f);
            Assert.AreEqual(1.5f, e.GetMultiplier(2f), 1e-5f);
        }

        [Test]
        public void ZoneCrossing()
        {
            Assert.IsTrue(SpeedEffects.CrossedInto(67f, 68.1f, 68f));
            Assert.IsTrue(SpeedEffects.CrossedInto(60f, 80f, 68f), "jumping past the zone still counts");
            Assert.IsFalse(SpeedEffects.CrossedInto(68.5f, 69f, 68f), "already inside");
            Assert.IsFalse(SpeedEffects.CrossedInto(500f, 0f, 68f), "lap wrap");
            Assert.IsFalse(SpeedEffects.CrossedInto(60f, 60f, 68f), "standing still");
        }

        [Test]
        public void DefaultZoneMultipliers()
        {
            Assert.AreEqual(1.5f, SpeedZone.DefaultMultiplier(SpeedZoneKind.Boost));
            Assert.AreEqual(0.6f, SpeedZone.DefaultMultiplier(SpeedZoneKind.Slowdown));
        }
    }

    public class KickrGameplaySceneTests
    {
        const string ScenePath = "Assets/Scenes/KickrCubeTest.unity";

        [Test]
        public void SceneHasCourseZonesAndResistanceSender()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var track = Object.FindObjectsByType<KickrTrack>().Single();
            var mover = Object.FindObjectsByType<KickrCubeMover>().Single();
            var sender = Object.FindObjectsByType<KickrResistanceSender>().Single();
            var zones = Object.FindObjectsByType<SpeedZone>();

            Assert.AreEqual(500f, track.profile.Length, 1e-3f);
            Assert.AreSame(track, mover.track);
            Assert.AreSame(track, sender.track);
            Assert.AreSame(mover, sender.mover);
            Assert.AreEqual(5007, sender.port);
            Assert.AreEqual("127.0.0.1", sender.host);
            Assert.AreEqual(30, sender.mapping.baselineLevel);
            Assert.AreEqual(0.5f, sender.rateLimit.minIntervalSeconds, 1e-5f);

            Assert.GreaterOrEqual(zones.Count(z => z.kind == SpeedZoneKind.Boost), 2);
            Assert.GreaterOrEqual(zones.Count(z => z.kind == SpeedZoneKind.Slowdown), 2);
            Assert.AreEqual(zones.Length, mover.zones.Count);
            foreach (var z in zones)
            {
                Assert.That(z.transform.position.z, Is.InRange(0f, 500f));
                float surface = track.HeightAtWorldZ(z.transform.position.z);
                Assert.AreEqual(surface + 1.25f, z.transform.position.y, 0.01f, z.name + " sits on the track");
            }

            // The cube starts on the track surface.
            Assert.AreEqual(track.HeightAtWorldZ(mover.transform.position.z) + mover.heightAboveTrack, mover.transform.position.y, 0.01f);
        }
    }
}
