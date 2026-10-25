using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NUnit.Framework;
using UnityEngine;

namespace Kickr.Tests
{
    /// <summary>Boost gems, boost count, A-button edge, boost resistance and the UDP 5006 button JSON. No hardware.</summary>
    public class BoostGemLayoutTests
    {
        // V1 zones (4 m long): green 70, 260, 470; red 160, 350.
        static readonly List<Vector2> V1Zones = new List<Vector2>
        {
            new Vector2(68, 72), new Vector2(158, 162), new Vector2(258, 262), new Vector2(348, 352), new Vector2(468, 472),
        };

        static List<float> V1Gems() => BoostGemLayout.Generate(TrackProfile.CreateV1(), V1Zones, 10, 4f, 2f);

        [Test]
        public void TenGemsPerLapRoughlyEvery50m()
        {
            var gems = V1Gems();
            Assert.AreEqual(10, gems.Count);
            CollectionAssert.AreEqual(new[] { 31f, 64f, 97.5f, 154f, 219f, 254f, 286.5f, 344f, 407f, 464f }, gems);
            for (int i = 1; i < gems.Count; i++)
            {
                float gap = gems[i] - gems[i - 1];
                Assert.That(gap, Is.InRange(25f, 70f), $"gap {i}");
            }
            Assert.That(gems[0], Is.GreaterThan(0f));
            Assert.That(gems[gems.Count - 1], Is.LessThan(500f));
        }

        [Test]
        public void GemsStayClearOfZonesAndTwoSitOnEachClimb()
        {
            var gems = V1Gems();
            foreach (var g in gems) Assert.IsTrue(BoostGemLayout.IsClear(g, V1Zones, 4f), $"gem at {g} too close to a zone");
            Assert.That(gems.FindAll(g => g >= 40f && g < 100f).Count, Is.GreaterThanOrEqualTo(2), "Climb 1 (40-100 m)");
            Assert.That(gems.FindAll(g => g >= 230f && g < 290f).Count, Is.GreaterThanOrEqualTo(2), "Climb 2 (230-290 m)");
        }

        [Test]
        public void GemCountIsASetting()
        {
            Assert.AreEqual(5, BoostGemLayout.Generate(TrackProfile.CreateV1(), V1Zones, 5).Count);
            Assert.AreEqual(0, BoostGemLayout.Generate(TrackProfile.CreateV1(), V1Zones, 0).Count);
        }
    }

    public class BoostGemTrackerTests
    {
        static BoostGemTracker V1()
        {
            var t = new BoostGemTracker(new[] { 31f, 64f, 97.5f, 154f, 219f, 254f, 286.5f, 344f, 407f, 464f }, 500f);
            t.Reset(0);
            return t;
        }

        [Test]
        public void CollectsAGemWhenDistanceCrossesIt()
        {
            var t = V1();
            Assert.AreEqual(0, t.Advance(30.9));
            Assert.AreEqual(1, t.Advance(31.2));
            Assert.IsTrue(t.IsCollected(0));
            Assert.AreEqual(1, t.CollectedThisLap);
        }

        [Test]
        public void BigStepNeverSkipsAGem()
        {
            var t = V1();
            var got = new List<GemPickup>();
            Assert.AreEqual(10, t.Advance(499.0, got), "one 499 m step collects all ten");
            Assert.AreEqual(10, got.Count);
            Assert.AreEqual(10, t.TotalCollected);
        }

        [Test]
        public void LapWrapAt500CollectsTheNextLapsGemsAndRespawns()
        {
            var t = V1();
            Assert.AreEqual(10, t.Advance(490));
            Assert.AreEqual(1, t.Advance(540), "only the 31 m gem of lap 2");
            Assert.AreEqual(1, t.CurrentLap);
            Assert.AreEqual(1, t.CollectedThisLap);
            Assert.IsTrue(t.IsCollected(0));
            Assert.IsFalse(t.IsCollected(9), "464 m gem respawned on lap 2");
            Assert.AreEqual(9, t.Advance(964.5), "the other nine of lap 2; the 31 m gem is not collected again");
            Assert.AreEqual(10, t.CollectedThisLap);
            Assert.AreEqual(20, t.TotalCollected);
        }

        [Test]
        public void MultiLapStepCountsEveryLap()
        {
            var t = V1();
            Assert.AreEqual(25, t.Advance(1250), "10 + 10 + 5");
            Assert.AreEqual(2, t.CurrentLap);
            Assert.AreEqual(5, t.CollectedThisLap);
        }

        [Test]
        public void NeverCollectedTwicePerLap()
        {
            var t = V1();
            Assert.AreEqual(1, t.Advance(40));
            Assert.AreEqual(0, t.Advance(40));
            Assert.AreEqual(0, t.Advance(40.0001));
            Assert.AreEqual(1, t.Advance(70));
            Assert.AreEqual(2, t.TotalCollected);
        }

        [Test]
        public void GoingBackwardsResetsAndRespawns()
        {
            var t = V1();
            t.Advance(100);
            Assert.AreEqual(0, t.Advance(0), "cube reset to the start");
            Assert.AreEqual(0, t.CollectedThisLap);
            Assert.AreEqual(1, t.Advance(32));
        }
    }

    public class BoostInventoryTests
    {
        [Test]
        public void StartsAtTwoGemsAddOneUseTakesOne()
        {
            var inv = new BoostInventory(new BoostSettings());
            Assert.AreEqual(2, inv.Count);
            inv.AddGems(1);
            Assert.AreEqual(3, inv.Count);
            Assert.IsTrue(inv.TryActivate(0));
            Assert.AreEqual(2, inv.Count);
            Assert.AreEqual(1, inv.Used);
        }

        [Test]
        public void GemValueAndStartingCountAreSettings()
        {
            var inv = new BoostInventory(new BoostSettings { startingBoosts = 0, gemValue = 3 });
            Assert.AreEqual(0, inv.Count);
            inv.AddGems(2);
            Assert.AreEqual(6, inv.Count);
        }

        [Test]
        public void DoesNothingAtZeroOrWhileActive()
        {
            var inv = new BoostInventory(new BoostSettings { startingBoosts = 1, boostDuration = 10f });
            Assert.IsTrue(inv.TryActivate(0));
            inv.AddGems(1);
            Assert.IsFalse(inv.TryActivate(5), "boost already active");
            Assert.AreEqual(1, inv.Count);
            Assert.IsTrue(inv.IsActive(9.9));
            Assert.AreEqual(5f, inv.Remaining(5), 1e-4);
            Assert.IsFalse(inv.IsActive(10));
            Assert.IsTrue(inv.TryActivate(10));
            Assert.AreEqual(0, inv.Count);
            Assert.IsFalse(inv.TryActivate(25), "count is 0");
            Assert.AreEqual(2, inv.Used);
        }

        [Test]
        public void HeldPressUsesOnlyOneBoost()
        {
            var inv = new BoostInventory(new BoostSettings { startingBoosts = 3, boostDuration = 1f });
            var edge = new ButtonEdge();
            for (int frame = 0; frame < 300; frame++)       // held for 5 s at 60 fps (boost ends after 1 s)
                if (edge.Update(true)) inv.TryActivate(frame / 60.0);
            Assert.AreEqual(1, inv.Used);
            Assert.AreEqual(2, inv.Count);
            edge.Update(false);
            Assert.IsTrue(edge.Update(true), "new press after release");
        }
    }

    public class BoostResistanceTests
    {
        class Recorder : IResistanceCommandSender
        {
            public readonly List<int> Sent = new List<int>();
            public void SendResistance(int level) => Sent.Add(level);
        }

        [Test]
        public void ResistanceDrops15AndReturnsAfter10s()
        {
            var bridge = new Recorder();
            var c = new ResistanceController(null, null, null, bridge);
            var inv = new BoostInventory(new BoostSettings());
            c.Tick(0f, false, false, inv.ResistanceReduction(0), 0);        // flat: 30
            Assert.IsTrue(inv.TryActivate(1.0));
            for (double t = 1.0; t < 11.0; t += 0.1) c.Tick(0f, false, false, inv.ResistanceReduction(t), t);
            Assert.AreEqual(15, c.TargetLevel, "30 - 15 while boosted");
            c.Tick(0f, false, false, inv.ResistanceReduction(11.05), 11.05);  // boost over
            CollectionAssert.AreEqual(new[] { 30, 15, 30 }, bridge.Sent);
            Assert.AreEqual(0, c.BoostReduction);
        }

        [Test]
        public void RestoreIsSentImmediatelyEvenInsideTheRateLimitWindow()
        {
            var bridge = new Recorder();
            var c = new ResistanceController(null, null, null, bridge);
            c.Tick(0f, false, false, 0, 0.0);
            c.Tick(0f, false, false, 15, 0.6);   // boost start (next allowed send)
            c.Tick(0f, false, false, 0, 0.7);    // ends 0.1 s later
            CollectionAssert.AreEqual(new[] { 30, 15, 30 }, bridge.Sent);
        }

        [Test]
        public void CombinesWithZonesAndTerrainAndClampsAtZero()
        {
            var zone = new ZoneResistance();
            Assert.AreEqual(45, zone.Apply(60, false, false, 15), "+10 % climb 60 - 15");
            Assert.AreEqual(25, zone.Apply(60, false, true, 15), "green on the climb 60 - 20 - 15");
            Assert.AreEqual(35, zone.Apply(30, true, false, 15), "red on the flat 30 + 20 - 15");
            Assert.AreEqual(0, zone.Apply(6, false, false, 15), "-8 % descent 6 - 15 clamps to 0");
            Assert.AreEqual(0, zone.Apply(6, false, true, 15), "never below 0");
            Assert.AreEqual(100, zone.Apply(100, true, false, 0));
        }

        [Test]
        public void BoostEndingInsideARedZoneGoesBackToTheZoneLevel()
        {
            var bridge = new Recorder();
            var c = new ResistanceController(null, null, null, bridge);
            c.Tick(0f, false, false, 15, 0.0);   // boosted on the flat: 15
            c.Tick(0f, true, false, 15, 0.6);    // red while boosted: 30 + 20 - 15 = 35
            c.Tick(0f, true, false, 0, 1.2);     // boost over, still red: 50
            c.Tick(0f, false, false, 0, 1.3);    // leave red: 30 at once
            CollectionAssert.AreEqual(new[] { 15, 35, 50, 30 }, bridge.Sent);
        }
    }

    public class ZwiftButtonMessageTests
    {
        [Test]
        public void ParsesTheShortForm()
        {
            Assert.IsTrue(ZwiftButtonMessage.TryParse("{\"btn\":\"A\",\"side\":\"right\",\"pressed\":true}", out var e));
            Assert.AreEqual("A", e.Button);
            Assert.AreEqual("right", e.Side);
            Assert.IsTrue(e.Pressed);
        }

        [Test]
        public void ParsesTheListenersFullForm()
        {
            string json = "{\"kind\": \"release\", \"side\": \"right\", \"unit\": \"LEFT\", \"address\": \"CC:A3:D1:BA:46:D6\", " +
                          "\"device\": \"Ride\", \"btn\": \"A\", \"button\": \"A\", \"pressed\": false, \"raw\": \"23 08\", \"t\": 1761400000.5}";
            Assert.IsTrue(ZwiftButtonMessage.TryParse(json, out var e));
            Assert.AreEqual("A", e.Button);
            Assert.AreEqual("right", e.Side);
            Assert.AreEqual("LEFT", e.Unit);
            Assert.IsFalse(e.Pressed);
        }

        [Test]
        public void OlderListenerFormUsesKindAndButtonSide()
        {
            // Old format: "side" was the unit (the left unit relays the right A).
            Assert.IsTrue(ZwiftButtonMessage.TryParse("{\"kind\":\"press\",\"side\":\"LEFT\",\"button\":\"A\"}", out var e));
            Assert.AreEqual("right", e.Side);
            Assert.AreEqual("LEFT", e.Unit);
            Assert.IsTrue(e.Pressed);
        }

        [Test]
        public void IgnoresNonButtonMessagesAndGarbage()
        {
            Assert.IsFalse(ZwiftButtonMessage.TryParse("{\"kind\":\"battery\",\"percent\":80}", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("{\"kind\":\"state\",\"held\":[\"A\"]}", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("not json", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("", out _));
        }

        [Test]
        public void PhysicalSides()
        {
            Assert.AreEqual("right", ZwiftButtonMessage.PhysicalSide("A"));
            Assert.AreEqual("right", ZwiftButtonMessage.PhysicalSide("RIGHT_POWERUP"));
            Assert.AreEqual("left", ZwiftButtonMessage.PhysicalSide("DPAD_UP"));
            Assert.AreEqual("left", ZwiftButtonMessage.PhysicalSide("LEFT_SHIFT_DOWN"));
            Assert.AreEqual("", ZwiftButtonMessage.PhysicalSide("ANALOG_UP"));
        }

        [Test]
        public void RelayedDuplicateFromBothUnitsIsOneHeldButton()
        {
            var s = new ZwiftButtonState();
            var edge = new ButtonEdge();
            s.Apply(new ZwiftButtonEvent { Button = "A", Side = "right", Unit = "LEFT", Pressed = true });
            Assert.IsTrue(edge.Update(s.IsHeld("A", "right")));
            s.Apply(new ZwiftButtonEvent { Button = "A", Side = "right", Unit = "RIGHT", Pressed = true });   // ~4 ms later
            Assert.IsFalse(edge.Update(s.IsHeld("A", "right")), "duplicate is not a second press");
            s.Apply(new ZwiftButtonEvent { Button = "A", Side = "right", Unit = "LEFT", Pressed = false });
            Assert.IsTrue(s.IsHeld("A", "right"), "still held on the other unit");
            s.Apply(new ZwiftButtonEvent { Button = "A", Side = "right", Unit = "RIGHT", Pressed = false });
            Assert.IsFalse(s.IsHeld("A", "right"));
            Assert.IsFalse(edge.Update(false));
        }

        [Test]
        public void MappingFiltersBySideAndDisconnectClearsAUnit()
        {
            var s = new ZwiftButtonState();
            s.Apply(new ZwiftButtonEvent { Button = "A", Side = "right", Unit = "RIGHT", Pressed = true });
            Assert.IsFalse(s.IsHeld("A", "left"));
            Assert.IsTrue(s.IsHeld("A", ""));
            Assert.IsTrue(s.IsHeld("a", "any"));
            Assert.IsFalse(s.IsHeld("B", "right"));
            s.ClearUnit("RIGHT");
            Assert.IsFalse(s.IsHeld("A", "right"));
        }
    }

    public class BoostHudLayoutTests
    {
        [Test]
        public void BoostBoxDoesNotOverlapOtherHudBoxes()
        {
            Assert.IsFalse(KickrHud.BoostBox.Overlaps(KickrHud.KickrBox));
            Assert.IsFalse(KickrHud.BoostBox.Overlaps(KickrHud.TerrainBox));
            Assert.IsFalse(KickrHud.BoostBox.Overlaps(KickrHud.DistanceBox));
        }
    }

    public class BoostSceneTests
    {
        [Test]
        public void SceneHasTheBoostSystemWiredToTheRideButtons()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/KickrCubeTest.unity", OpenSceneMode.Single);
            var boost = Object.FindObjectsByType<BoostController>().Single();
            var buttons = Object.FindObjectsByType<ZwiftRideUdpReceiver>().Single();
            var sender = Object.FindObjectsByType<KickrResistanceSender>().Single();
            Assert.AreSame(Object.FindObjectsByType<KickrCubeMover>().Single(), boost.mover);
            Assert.AreSame(Object.FindObjectsByType<KickrTrack>().Single(), boost.track);
            Assert.AreSame(buttons, boost.buttons);
            Assert.AreSame(boost, sender.boost);
            Assert.AreEqual(5006, buttons.port);
            Assert.AreEqual("A", boost.boostButton);
            Assert.AreEqual("right", boost.boostButtonSide);
            Assert.AreEqual(2, boost.settings.startingBoosts);
            Assert.AreEqual(10, boost.settings.gemsPerLap);
            Assert.AreEqual(15, boost.settings.boostResistanceReduction);
            Assert.AreEqual(10f, boost.settings.boostDuration, 1e-5f);
            Assert.AreEqual(1.3f, boost.settings.speedMultiplier, 1e-5f);
            Assert.IsNotNull(boost.gemMaterial);
        }
    }
}
