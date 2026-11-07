using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Правила честности событий из AGENTS.md на 10 000 розыгрышей: выбор, баланс, тяжёлые, частота.
    public sealed class EventFairnessTests
    {
        const int Draws = 10000;

        /// Обычные события (трюки v5 идут своим потоком и своим планировщиком).
        static List<EventDefinition> Events => EventSettings.DefaultEvents().Where(e => !e.Gag && !e.Spectacle).ToList();

        static EventScheduler Scheduler(int seed = 1) => new EventScheduler(Events, seed);

        [Test]
        public void NoTwoHeavyEventsInARow()
        {
            var s = Scheduler();
            EventDefinition previous = null;
            int pairs = 0, heavy = 0;
            for (int i = 0; i < Draws; i++)
            {
                var e = s.PickAndCommit();
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
            for (int i = 0; i < Draws; i++) if (!s.PickAndCommit().IsGood) bad++;
            float share = bad / (float)Draws;
            Assert.That(share, Is.InRange(0.47f, 0.53f), $"доля плохих {share:P1}");
        }

        [Test]
        public void EveryEventCanHappen()
        {
            var s = Scheduler(3);
            var seen = Enumerable.Range(0, Draws).Select(_ => s.PickAndCommit().Id).Distinct().ToList();
            // События-замены (вес 0) выпадают только вместо других на своих картах.
            Assert.That(seen.Count, Is.EqualTo(Events.Count(e => e.Weight > 0f)));
        }

        [Test]
        public void WeightsOfBadAndGoodAreEqual()
        {
            var events = Events;
            float bad = events.Where(e => !e.IsGood).Sum(e => e.Weight);
            float good = events.Where(e => e.IsGood).Sum(e => e.Weight);
            Assert.That(bad, Is.EqualTo(good).Within(0.001f));
        }

        [Test]
        public void AllWarningsAreAtLeastMinimum()
        {
            foreach (var e in Events)
                Assert.That(e.WarningSeconds, Is.GreaterThanOrEqualTo(FairnessLimits.MinWarningSeconds), e.Id);
            // Трюки v5 — мгновенные: предупреждение короткое, но есть (видно, что сейчас что-то будет).
            foreach (var e in EventSettings.DefaultEvents().Where(e => e.Gag))
                Assert.That(e.WarningSeconds, Is.InRange(0.3f, 1.2f), e.Id);
        }

        [Test]
        public void ChaosSetsFrequencyAndCapacity()
        {
            var s = Scheduler(6);
            var settings = ScriptableObject.CreateInstance<EventSettings>();
            var moderate = settings.ModerateInterval;
            var many = settings.ManyInterval;
            var mayhem = settings.MayhemInterval;
            var absurd = settings.AbsurdInterval;
            Assert.That(float.IsPositiveInfinity(s.NextDelay(ChaosLevel.Off, moderate, many, mayhem, absurd)), Is.True);
            float sumModerate = 0f, sumMany = 0f, sumMayhem = 0f;
            for (int i = 0; i < 1000; i++)
            {
                float a = s.NextDelay(ChaosLevel.Moderate, moderate, many, mayhem, absurd);
                float b = s.NextDelay(ChaosLevel.Many, moderate, many, mayhem, absurd);
                float c = s.NextDelay(ChaosLevel.Mayhem, moderate, many, mayhem, absurd);
                Assert.That(a, Is.InRange(6.5f, 8.5f));
                Assert.That(b, Is.InRange(3.2f, 4.4f));
                Assert.That(c, Is.InRange(1.8f, 2.6f));
                Assert.That(s.NextDelay(ChaosLevel.Absurd, moderate, many, mayhem, absurd), Is.InRange(0.9f, 1.4f));
                sumModerate += a;
                sumMany += b;
                sumMayhem += c;
            }
            Assert.That(sumModerate / 1000f, Is.EqualTo(7.5f).Within(0.3f), "умеренно — на первом круге в среднем раз в 6.5–8.5 с");
            Assert.That(sumMany / 1000f, Is.EqualTo(3.8f).Within(0.25f), "много — на первом круге в среднем раз в 3.2–4.4 с");
            Assert.That(sumMayhem / 1000f, Is.EqualTo(2.2f).Within(0.1f), "безумие — большое событие примерно раз в 2 с");
            // v5.1: в «безумии» с потоком трюков происшествие примерно раз в 0.5–0.8 с, в «абсурде» — каждые 0.2–0.3 с.
            float Rate(ChaosLevel c) => 2f / (settings.Interval(c).x + settings.Interval(c).y) + 2f / (settings.GagInterval(c).x + settings.GagInterval(c).y);
            Assert.That(1f / Rate(ChaosLevel.Mayhem), Is.InRange(0.5f, 0.8f), "безумие: происшествие раз в ~0.6 с");
            Assert.That(1f / Rate(ChaosLevel.Absurd), Is.LessThan(1f / Rate(ChaosLevel.Mayhem)), "абсурд чаще");
            Assert.That(1f / Rate(ChaosLevel.Absurd), Is.InRange(0.18f, 0.3f), "абсурд: каждые ~0.2–0.3 с");
            Assert.That(settings.MaxActive(ChaosLevel.Off), Is.EqualTo(0));
            Assert.That(settings.MaxActive(ChaosLevel.Moderate), Is.InRange(2, 3));
            Assert.That(settings.MaxActive(ChaosLevel.Many), Is.InRange(3, 6));
            Assert.That(settings.MaxActive(ChaosLevel.Mayhem), Is.GreaterThanOrEqualTo(6));
            Assert.That(settings.MaxActive(ChaosLevel.Absurd), Is.GreaterThan(settings.MaxActive(ChaosLevel.Mayhem)));
            Assert.That(settings.MaxGags(ChaosLevel.Absurd), Is.GreaterThanOrEqualTo(15), "много трюков одновременно");
            Assert.That(settings.MaxBodies(ChaosLevel.Mayhem), Is.GreaterThan(settings.MaxBodies(ChaosLevel.Many)));
            Assert.That(settings.MaxBodies(ChaosLevel.Absurd), Is.GreaterThan(settings.MaxBodies(ChaosLevel.Mayhem)));
            Assert.That(settings.MaxBodies(ChaosLevel.Off), Is.EqualTo(0));
            Assert.That(settings.Grace(ChaosLevel.Mayhem), Is.LessThanOrEqualTo(1f), "безумие — с первых секунд");
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void HeavyEventNeverStartsWhileHeavyIsActive()
        {
            var s = Scheduler(7);
            for (int i = 0; i < Draws; i++) Assert.That(s.Pick(heavyActive: true).IsHeavy, Is.False);
        }

        [Test]
        public void BalanceHoldsWhileHeavyIsActive()
        {
            var s = Scheduler(8);
            int bad = 0;
            for (int i = 0; i < Draws; i++) if (!s.Pick(heavyActive: true).IsGood) bad++;
            Assert.That(bad / (float)Draws, Is.InRange(0.47f, 0.53f), "веса плохих растягиваются, 50/50 сохраняется");
        }

        [Test]
        public void NoCarTargetingApiRemains()
        {
            Assert.That(typeof(RaceEvent).GetProperty("Target"), Is.Null, "у события нет цели-машины");
            Assert.That(typeof(EventScheduler).GetMethod("PickTarget"), Is.Null);
            Assert.That(typeof(RacerState).GetField("Laps"), Is.Null, "расстановщик не знает кругов");
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
            Assert.That(FairnessLimits.MinReactionSeconds, Is.GreaterThanOrEqualTo(1f));
        }
    }
}
