using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using RandomCar.Events;
using UnityEditor;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Шоу-события v6: VERITY и EL DRIFTO BUS — определения, свой поток только в «безумии» и «абсурде»,
    /// оригинальная запись крика пользователя, устройство префабов (физика, модель, звук).
    public sealed class SpectacleTests
    {
        const string ClipPath = "Assets/Art/Audio/Sfx/Events/el-drifto.mp3";
        /// SHA-256 файла, который прислал пользователь (D:\el-drifto.mp3): запись не заменена и не изменена.
        const string OriginalSha256 = "6d0fcd160262f447066ecec214a33b14e8708bda42eaa39ee74f1d021fd93b59";

        static EventDefinition Def(string id) => EventSettings.DefaultEvents().FirstOrDefault(d => d.Id == id);

        static readonly string[] ShowIds = { "verity", "eldrifto", "hypergamy" };
        static readonly ChaosLevel[] OnLevels = { ChaosLevel.Moderate, ChaosLevel.Many, ChaosLevel.Mayhem, ChaosLevel.Absurd };

        [Test]
        public void ThreeSpectaclesAreLongOwnStreamAndOutsideTheBalance()
        {
            var shows = EventSettings.DefaultEvents().Where(d => d.Spectacle).ToList();
            Assert.That(shows.Select(d => d.Id), Is.EquivalentTo(ShowIds));
            foreach (var d in shows)
            {
                Assert.That(d.Gag, Is.False, d.Id);
                Assert.That(d.ActiveSeconds, Is.GreaterThanOrEqualTo(8f), $"{d.Id}: успеваешь повзаимодействовать");
                Assert.That(d.Copies, Is.GreaterThanOrEqualTo(1));
                Assert.That(d.Weight, Is.GreaterThan(0f));
                Assert.That(d.MinChaos, Is.LessThanOrEqualTo(ChaosLevel.Moderate), $"{d.Id}: не только для «безумия»");
                Assert.That(d.WarningSeconds, Is.GreaterThanOrEqualTo(1.5f), $"{d.Id}: заметно заранее");
            }
            Assert.That(Def("eldrifto").Bodies, Is.EqualTo(1), "автобус — одно физическое тело в бюджете");
            Assert.That(Def("hypergamy").Sustain, Is.True, "картинка стоит, пока участник не проедет");
            // Баланс обычных событий не меняется: шоу-события в нём не участвуют.
            var regular = EventSettings.DefaultEvents().Where(d => !d.Gag && !d.Spectacle).ToList();
            Assert.That(regular.Where(e => !e.IsGood).Sum(e => e.Weight), Is.EqualTo(regular.Where(e => e.IsGood).Sum(e => e.Weight)).Within(0.001f));
        }

        [Test]
        public void SpectaclesRunOnEveryChaosLevelAndRepeat()
        {
            var settings = ScriptableObject.CreateInstance<EventSettings>();
            try
            {
                Assert.That(settings.SpectaclesOn(ChaosLevel.Off), Is.False, "«выключен» — событий нет совсем");
                foreach (var level in OnLevels)
                {
                    Assert.That(settings.SpectaclesOn(level), Is.True, $"{level}: шоу есть");
                    Assert.That(settings.SpectacleInterval(level).y, Is.LessThanOrEqualTo(60f), $"{level}: несколько шоу за гонку");
                    Assert.That(settings.FirstSpectacle(level), Is.InRange(3f, 20f), $"{level}: первое — в начале гонки");
                    Assert.That(settings.MaxSpectacles(level), Is.GreaterThanOrEqualTo(1));
                }
                // Хаос меняет частоту: чем выше уровень, тем чаще.
                for (int i = 1; i < OnLevels.Length; i++)
                    Assert.That(settings.SpectacleInterval(OnLevels[i]).x, Is.LessThan(settings.SpectacleInterval(OnLevels[i - 1]).x), $"{OnLevels[i]} чаще {OnLevels[i - 1]}");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        /// Настоящий ассет настроек (его читает игра), а не значения по умолчанию в коде: шоу на всех уровнях,
        /// и планировщик потока шоу с теми же фильтрами, что у директора, выбирает каждое из трёх на каждом уровне.
        [Test]
        public void ProjectSettingsPickEverySpectacleOnEveryChaosLevel()
        {
            var settings = AssetDatabase.LoadAssetAtPath<EventSettings>("Assets/Settings/EventSettings.asset");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.Version, Is.GreaterThanOrEqualTo(7), "ассет переведён на правила v7");
            var shows = settings.Events.Where(d => d.Spectacle).ToList();
            Assert.That(shows.Select(d => d.Id), Is.EquivalentTo(ShowIds), "в ассете все три шоу");
            foreach (var level in OnLevels)
            {
                Assert.That(settings.SpectaclesOn(level), Is.True, $"{level}: поток шоу включён в ассете");
                var scheduler = new EventScheduler(shows, 1234 + (int)level);
                var seen = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < 300; i++)
                {
                    // Как EventDirector.UpdateSpectacles: свободный экземпляр есть, тела помещаются в бюджет Хаоса.
                    var pick = scheduler.PickAndCommit(false, d => d.Bodies == 0 || d.Bodies <= settings.MaxBodies(level), true);
                    Assert.That(pick, Is.Not.Null, $"{level}: выбор не пустой");
                    seen[pick.Id] = seen.TryGetValue(pick.Id, out int n) ? n + 1 : 1;
                }
                foreach (var id in ShowIds)
                    Assert.That(seen.TryGetValue(id, out int count) ? count : 0, Is.GreaterThan(40), $"{level}: «{id}» выпадает");
            }
        }

        [Test]
        public void ElDriftoUsesTheOriginalRecordingAndScreamsBeforeTheBus()
        {
            Assert.That(File.Exists(ClipPath), Is.True, "запись в проекте");
            using (var sha = SHA256.Create())
            {
                string hash = string.Concat(sha.ComputeHash(File.ReadAllBytes(ClipPath)).Select(b => b.ToString("x2")));
                Assert.That(hash, Is.EqualTo(OriginalSha256), "файл пользователя без изменений");
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.InRange(1.6f, 2.0f));
            var importer = (AudioImporter)AssetImporter.GetAtPath(ClipPath);
            Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.PCM), "без сжатия с потерями");
            Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad), "звучит без задержки");
            Assert.That(Def("eldrifto").WarningSeconds, Is.GreaterThan(clip.length), "автобус вылетает после крика");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Events/Event_eldrifto.prefab");
            Assert.That(prefab, Is.Not.Null);
            var ev = prefab.GetComponent<ElDriftoEvent>();
            Assert.That(ev.ScreamClip, Is.SameAs(clip), "префаб ссылается именно на эту запись");
            var voice = prefab.transform.Find("Voice").GetComponent<AudioSource>();
            Assert.That(voice.playOnAwake, Is.False, "не звучит сам по себе");
            Assert.That(voice.loop, Is.False, "не повторяется");
            Assert.That(voice.maxDistance, Is.GreaterThanOrEqualTo(200f), "слышно издалека");
        }

        [Test]
        public void BusIsARealHeavyBodyWithWheelsAndColliders()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Events/Event_eldrifto.prefab");
            var bus = prefab.transform.Find("Bus");
            Assert.That(bus.GetComponent<Rigidbody>(), Is.Not.Null);
            var box = bus.GetComponent<BoxCollider>();
            Assert.That(box.size.z, Is.GreaterThan(10f), "автобус намного длиннее машины");
            Assert.That(box.size.y, Is.GreaterThan(3f));
            var body = bus.GetComponent<ChaosBody>();
            Assert.That(body.Kind, Is.EqualTo(BodyKind.Bus));
            Assert.That(bus.GetComponent<DriftBusContact>(), Is.Not.Null, "удары доходят до события");
            var model = bus.Find("Visual/Lean/Model");
            foreach (var w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
                Assert.That(model.Find(w), Is.Not.Null, w);
            foreach (var fx in new[] { "FxSmokeL", "FxSmokeR", "FxSparks", "FxDust", "Screech" })
                Assert.That(bus.Find(fx), Is.Not.Null, fx);
        }

        [Test]
        public void VerityHasAFaceThatCanBlinkAndGasp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Events/Event_verity.prefab");
            Assert.That(prefab, Is.Not.Null);
            var verity = prefab.transform.Find("Verity");
            var rb = verity.GetComponent<Rigidbody>();
            Assert.That(rb.isKinematic, Is.True, "движется по дугам, в геометрии не застревает");
            Assert.That(verity.GetComponent<SphereCollider>().radius, Is.InRange(2.2f, 2.8f), "машины бьются о шар");
            var model = verity.Find("Pivot/Model");
            foreach (var part in new[] { "Body", "EyeL", "EyeR", "Smile", "MouthO" })
                Assert.That(model.Find(part), Is.Not.Null, part);
            var bounds = model.Find("Body").GetComponent<Renderer>().bounds;
            Assert.That(bounds.size.y, Is.InRange(4.8f, 5.6f), "шар диаметром ~5 м — видно издалека");
        }
    }
}
