using System.IO;
using System.Linq;
using NUnit.Framework;
using RandomCar.Car;
using RandomCar.Track;
using RandomCar.UserContent;
using UnityEditor;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Бета 0.1: своя музыка и наклейки (импорт, дубликаты, форматы, папки), новые трассы «Город» и «Холмы Рандома».
    /// Файлы — во временной папке: библиотека игрока не трогается.
    public sealed class BetaReleaseTests
    {
        string temp;

        [SetUp]
        public void SetUp()
        {
            temp = Path.Combine(Path.GetTempPath(), "rc_beta_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            UserMusicLibrary.UseFolder(Path.Combine(temp, "Library", "Music"));
            UserDecalFolder.UseIndex(Path.Combine(temp, "Library", "decals_folder.json"));
            CustomDecals.UseFolder(Path.Combine(temp, "Decals"));
        }

        [TearDown]
        public void TearDown()
        {
            CustomDecals.UseFolder(null);
            UserMusicLibrary.UseFolder(UserContentPaths.MusicLibraryFolder);
            UserDecalFolder.UseIndex(null);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }

        /// Минимальный WAV (PCM 16 бит, моно): заголовок RIFF/WAVE и тишина; seed меняет содержимое.
        static string Wav(string dir, string name, int seed)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            using var w = new BinaryWriter(File.Create(path));
            int samples = 2205;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + samples * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(samples * 2);
            for (int i = 0; i < samples; i++) w.Write((short)(i == 0 ? seed : 0));
            return path;
        }

        [Test]
        public void MusicImportDeduplicatesByContent()
        {
            string a = Wav(Path.Combine(temp, "src"), "Artist - Song.wav", 1);
            string copy = Path.Combine(temp, "src", "renamed copy.wav");
            File.Copy(a, copy);
            var first = UserMusicLibrary.Import(a);
            var second = UserMusicLibrary.Import(copy);
            Assert.That(first.Added, Is.True, first.Message);
            Assert.That(second.Added, Is.False, "тот же трек под другим именем — не дубликат");
            Assert.That(UserMusicLibrary.Tracks.Count, Is.EqualTo(1));
            var t = UserMusicLibrary.Tracks[0];
            Assert.That(t.title, Is.EqualTo("Song"));
            Assert.That(t.artist, Is.EqualTo("Artist"));
            Assert.That(t.imported, Is.True);
            Assert.That(File.Exists(t.file), Is.True, "копия в библиотеке игры");
            Assert.That(t.file.StartsWith(temp), Is.True);
        }

        [Test]
        public void MusicImportRejectsUnsupportedAndBrokenFiles()
        {
            Directory.CreateDirectory(Path.Combine(temp, "src"));
            string m4a = Path.Combine(temp, "src", "song.mp3");
            File.WriteAllBytes(m4a, new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ', 0, 0, 0, 0 });
            var r = UserMusicLibrary.Import(m4a);
            Assert.That(r.Ok, Is.False);
            StringAssert.Contains("M4A", r.Message, "понятная причина: формат, а не «ошибка»");
            string text = Path.Combine(temp, "src", "notes.ogg");
            File.WriteAllText(text, "это не музыка, а просто текстовый файл");
            Assert.That(UserMusicLibrary.Import(text).Ok, Is.False);
            string empty = Path.Combine(temp, "src", "empty.wav");
            File.WriteAllBytes(empty, new byte[0]);
            Assert.That(UserMusicLibrary.Import(empty).Ok, Is.False);
            Assert.That(UserMusicLibrary.Tracks, Is.Empty);
        }

        [Test]
        public void MusicSniffRecognizesSupportedFormats()
        {
            byte[] Head(string tag, int at = 0)
            {
                var b = new byte[16];
                for (int i = 0; i < tag.Length; i++) b[at + i] = (byte)tag[i];
                return b;
            }
            Assert.That(UserMusicLibrary.Sniff(Head("OggS"), out _), Is.EqualTo(".ogg"));
            Assert.That(UserMusicLibrary.Sniff(Head("ID3"), out _), Is.EqualTo(".mp3"));
            var frame = new byte[16];
            frame[0] = 0xFF; frame[1] = 0xFB;
            Assert.That(UserMusicLibrary.Sniff(frame, out _), Is.EqualTo(".mp3"));
            var wav = Head("RIFF");
            foreach (var (c, i) in "WAVE".Select((c, i) => (c, i))) wav[8 + i] = (byte)c;
            Assert.That(UserMusicLibrary.Sniff(wav, out _), Is.EqualTo(".wav"));
            Assert.That(UserMusicLibrary.Sniff(Head("fLaC"), out string reason), Is.Null);
            StringAssert.Contains("FLAC", reason);
        }

        [Test]
        public void MusicFolderScanAddsOnceAndKeepsUserFileOnRemove()
        {
            string folder = Path.Combine(temp, "UserContent", "Music");
            string song = Wav(folder, "Band - Track.wav", 7);
            Wav(folder, "Band - Track (copy).wav", 7);
            Wav(folder, "Other - Tune.wav", 9);
            UserMusicLibrary.ScanDropFolder(folder);
            Assert.That(UserMusicLibrary.Tracks.Count, Is.EqualTo(2), "два разных трека, копия не дублируется");
            UserMusicLibrary.ScanDropFolder(folder);
            Assert.That(UserMusicLibrary.Tracks.Count, Is.EqualTo(2), "повторное сканирование без дубликатов");
            var track = UserMusicLibrary.Tracks.First(t => t.artist == "Band");
            Assert.That(track.imported, Is.False, "файл из папки не копируется");
            string userFile = track.file;
            string message = UserMusicLibrary.Remove(track.id);
            StringAssert.Contains("Файл остался", message);
            Assert.That(File.Exists(userFile) && File.Exists(song), Is.True, "удаление из библиотеки не удаляет файл игрока");
            UserMusicLibrary.ScanDropFolder(folder);
            Assert.That(UserMusicLibrary.Tracks.Any(t => t.id == track.id), Is.False, "убранный трек не возвращается сканированием");
            // Добавили тот же файл кнопкой — снова в библиотеке.
            var again = UserMusicLibrary.Import(song);
            Assert.That(again.Added, Is.True);
            Assert.That(UserMusicLibrary.Tracks.Any(t => t.id == track.id), Is.True);
        }

        [Test]
        public void MusicLibrarySurvivesReload()
        {
            string a = Wav(Path.Combine(temp, "src"), "Keep - Me.wav", 3);
            UserMusicLibrary.Import(a);
            File.Delete(a);   // исходник пропал — у игры своя копия
            UserMusicLibrary.UseFolder(Path.Combine(temp, "Library", "Music"));   // «перезапуск»: список читается с диска
            Assert.That(UserMusicLibrary.Tracks.Count, Is.EqualTo(1));
            Assert.That(UserMusicLibrary.Tracks[0].title, Is.EqualTo("Me"));
            string id = UserMusicLibrary.Tracks[0].id;
            string file = UserMusicLibrary.Tracks[0].file;
            UserMusicLibrary.Remove(id);
            Assert.That(File.Exists(file), Is.False, "своя копия игры удаляется вместе с треком");
            Assert.That(UserMusicLibrary.Tracks, Is.Empty);
        }

        static void SavePng(string path, bool transparent)
        {
            var tex = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 40, 40, (byte)(transparent && i % 64 < 32 ? 0 : 255));
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, transparent ? tex.EncodeToPNG() : tex.EncodeToJPG(90));
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void DecalFolderImportsPngWithAlphaAndJpegOnce()
        {
            string folder = Path.Combine(temp, "UserContent", "Decals");
            Directory.CreateDirectory(folder);
            SavePng(Path.Combine(folder, "logo.png"), true);
            SavePng(Path.Combine(folder, "photo.jpg"), false);
            File.WriteAllText(Path.Combine(folder, "fake.png"), "не картинка");
            string message = UserDecalFolder.Scan(folder);
            StringAssert.Contains("добавлено картинок: 2", message);
            StringAssert.Contains("Не подошло: 1", message);
            var ids = CustomDecals.List();
            Assert.That(ids.Count, Is.EqualTo(2));
            Assert.That(UserDecalFolder.Scan(folder), Is.Null, "повторное сканирование ничего не добавляет");
            Assert.That(CustomDecals.List().Count, Is.EqualTo(2));
            Assert.That(ids.All(id => CustomDecals.Load(id) != null), Is.True, "картинки загружаются как текстуры наклеек");
            // PNG сохранил прозрачность: левая половина — прозрачная (читаем сохранённые файлы библиотеки).
            var textures = ids.Select(id =>
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(Path.Combine(CustomDecals.Folder, id + ".png")));
                return t;
            }).ToList();
            Assert.That(textures.Any(t => t.GetPixel(4, 4).a < 0.05f && t.GetPixel(60, 4).a > 0.95f), Is.True, "альфа-канал PNG");
            Assert.That(textures.Any(t => t.GetPixel(4, 4).a > 0.95f), Is.True, "JPEG без прозрачности — непрозрачный");
            // Удалённая в гараже картинка из папки обратно не возвращается.
            string removed = ids[0];
            CustomDecals.Delete(removed);
            UserDecalFolder.Forget(removed);
            File.SetLastWriteTimeUtc(Path.Combine(folder, "logo.png"), System.DateTime.UtcNow.AddMinutes(1));
            UserDecalFolder.Scan(folder);
            Assert.That(CustomDecals.List().Contains(removed), Is.False);
        }

        static readonly string[] NewMaps = { "City", "Hills" };

        [Test]
        public void ReworkedTracksAreLongWideAndDoNotTouchThemselves([ValueSource(nameof(NewMaps))] string key)
        {
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>($"Assets/Settings/Tracks/Track_{key}.asset");
            Assert.That(def, Is.Not.Null);
            var path = def.Path;
            Assert.That(path.Length, Is.GreaterThan(2500f), "большая трасса (больше «Пустыни»)");
            Assert.That(def.HalfWidth, Is.GreaterThanOrEqualTo(7f), "дорога не уже 14 м — место для нескольких машин");
            // Участки, далёкие по трассе, не подходят друг к другу ближе 60 м: ближайшая точка трассы (круги, ИИ, события) однозначна.
            float closest = float.MaxValue;
            for (int i = 0; i < path.Count; i += 2)
                for (int j = i + 2; j < path.Count; j += 2)
                {
                    float along = Mathf.Abs(path.DistanceAt(j) - path.DistanceAt(i));
                    along = Mathf.Min(along, path.Length - along);
                    if (along < 150f) continue;
                    Vector3 a = path.Point(i), b = path.Point(j);
                    closest = Mathf.Min(closest, new Vector2(a.x - b.x, a.z - b.z).magnitude);
                }
            TestContext.WriteLine($"{key}: {path.Length:F0} м, полуширина {def.HalfWidth}, ближе всего участки {closest:F0} м");
            Assert.That(closest, Is.GreaterThan(60f));
        }

        [Test]
        public void ReworkedMapScenesHaveTwoLaps([ValueSource(nameof(NewMaps))] string key)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"Assets/Scenes/Map_{key}.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var map = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Maps.MapDefinition>()).FirstOrDefault(m => m != null);
                Assert.That(map, Is.Not.Null);
                Assert.That(map.Laps, Is.EqualTo(2), "длинная трасса — 2 круга, как «Тель-Авив»");
                Assert.That(map.Track.Definition, Is.SameAs(AssetDatabase.LoadAssetAtPath<TrackDefinition>($"Assets/Settings/Tracks/Track_{key}.asset")));
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
