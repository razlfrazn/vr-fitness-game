using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Kickr.Tests
{
    /// <summary>Records every level "sent to the bridge". Nothing reaches the real trainer.</summary>
    public class MockResistanceSender : IResistanceCommandSender
    {
        public readonly List<int> Sent = new List<int>();
        public int Last => Sent.Count > 0 ? Sent[Sent.Count - 1] : -1;
        public void SendResistance(int level) => Sent.Add(level);
    }

    public class ZoneResistanceTests
    {
        MockResistanceSender _bridge;
        ResistanceController _c;

        [SetUp]
        public void SetUp()
        {
            _bridge = new MockResistanceSender();
            // Defaults: baseline 30, 3 levels per % grade, change >= 1, at most 2 Hz, offsets 20/20.
            _c = new ResistanceController(new TerrainResistanceMapper(), new ResistanceRateLimiter(), new ZoneResistance(), _bridge);
        }

        // --- defaults and pure mapping ---

        [Test]
        public void DefaultOffsetsAreTwenty()
        {
            var z = new ZoneResistance();
            Assert.AreEqual(20, z.slowdownOffset);
            Assert.AreEqual(20, z.boostOffset);
        }

        [Test]
        public void OffsetsClampTo0And100()
        {
            var z = new ZoneResistance();
            Assert.AreEqual(100, z.Apply(90, true), "red clamps at 100");
            Assert.AreEqual(100, z.Apply(100, true));
            Assert.AreEqual(0, z.Apply(6, false, true), "green clamps at 0");
            Assert.AreEqual(0, z.Apply(0, false, true));
            z.slowdownOffset = 500; z.boostOffset = -50;   // bad inspector values
            Assert.AreEqual(100, z.Apply(10, true), "offset itself clamped to 100");
            Assert.AreEqual(10, z.Apply(10, false, true), "negative offset clamped to 0");
        }

        [Test]
        public void NoZoneMeansTerrainLevel()
        {
            Assert.AreEqual(48, new ZoneResistance().Apply(48, false, false));
        }

        // --- red (slowdown) ---

        [Test]
        public void RedEnterRaisesByOffset()
        {
            _c.Tick(0f, false, 0.0);
            _c.Tick(0f, true, 1.0);
            CollectionAssert.AreEqual(new[] { 30, 50 }, _bridge.Sent);
        }

        [Test]
        public void RedLeaveRestoresTerrainLevel()
        {
            _c.Tick(0f, false, 0.0);
            _c.Tick(0f, true, 1.0);
            _c.Tick(0f, false, 2.0);
            CollectionAssert.AreEqual(new[] { 30, 50, 30 }, _bridge.Sent);
        }

        [Test]
        public void RedOffsetClampsAt100()
        {
            _c.Tick(25f, false, 0.0);   // 30 + 75 = 105 -> 100
            _c.Tick(25f, true, 1.0);    // already 100: nothing new to send
            _c.Tick(20f, false, 2.0);   // 90
            _c.Tick(20f, true, 3.0);    // 110 -> 100
            CollectionAssert.AreEqual(new[] { 100, 90, 100 }, _bridge.Sent);
        }

        [Test]
        public void RedCombinesWithUphillAndDownhill()
        {
            _c.Tick(10f, false, 0.0);   // 60
            _c.Tick(10f, true, 1.0);    // 80
            _c.Tick(10f, false, 2.0);   // 60
            _c.Tick(-8f, false, 3.0);   // 6
            _c.Tick(-8f, true, 4.0);    // 26
            _c.Tick(-8f, false, 5.0);   // 6
            CollectionAssert.AreEqual(new[] { 60, 80, 60, 6, 26, 6 }, _bridge.Sent);
        }

        // --- green (boost) ---

        [Test]
        public void GreenEnterLowersByOffset()
        {
            _c.Tick(0f, false, false, 0.0);
            _c.Tick(0f, false, true, 1.0);
            CollectionAssert.AreEqual(new[] { 30, 10 }, _bridge.Sent);
        }

        [Test]
        public void GreenLeaveRestoresTerrainLevel()
        {
            _c.Tick(0f, false, false, 0.0);
            _c.Tick(0f, false, true, 1.0);
            _c.Tick(0f, false, false, 2.0);
            CollectionAssert.AreEqual(new[] { 30, 10, 30 }, _bridge.Sent);
        }

        [Test]
        public void GreenOnClimbExample_60_40_60()
        {
            _c.Tick(10f, false, false, 0.0);
            _c.Tick(10f, false, true, 1.0);
            _c.Tick(10f, false, false, 2.0);
            CollectionAssert.AreEqual(new[] { 60, 40, 60 }, _bridge.Sent);
        }

        [Test]
        public void GreenClampsAt0()
        {
            _c.Tick(-8f, false, false, 0.0);  // 6
            _c.Tick(-8f, false, true, 1.0);   // 6 - 20 -> 0
            _c.Tick(-8f, false, false, 2.0);  // 6
            CollectionAssert.AreEqual(new[] { 6, 0, 6 }, _bridge.Sent);
        }

        [Test]
        public void GreenCombinesWithDownhill()
        {
            _c.Tick(-3f, false, false, 0.0);  // 21
            _c.Tick(-3f, false, true, 1.0);   // 1
            _c.Tick(-3f, false, false, 2.0);  // 21
            CollectionAssert.AreEqual(new[] { 21, 1, 21 }, _bridge.Sent);
        }

        // --- edges vs the rate limiter ---

        [Test]
        public void RateLimiterDoesNotDropRedEnter()
        {
            _c.Tick(0f, false, 0.0);    // 30 sent at t = 0
            _c.Tick(0f, true, 0.1);     // inside the 0.5 s window: held
            _c.Tick(0f, true, 0.3);
            CollectionAssert.AreEqual(new[] { 30 }, _bridge.Sent);
            Assert.IsTrue(_c.EnterPending);
            _c.Tick(0f, true, 0.5);     // next allowed send
            CollectionAssert.AreEqual(new[] { 30, 50 }, _bridge.Sent);
            Assert.IsFalse(_c.EnterPending);
        }

        [Test]
        public void RateLimiterDoesNotDropGreenEnter()
        {
            _c.Tick(10f, false, false, 0.0);   // 60
            _c.Tick(10f, false, true, 0.2);
            CollectionAssert.AreEqual(new[] { 60 }, _bridge.Sent);
            _c.Tick(10f, false, true, 0.5);
            CollectionAssert.AreEqual(new[] { 60, 40 }, _bridge.Sent);
        }

        [Test]
        public void RestoreIsSentInsideTheWindow()
        {
            _c.Tick(0f, false, 0.0);
            _c.Tick(0f, true, 1.0);     // 50 at t = 1.0
            _c.Tick(0f, false, 1.1);    // only 0.1 s later: still sent
            CollectionAssert.AreEqual(new[] { 30, 50, 30 }, _bridge.Sent);

            _c.Tick(10f, false, false, 3.0);   // 60
            _c.Tick(10f, false, true, 4.0);    // 40
            _c.Tick(10f, false, false, 4.05);  // 60, inside the window
            CollectionAssert.AreEqual(new[] { 30, 50, 30, 60, 40, 60 }, _bridge.Sent);
        }

        [Test]
        public void RestoreUsesTheCurrentGradeNotAStaleLevel()
        {
            _c.Tick(0f, false, 0.0);    // 30
            _c.Tick(0f, true, 1.0);     // 50
            _c.Tick(6f, true, 2.0);     // grade changed inside the zone: 48 + 20 = 68
            _c.Tick(10f, false, 2.1);   // leave on a steeper bit: restore 60, not 30 or 48
            CollectionAssert.AreEqual(new[] { 30, 50, 68, 60 }, _bridge.Sent);
        }

        [Test]
        public void LeavingBeforeTheZoneLevelWentOutSendsNothingStale()
        {
            _c.Tick(0f, false, 0.0);    // 30
            _c.Tick(0f, true, 0.1);     // held
            _c.Tick(0f, false, 0.3);    // left before it could go out: trainer is already at 30
            _c.Tick(0f, false, 1.0);
            _c.Tick(0f, false, 2.0);
            CollectionAssert.AreEqual(new[] { 30 }, _bridge.Sent);
            Assert.IsFalse(_c.EnterPending);
        }

        [Test]
        public void ReEntryAfterRestoreIsHeldThenSent()
        {
            _c.Tick(0f, false, 0.0);
            _c.Tick(0f, true, 1.0);     // 50
            _c.Tick(0f, false, 1.2);    // 30 (forced)
            _c.Tick(0f, true, 1.3);     // re-enter: held until 0.5 s after the restore
            _c.Tick(0f, true, 1.6);
            CollectionAssert.AreEqual(new[] { 30, 50, 30 }, _bridge.Sent);
            _c.Tick(0f, true, 1.75);
            CollectionAssert.AreEqual(new[] { 30, 50, 30, 50 }, _bridge.Sent);
        }

        [Test]
        public void RedThenGreenInSequence()
        {
            _c.Tick(0f, false, false, 0.0);   // 30
            _c.Tick(0f, true, false, 1.0);    // red 50
            _c.Tick(0f, false, false, 1.2);   // 30
            _c.Tick(10f, false, false, 2.0);  // climb 60
            _c.Tick(10f, false, true, 3.0);   // green 40
            _c.Tick(10f, false, false, 3.1);  // 60
            CollectionAssert.AreEqual(new[] { 30, 50, 30, 60, 40, 60 }, _bridge.Sent);
        }

        [Test]
        public void SwitchingStraightFromRedToGreenIsNotLost()
        {
            _c.Tick(0f, false, false, 0.0);   // 30
            _c.Tick(0f, true, false, 1.0);    // 50
            _c.Tick(0f, false, true, 1.2);    // touching zones: green held until 1.5
            CollectionAssert.AreEqual(new[] { 30, 50 }, _bridge.Sent);
            _c.Tick(0f, false, true, 1.5);    // 10
            _c.Tick(0f, false, false, 1.6);   // 30
            CollectionAssert.AreEqual(new[] { 30, 50, 10, 30 }, _bridge.Sent);
        }

        [Test]
        public void ZoneLookupHandlesOverlapsAndBothColours()
        {
            var made = new List<GameObject>();
            SpeedZone Make(SpeedZoneKind kind, float centreZ)
            {
                var go = new GameObject("zone");
                made.Add(go);
                go.transform.position = new Vector3(0f, 0f, centreZ);
                var z = go.AddComponent<SpeedZone>();
                z.kind = kind;
                z.length = 4f;
                return z;
            }
            try
            {
                var zones = new List<SpeedZone> { Make(SpeedZoneKind.Slowdown, 10f), Make(SpeedZoneKind.Slowdown, 12f), Make(SpeedZoneKind.Boost, 30f) };
                Assert.IsTrue(ZoneResistance.InsideAnySlowdown(zones, 8f), "start is inside");
                Assert.IsTrue(ZoneResistance.InsideAnySlowdown(zones, 13f), "overlap region");
                Assert.IsFalse(ZoneResistance.InsideAnySlowdown(zones, 14f), "end is outside");
                Assert.IsFalse(ZoneResistance.InsideAnyBoost(zones, 11f));
                Assert.IsTrue(ZoneResistance.InsideAnyBoost(zones, 30f));
                Assert.IsFalse(ZoneResistance.InsideAnySlowdown(zones, 30f));

                var r = new ZoneResistance();
                Assert.AreEqual(50, r.Apply(30, ZoneResistance.InsideAnySlowdown(zones, 11f)), "overlapping red zones don't stack");
                Assert.AreEqual(30, r.Apply(30, true, true), "red and green together cancel out");
            }
            finally
            {
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ForcedSendsKeepTheRateLimiterInSync()
        {
            _c.Tick(0f, false, 0.0);
            _c.Tick(0f, true, 1.0);
            _c.Tick(0f, false, 1.1);
            Assert.AreEqual(30, _c.RateLimit.LastSentLevel);
            _c.Tick(6f, false, 1.3);    // normal terrain change waits for the window after the restore
            CollectionAssert.AreEqual(new[] { 30, 50, 30 }, _bridge.Sent);
            _c.Tick(6f, false, 1.6);
            CollectionAssert.AreEqual(new[] { 30, 50, 30, 48 }, _bridge.Sent);
        }
    }
}
