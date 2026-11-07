using System.IO;
using System.Linq;
using NUnit.Framework;
using RandomCar.Arena;
using RandomCar.Events;
using RandomCar.Maps;
using RandomCar.Track;
using RandomCar.UI;
using UnityEditor;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Полигон испытаний и экран автора: все события в меню полигона, отдельная скрытая карта, трасса полигона
    /// подходит для каждого события, надпись автора — ровно та, что просил пользователь.
    public sealed class ArenaTests
    {
        [Test]
        public void EveryEventIsInTheArenaMenuWithADescription()
        {
            var settings = AssetDatabase.LoadAssetAtPath<EventSettings>("Assets/Settings/EventSettings.asset");
            var ids = settings.Events.Select(d => d.Id).Concat(EventSettings.DefaultEvents().Select(d => d.Id)).Distinct().ToList();
            Assert.That(ids.Count, Is.GreaterThanOrEqualTo(50), "все события игры");
            foreach (var id in ids)
                Assert.That(ArenaCatalog.Description(id), Is.Not.Empty, $"«{id}»: описание в меню полигона");
            foreach (var c in new[] { ArenaCategory.Hazard, ArenaCategory.Good, ArenaCategory.Gag, ArenaCategory.Show })
                Assert.That(settings.Events.Count(d => ArenaCatalog.Category(d) == c), Is.GreaterThan(0), $"раздел {c} не пустой");
            foreach (var id in new[] { "verity", "eldrifto", "hypergamy" })
                Assert.That(settings.Events.Any(d => d.Id == id), Is.True, $"шоу «{id}» в меню");
            var hypergamy = settings.Events.First(d => d.Id == "hypergamy");
            Assert.That(ArenaCatalog.Hint(hypergamy), Does.Contain("Коснуться"), "у события-касания есть ручной запуск");
        }

        [Test]
        public void ArenaIsASeparateHiddenMap()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>("Assets/Settings/MapCatalog.asset");
            Assert.That(catalog.ArenaKey, Is.EqualTo("Arena"));
            var arena = catalog.Maps.First(m => m.Key == "Arena");
            Assert.That(arena.Hidden, Is.True, "не трасса для гонок");
            Assert.That(catalog.RaceMaps.Any(m => m.Hidden), Is.False, "в выборе трасс и лобби полигона нет");
            Assert.That(catalog.RaceMaps.Count, Is.EqualTo(catalog.Maps.Count - 1));
            Assert.That(catalog.Get("нет-такой").Hidden, Is.False, "неизвестный ключ не ведёт на полигон");
            Assert.That(File.Exists($"Assets/Scenes/{arena.SceneName}.unity"), Is.True);
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path == $"Assets/Scenes/{arena.SceneName}.unity" && s.enabled), Is.True, "сцена в сборке");
        }

        [Test]
        public void ArenaTrackFitsEveryEvent()
        {
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>("Assets/Settings/Tracks/Track_Arena.asset");
            Assert.That(def, Is.Not.Null);
            var path = def.Path;
            float widest = EventSettings.DefaultEvents().Max(d => d.MinRoadHalf);
            Assert.That(path.HalfWidth, Is.GreaterThanOrEqualTo(widest), "дорога не уже, чем нужно самому широкому событию (развилка)");
            // Зона испытаний — прямая: от места старта машины 200 м вперёд дорога почти не поворачивает.
            float start = path.Nearest(MapDefinition.GridPose(path, 0).position);
            Vector3 dir0 = path.DirectionAt(start);
            for (float d = 0f; d <= 200f; d += 10f)
            {
                Vector3 dir = path.DirectionAt(TrackPath.Wrap(start + d / path.Length));
                Assert.That(Vector3.Angle(dir0, dir), Is.LessThan(3f), $"прямая на {d} м");
            }
        }

        [Test]
        public void AuthorScreenShowsExactlyTheRequestedLine()
        {
            Assert.That(GameUI.AuthorLine, Is.EqualTo("Игра сделана специально для \"Вакханалии\" и Олега А."));
            Assert.That(GameUI.AuthorSeconds, Is.InRange(2f, 6f), "несколько секунд");
        }
    }
}
