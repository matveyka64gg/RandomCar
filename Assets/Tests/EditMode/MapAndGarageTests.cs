using System.Collections.Generic;
using NUnit.Framework;
using RandomCar.Car;
using RandomCar.Maps;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Карты (геометрия трасс) и гараж (косметика не меняет физику, каталог полный).
    public sealed class MapAndGarageTests
    {
        static readonly string[] Maps = { "Hills", "City", "Desert", "TelAviv", "Arena" };

        static TrackDefinition Track(string key)
        {
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>($"Assets/Settings/Tracks/Track_{key}.asset");
            Assert.That(def, Is.Not.Null, key);
            return def;
        }

        [Test]
        public void TracksAreDrivable([ValueSource(nameof(Maps))] string key)
        {
            var path = Track(key).Path;
            Assert.That(path.Length, Is.InRange(600f, 4200f), "длина круга");
            float maxGrade = 0f, minRadius = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 a = path.Point(i), b = path.Point((i + 1) % path.Count);
                float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                if (run > 0.1f) maxGrade = Mathf.Max(maxGrade, Mathf.Abs(b.y - a.y) / run);
                float c = Mathf.Abs(path.CurvatureAt(i, 3));
                if (c > 1e-4f) minRadius = Mathf.Min(minRadius, 1f / c);
            }
            TestContext.WriteLine($"{key}: {path.Length:F0} м, уклон до {maxGrade:P1}, мин. радиус {minRadius:F0} м");
            Assert.That(maxGrade, Is.LessThan(0.12f), "уклон без трамплинов");
            Assert.That(minRadius, Is.GreaterThan(12f), "поворот проходим");
        }

        [Test]
        public void GridIsBehindStartLineOnRoad([ValueSource(nameof(Maps))] string key)
        {
            var path = Track(key).Path;
            var seen = new HashSet<Vector3>();
            for (int slot = 0; slot < MapDefinition.MaxGrid; slot++)
            {
                var pose = MapDefinition.GridPose(path, slot);
                float p = path.Nearest(pose.position);
                Assert.That(p, Is.GreaterThan(0.9f), "решётка за линией старта");
                float lateral = Mathf.Abs(Vector3.Dot(pose.position - path.PointAt(p), path.SideAt(p)));
                Assert.That(lateral, Is.LessThan(4.5f), "место на дороге");
                Assert.That(seen.Add(pose.position), Is.True, "места не совпадают");
            }
        }

        [Test]
        public void RoundedPolygonIsClosedAndSmooth()
        {
            var corners = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, -100), new Vector2(0, -100) };
            var pts = TrackShapes.RoundedPolygon(corners, new[] { 20f, 20f, 20f, 20f }, 5f, new[] { 0f, 6f, 0f, -6f });
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                Assert.That(Vector3.Distance(a, b), Is.LessThan(6f), "без разрывов");
            }
        }

        [Test]
        public void CosmeticsDoNotChangePhysics()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>("Assets/Settings/CarCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Cars.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(catalog.Palette.Count, Is.GreaterThanOrEqualTo(16));
            Assert.That(catalog.Parts.FindAll(p => p.Slot == PartSlot.Rims && p.Prefab != null).Count, Is.GreaterThanOrEqualTo(8));
            foreach (var part in catalog.Parts)
                if (part.Prefab != null) Assert.That(part.Prefab.GetComponentsInChildren<Collider>(true), Is.Empty, part.Name);
            foreach (var car in catalog.Cars)
            {
                var cosmetics = car.Prefab.GetComponent<CarCosmetics>();
                Assert.That(cosmetics, Is.Not.Null, car.Key);
                Assert.That(car.Prefab.GetComponent<CarPhysics>().Spec, Is.SameAs(car.Spec), "физика берёт только CarSpec");
                // Детали обвеса — только картинка: без коллайдеров и тел.
                foreach (var kit in cosmetics.Kits)
                {
                    Assert.That(kit.Object.GetComponentsInChildren<Collider>(true), Is.Empty, $"{car.Key} {kit.Slot}.{kit.Variant}");
                    Assert.That(kit.Object.GetComponentsInChildren<Rigidbody>(true), Is.Empty, $"{car.Key} {kit.Slot}.{kit.Variant}");
                }
            }
            // Косметика не хранит ссылок на физику: её поля — только рендеры, объекты и размеры.
            foreach (var field in typeof(CarCosmetics).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(CarSpec)).And.Not.EqualTo(typeof(Rigidbody)), field.Name);
        }

        [Test]
        public void MapCatalogHasAllMaps()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>("Assets/Settings/MapCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            foreach (string key in Maps)
            {
                var entry = catalog.Get(key);
                Assert.That(entry.Key, Is.EqualTo(key));
                Assert.That(System.IO.File.Exists($"Assets/Scenes/{entry.SceneName}.unity"), Is.True, entry.SceneName);
                // Превью — для экрана выбора трасс; полигона испытаний (скрытая карта) там нет.
                if (!entry.Hidden) Assert.That(entry.Preview, Is.Not.Null, "превью для экрана выбора");
            }
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes[0].path, Is.EqualTo("Assets/Scenes/Game.unity"), "первая сцена сборки — Game");
        }
    }
}
