using System.Collections.Generic;
using NUnit.Framework;
using RandomCar.Events;

namespace RandomCar.Tests
{
    /// События v3: впереди игрока, на достижимом расстоянии, накал растёт от круга к кругу, полосы чередуются.
    public sealed class EventPacingTests
    {
        const float Length = 2000f;

        static EventPlacer Placer() => new EventPlacer(Length, _ => 0f, new System.Random(1));

        static EventDefinition Def(float warn = 2f, float zone = 10f) => new EventDefinition
        {
            Id = "test", Kind = EventKind.Bad, Class = EventClass.Medium, WarningSeconds = warn, ActiveSeconds = 3f, ZoneLength = zone,
        };

        [Test]
        public void PlacesAheadOfFocusNearDesiredDistance()
        {
            var placer = Placer();
            var focus = new RacerState(0.95f, 30f, true, 0);
            var racers = new List<RacerState> { focus };
            Assert.IsTrue(placer.TryPlaceAhead(Def(), focus, 150f, racers, new List<EventZone>(), out float p));
            float ahead = placer.Ahead(focus.Progress, p);
            Assert.That(ahead, Is.InRange(100f, 220f), "у желаемой дистанции, через старт тоже");
        }

        [Test]
        public void NeverPlacesBehindOrWithinWarningReach()
        {
            var placer = Placer();
            var random = new System.Random(5);
            for (int i = 0; i < 500; i++)
            {
                var focus = new RacerState((float)random.NextDouble(), 10f + (float)random.NextDouble() * 50f, true, 0);
                var other = new RacerState(focus.Progress + 0.03f, 45f, false, 1);
                var racers = new List<RacerState> { focus, other };
                float wanted = 40f + (float)random.NextDouble() * 280f;
                var def = Def();
                if (!placer.TryPlaceAhead(def, focus, wanted, racers, new List<EventZone>(), out float p)) continue;
                float ahead = placer.Ahead(focus.Progress, p);
                Assert.That(ahead, Is.LessThan(Length * 0.45f), "не на другом конце трассы и не позади");
                Assert.IsTrue(placer.IsFair(def, p, racers), "никто не доедет раньше конца предупреждения");
            }
        }

        [Test]
        public void SkipsOccupiedZones()
        {
            var placer = Placer();
            var focus = new RacerState(0.1f, 30f, true, 0);
            float wanted = 150f;
            var zones = new List<EventZone> { new EventZone(0.1f + wanted / Length, 12f) };
            Assert.IsTrue(placer.TryPlaceAhead(Def(), focus, wanted, new List<RacerState> { focus }, zones, out float p));
            Assert.IsFalse(placer.Overlaps(p, 10f, zones));
        }

        [Test]
        public void IntensityGrowsLapByLap()
        {
            float previous = -1f;
            for (int i = 0; i <= 10; i++)
            {
                var intensity = new EventIntensity(i / 10f, ChaosLevel.Many);
                Assert.That(intensity.Level, Is.GreaterThanOrEqualTo(previous));
                previous = intensity.Level;
            }
            var start = new EventIntensity(0f, ChaosLevel.Many);
            var end = new EventIntensity(1f, ChaosLevel.Many);
            Assert.That(end.GapFactor, Is.LessThan(start.GapFactor * 0.75f), "к концу гонки встречи заметно чаще");
            Assert.IsTrue(start.AllowHeavy, "Хаос — только частота: все события, включая тяжёлые, с первых секунд");
            Assert.IsTrue(end.AllowHeavy);
            Assert.That(start.ComboChance, Is.GreaterThan(0f), "комбинации — с первых секунд");
            Assert.That(end.ComboChance, Is.GreaterThan(0.3f));
            Assert.That(end.MaxActive(3), Is.GreaterThan(start.MaxActive(3)));
            Assert.AreEqual(0, end.MaxActive(0), "Хаос выключен — событий нет");
            Assert.That(new EventIntensity(0.5f, ChaosLevel.Many).MaxQuiet, Is.LessThan(new EventIntensity(0.5f, ChaosLevel.Moderate).MaxQuiet));
        }

        [Test]
        public void LanesRotateAndStayOnRoad()
        {
            var lanes = new LanePicker(3);
            float half = 10f, radius = 5f;
            float limit = half - radius;
            int left = 0, right = 0, repeats = 0;
            float last = float.NaN;
            for (int i = 0; i < 600; i++)
            {
                float lateral = lanes.Next(false, 0f, half, radius);
                Assert.That(System.Math.Abs(lateral), Is.LessThanOrEqualTo(limit), "зона целиком на дороге");
                if (lateral < -0.1f) left++;
                if (lateral > 0.1f) right++;
                if (lateral == last) repeats++;
                last = lateral;
            }
            Assert.AreEqual(0, repeats, "одна полоса два раза подряд не выпадает");
            Assert.That(left / (float)right, Is.InRange(0.7f, 1.4f), "не всё время слева или справа");
            float aimed = lanes.Next(true, 2.5f, half, radius);
            Assert.That(aimed, Is.InRange(1.4f, 3.5f), "прицельное событие — на полосе игрока");
        }
    }
}
