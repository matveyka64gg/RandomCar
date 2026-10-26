using System.Linq;
using NUnit.Framework;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Правила честности событий из AGENTS.md на 10 000 розыгрышей.
    public sealed class EventFairnessTests
    {
        const int Draws = 10000;

        static EventScheduler Scheduler(int seed = 1) => new EventScheduler(EventSettings.DefaultEvents(), seed);

        [Test]
        public void NoTwoHeavyEventsInARow()
        {
            var s = Scheduler();
            EventDefinition previous = null;
            int pairs = 0, heavy = 0;
            for (int i = 0; i < Draws; i++)
            {
                var e = s.Pick();
                if (e.IsHeavy) heavy++;
                if (previous != null && previous.IsHeavy && e.IsHeavy) pairs++;
                previous = e;
            }
            Assert.That(pairs, Is.EqualTo(0));
            Assert.That(heavy, Is.GreaterThan(0), "тяжёлые события вообще выпадают");
        }

        [Test]
        public void BadAndGoodAreFiftyFifty()
        {
            var s = Scheduler(2);
            int bad = 0;
            for (int i = 0; i < Draws; i++) if (!s.Pick().IsGood) bad++;
            float share = bad / (float)Draws;
            Assert.That(share, Is.InRange(0.47f, 0.53f), $"доля плохих {share:P1}");
        }

        [Test]
        public void EveryEventCanHappen()
        {
            var s = Scheduler(3);
            var seen = Enumerable.Range(0, Draws).Select(_ => s.Pick().Id).Distinct().ToList();
            Assert.That(seen.Count, Is.EqualTo(EventSettings.DefaultEvents().Count));
        }

        [Test]
        public void WeightsOfBadAndGoodAreEqual()
        {
            var events = EventSettings.DefaultEvents();
            float bad = events.Where(e => !e.IsGood).Sum(e => e.Weight);
            float good = events.Where(e => e.IsGood).Sum(e => e.Weight);
            Assert.That(bad, Is.EqualTo(good).Within(0.001f));
        }

        [Test]
        public void AllWarningsAreAtLeastMinimum()
        {
            foreach (var e in EventSettings.DefaultEvents())
                Assert.That(e.WarningSeconds, Is.GreaterThanOrEqualTo(FairnessLimits.MinWarningSeconds), e.Id);
        }

        [Test]
        public void TargetsAreUniform()
        {
            var s = Scheduler(4);
            var counts = new int[4];
            for (int i = 0; i < Draws * 4; i++) counts[s.PickTarget(4)]++;
            foreach (int c in counts)
                Assert.That(c / (float)(Draws * 4), Is.InRange(0.23f, 0.27f));
        }

        [Test]
        public void TargetJitterIsWithinOneMetre()
        {
            var s = Scheduler(5);
            for (int i = 0; i < Draws; i++)
                Assert.That(Mathf.Abs(s.LateralJitter()), Is.LessThanOrEqualTo(FairnessLimits.TargetLateralJitter));
        }

        [Test]
        public void ChaosChangesOnlyFrequency()
        {
            var s = Scheduler(6);
            var moderate = new Vector2(20f, 30f);
            var many = new Vector2(8f, 12f);
            Assert.That(float.IsPositiveInfinity(s.NextDelay(ChaosLevel.Off, moderate, many)), Is.True);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(s.NextDelay(ChaosLevel.Moderate, moderate, many), Is.InRange(20f, 30f));
                Assert.That(s.NextDelay(ChaosLevel.Many, moderate, many), Is.InRange(8f, 12f));
            }
        }

        [Test]
        public void HazardZoneIsAtMostHalfTheRoad()
        {
            Assert.That(FairnessLimits.MaxHazardRadius * 2f, Is.LessThanOrEqualTo(FairnessLimits.RoadHalfWidth));
        }

        [Test]
        public void EffectLimitsMatchRules()
        {
            Assert.That(FairnessLimits.MaxSlowSeconds, Is.LessThanOrEqualTo(1.5f));
            Assert.That(FairnessLimits.BoostSeconds, Is.EqualTo(2f));
            Assert.That(FairnessLimits.MaxBoostFactor, Is.LessThanOrEqualTo(1.25f));
            Assert.That(FairnessLimits.MinWarningSeconds, Is.GreaterThanOrEqualTo(1.5f));
        }
    }
}
