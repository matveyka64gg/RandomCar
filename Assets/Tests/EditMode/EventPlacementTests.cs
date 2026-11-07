using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RandomCar.Events;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Расстановка событий на трассе: честное предупреждение для всех, нет цели и нет предпочтения
    /// лидера или отстающего, зоны не накладываются, «прямые» события — на прямых.
    public sealed class EventPlacementTests
    {
        const int Trials = 4000;

        static TrackPath path;
        static TrackPath Path => path ??= TrackLayout.CreatePath();

        static EventPlacer Placer(int seed)
        {
            var p = Path;
            return new EventPlacer(p.Length, x => Mathf.Abs(p.CurvatureAt(Mathf.FloorToInt(TrackPath.Wrap(x) * p.Count) % p.Count, 4)), new System.Random(seed));
        }

        static List<RacerState> RandomRacers(System.Random r, int count = 4)
        {
            var list = new List<RacerState>();
            for (int i = 0; i < count; i++) list.Add(new RacerState((float)r.NextDouble(), (float)(r.NextDouble() * 55.0)));
            return list;
        }

        [Test]
        public void EveryPlacedEventGivesAllRacersFullWarning()
        {
            var placer = Placer(1);
            var r = new System.Random(2);
            int placed = 0;
            foreach (var def in EventSettings.DefaultEvents())
                for (int i = 0; i < Trials / 4; i++)
                {
                    var racers = RandomRacers(r);
                    if (!placer.TryPlace(def, racers, null, out var spot)) continue;
                    placed++;
                    foreach (var racer in racers)
                    {
                        float ahead = placer.Ahead(racer.Progress, spot.Progress) - def.ZoneLength * 0.5f;
                        Assert.That(ahead, Is.GreaterThan(0f), "машина не внутри зоны в момент появления");
                        float fastest = Mathf.Max(racer.Speed, FairnessLimits.AssumedMinSpeed) * 1.15f;
                        Assert.That(ahead / fastest, Is.GreaterThanOrEqualTo(def.WarningSeconds + FairnessLimits.MinReactionSeconds - 1e-3f),
                            $"{def.Id}: опасность не появляется под машиной");
                    }
                }
            Assert.That(placed, Is.GreaterThan(Trials / 2), "место обычно находится");
        }

        [Test]
        public void NoRacerIsPreferredByPositionInRace()
        {
            // Четыре машины на равных расстояниях и с равной скоростью; «лидер» по кругам — первая.
            // Кто первым доедет до события, должно выпадать примерно поровну: позиция в гонке не учитывается.
            var placer = Placer(3);
            var counts = new int[4];
            var def = EventSettings.DefaultEvents().First(d => d.Id == "worms");
            var rng = new System.Random(4);
            int placed = 0;
            for (int i = 0; i < Trials; i++)
            {
                float offset = (float)rng.NextDouble();
                var racers = Enumerable.Range(0, 4).Select(k => new RacerState(TrackPath.Wrap(offset + k * 0.25f), 35f)).ToList();
                if (!placer.TryPlace(def, racers, null, out var spot)) continue;
                placed++;
                int first = 0;
                float best = float.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    float d = placer.Ahead(racers[k].Progress, spot.Progress);
                    if (d < best) { best = d; first = k; }
                }
                counts[first]++;
            }
            foreach (int c in counts)
                Assert.That(c / (float)placed, Is.InRange(0.2f, 0.3f), string.Join(", ", counts));
        }

        [Test]
        public void ZonesDoNotOverlap()
        {
            var placer = Placer(5);
            var r = new System.Random(6);
            var defs = EventSettings.DefaultEvents();
            for (int i = 0; i < Trials; i++)
            {
                var racers = RandomRacers(r);
                var active = new List<EventZone>();
                for (int k = 0; k < 3; k++)
                {
                    var def = defs[r.Next(defs.Count)];
                    if (!placer.TryPlace(def, racers, active, out var spot)) continue;
                    foreach (var z in active)
                    {
                        float d = Mathf.Min(placer.Ahead(z.Progress, spot.Progress), placer.Ahead(spot.Progress, z.Progress));
                        Assert.That(d, Is.GreaterThanOrEqualTo((def.ZoneLength + z.Length) * 0.5f + FairnessLimits.ZoneGap - 1e-3f));
                    }
                    active.Add(new EventZone(spot.Progress, def.ZoneLength));
                }
            }
        }

        [Test]
        public void StraightEventsArePlacedOnStraights()
        {
            var placer = Placer(7);
            var r = new System.Random(8);
            foreach (var def in EventSettings.DefaultEvents().Where(d => d.NeedsStraight))
                for (int i = 0; i < 500; i++)
                {
                    if (!placer.TryPlace(def, RandomRacers(r), null, out var spot)) continue;
                    Assert.That(placer.IsStraight(spot.Progress, def.ZoneLength), Is.True, def.Id);
                }
        }

        [Test]
        public void SomeoneReachesTheZoneWhileItIsActive()
        {
            var placer = Placer(9);
            var r = new System.Random(10);
            foreach (var def in EventSettings.DefaultEvents())
                for (int i = 0; i < 300; i++)
                {
                    var racers = RandomRacers(r);
                    if (!placer.TryPlace(def, racers, null, out var spot)) continue;
                    Assert.That(placer.IsRelevant(def, spot.Progress, racers), Is.True, def.Id);
                }
        }

        [Test]
        public void LateralStaysOnRoad()
        {
            var placer = Placer(11);
            var r = new System.Random(12);
            var def = EventSettings.DefaultEvents()[0];
            for (int i = 0; i < Trials; i++)
                if (placer.TryPlace(def, RandomRacers(r), null, out var spot))
                    Assert.That(Mathf.Abs(spot.Lateral), Is.LessThanOrEqualTo(FairnessLimits.RoadHalfWidth - 1.5f + 1e-4f));
        }
    }
}
