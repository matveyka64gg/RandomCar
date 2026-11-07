using System.Linq;
using NUnit.Framework;
using RandomCar.Audio;
using UnityEditor;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Музыка: читаемые названия из имён файлов, библиотека с плейлистами, настройки импорта, отдельная папка эффектов.
    public sealed class MusicTests
    {
        const string LibraryPath = "Assets/Settings/MusicLibrary.asset";

        [TestCase("ЭЛЕВЕН ЭЙТ - ФРУКТОВЫЙ 2.0.ogg", "ЭЛЕВЕН ЭЙТ", "ФРУКТОВЫЙ 2.0")]
        [TestCase("Aki Nakata - Nightcore - 18.ogg", "Aki Nakata", "Nightcore - 18")]
        [TestCase("51XDorass - Toyoki (康熙) - Luo Lei (落淚) (TT VERSION) (how it feels to cook a yummy meal as a boy).ogg", "51XDorass", "Toyoki - Luo Lei (TT VERSION)")]
        [TestCase("►Plαy┼Mµsìc«² - Nightcore - All The Things She Said.ogg", "Play Musìc", "Nightcore - All The Things She Said")]
        [TestCase("XI$OW, GXNRC, dmitry.rx - HAVA NAGILA (HARDTEKK).ogg", "XI$OW, GXNRC, dmitry.rx", "HAVA NAGILA (HARDTEKK)")]
        [TestCase("Track_without_artist.mp3", "", "Track without artist")]
        [TestCase("康熙.ogg", "", MusicTitles.Untitled)]
        public void TitleFromFileName(string file, string artist, string title)
        {
            var (a, t) = MusicTitles.FromFileName(file);
            Assert.That(a, Is.EqualTo(artist));
            Assert.That(t, Is.EqualTo(title));
        }

        [Test]
        public void LibraryHasReadablePlaylists()
        {
            var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
            Assert.That(library, Is.Not.Null, "библиотека собрана (RandomCar/Audio/Update Music Library)");
            foreach (var track in library.Tracks)
            {
                Assert.That(track.Clip, Is.Not.Null, track.Title);
                Assert.That(track.Title, Is.Not.Empty);
                Assert.That((track.Title + track.Artist).All(MusicTitles.Renderable), Is.True, $"символы вне шрифта: {track.Title}");
                Assert.That(track.Gain, Is.InRange(0.05f, 1f));
                string name = track.Clip.name.ToLowerInvariant();
                Assert.That(name.Contains("сирен") || name.Contains("siren"), Is.False, "сирена — звуковой эффект, не трек");
            }
            // Плейлист «Тель-Авива» отделён от общего (может быть пустым — тогда карта играет без музыки).
            var main = library.Playlist(MusicLibrary.MainPlaylist);
            var telAviv = library.Playlist("TelAviv");
            Assert.That(main.Intersect(telAviv), Is.Empty);
            Assert.That(library.Playlist("НетТакого"), Is.Empty);
        }

        [Test]
        public void MusicStreamsAndEffectsLiveSeparately()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Art/Audio/Music" }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming), importer.assetPath);
                Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis), importer.assetPath);
            }
            var effects = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Art/Audio/Sfx" });
            foreach (string guid in effects)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(importer.defaultSampleSettings.loadType, Is.Not.EqualTo(AudioClipLoadType.Streaming), importer.assetPath);
            }
        }
    }
}
