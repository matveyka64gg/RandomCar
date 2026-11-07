using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RandomCar.Car;
using RandomCar.PowerUps;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Бонусы: виды и тексты, выпадение по месту в гонке, расстановка ящиков, защита после возврата на трассу.
    public sealed class PowerUpTests
    {
        static TrackPath Oval(float halfWidth)
        {
            // «Стадион»: две прямые по 300 м и крутые полукруги радиусом 28 м.
            var points = new List<Vector3>();
            const float r = 28f;
            for (int i = 0; i <= 6; i++) points.Add(new Vector3(r, 0f, -150f + i * 50f));
            for (int i = 1; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI;
                points.Add(new Vector3(Mathf.Cos(a) * r, 0f, 150f + Mathf.Sin(a) * r));
            }
            for (int i = 0; i <= 6; i++) points.Add(new Vector3(-r, 0f, 150f - i * 50f));
            for (int i = 1; i < 8; i++)
            {
                float a = Mathf.PI + i / 8f * Mathf.PI;
                points.Add(new Vector3(Mathf.Cos(a) * r, 0f, -150f + Mathf.Sin(a) * r));
            }
            return new TrackPath(points, 8) { HalfWidth = halfWidth };
        }

        [Test]
        public void EveryItemHasTextsAndIsDistinct()
        {
            var items = PowerUpSettings.DefaultItems();
            Assert.That(items.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(items.Select(i => i.Kind).Distinct().Count(), Is.EqualTo(items.Count), "каждый вид — один раз");
            foreach (var i in items)
            {
                Assert.That(i.Kind, Is.Not.EqualTo(PowerUpKind.None));
                Assert.That(string.IsNullOrWhiteSpace(i.Title), Is.False, i.Kind.ToString());
                Assert.That(string.IsNullOrWhiteSpace(i.Hint), Is.False, i.Kind.ToString());
            }
        }

        [Test]
        public void LeaderNeverRollsLeaderRocketAndTrailersGetMoreBoosts()
        {
            var settings = ScriptableObject.CreateInstance<PowerUpSettings>();
            var random = new System.Random(1);
            int leaderRockets = 0, lastRockets = 0, leaderTurbo = 0, lastTurbo = 0;
            for (int i = 0; i < 20000; i++)
            {
                var front = settings.Roll(0, 6, random.NextDouble());
                var back = settings.Roll(5, 6, random.NextDouble());
                if (front == PowerUpKind.LeaderRocket) leaderRockets++;
                if (back == PowerUpKind.LeaderRocket) lastRockets++;
                if (front == PowerUpKind.Turbo) leaderTurbo++;
                if (back == PowerUpKind.Turbo) lastTurbo++;
            }
            Assert.That(leaderRockets, Is.EqualTo(0), "лидеру ракета на лидера не выпадает");
            Assert.That(lastRockets, Is.InRange(200, 1200), "последним — редко, но выпадает");
            Assert.That(lastTurbo, Is.GreaterThan(leaderTurbo), "отстающим турбо чаще");
            // Все виды выпадают.
            var seen = new HashSet<PowerUpKind>();
            for (int i = 0; i < 5000; i++) seen.Add(settings.Roll(random.Next(6), 6, random.NextDouble()));
            Assert.That(seen.Count, Is.EqualTo(settings.Items.Count));
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void BoxesCoverTheTrackInRowsOnTheRoad()
        {
            var course = Oval(6f);
            var places = PowerUpLayout.Build(course, 120f);
            int rows = places.Select(p => p.Row).Distinct().Count();
            Assert.That(rows, Is.GreaterThanOrEqualTo(Mathf.RoundToInt(course.Length / 120f) - 1), "ряды через ~120 м");
            foreach (var p in places)
            {
                Assert.That(Mathf.Abs(p.Lateral), Is.LessThanOrEqualTo(course.HalfWidth - 1f), "ящик на дороге");
                float d = p.Progress * course.Length;
                Assert.That(d, Is.GreaterThan(25f).And.LessThan(course.Length - 25f), "не на стартовой линии");
            }
            foreach (var row in places.GroupBy(p => p.Row)) Assert.That(row.Count(), Is.InRange(3, 5), "в ряду 3–5 ящиков");
            // Разные места: и прямые, и входы в повороты.
            Assert.That(places.Any(p => p.Spot == BoxSpot.BeforeTurn), Is.True);
            Assert.That(places.Any(p => p.Spot == BoxSpot.Straight), Is.True);
            // Широкая дорога — больше ящиков в ряду.
            Assert.That(PowerUpLayout.Build(Oval(11f), 120f).GroupBy(p => p.Row).First().Count(), Is.GreaterThan(3));
        }

        [Test]
        public void RespawnProtectionLastsTwoSecondsAndBlocksLaunches()
        {
            Assert.That(CarController.RespawnProtection, Is.EqualTo(2f));
            var go = new GameObject("car");
            go.AddComponent<Rigidbody>();
            var car = go.AddComponent<CarController>();
            car.Protect(CarController.RespawnProtection);
            Assert.IsTrue(car.Invulnerable);
            Assert.IsTrue(car.Protected);
            Assert.That(car.ProtectionLeft, Is.EqualTo(2f).Within(0.05f));
            int launches = car.Launches;
            car.ApplyLaunch(Vector3.up * 25f, Vector3.right * 6f, "мина");
            car.ApplyImpulse(Vector3.right * 10f);
            Assert.That(car.Launches, Is.EqualTo(launches), "подброс под защитой не засчитан и не применён");
            Assert.That(car.Absorbed, Is.EqualTo(2), "защита поглотила оба воздействия");
            Object.DestroyImmediate(go);
        }
    }
}
