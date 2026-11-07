using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using RandomCar.Events;
using RandomCar.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.Tests
{
    /// ГИПЕРГАМИЯ (v7): материалы пользователя без подмены, настройки импорта (картинка без искажения пропорций),
    /// карточка на дороге с триггером касания, полноэкранная панель поверх интерфейса, 10 секунд.
    public sealed class HypergamyTests
    {
        const string PicturePath = "Assets/Art/Textures/Events/hypergamy.jpg";
        const string MusicPath = "Assets/Art/Audio/Sfx/Events/hypergamy.wav";
        /// SHA-256 присланной картинки (копия байт в байт).
        const string PictureSha256 = "401cc4d49c12c2a38743e7d0bb73f9e47f779e0dc918f1b1a660063991498163";
        /// SHA-256 звуковой дорожки, извлечённой из видео пользователя IMG_5606.MP4 (ffmpeg, PCM 16 бит, без обрезки).
        const string MusicSha256 = "2790af9c370c71081902178890f06b447af7d07821c6713d3f6a52e125f7ed50";

        static string Sha(string path)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
        }

        [Test]
        public void UsesTheUsersPictureAndMusicUnchanged()
        {
            Assert.That(Sha(PicturePath), Is.EqualTo(PictureSha256), "картинка пользователя без изменений");
            Assert.That(Sha(MusicPath), Is.EqualTo(MusicSha256), "музыка из видео пользователя без изменений");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PicturePath);
            Assert.That(texture, Is.Not.Null);
            Assert.That(new Vector2(texture.width, texture.height), Is.EqualTo(new Vector2(828, 979)), "не растянута до степени двойки");
            var importer = (TextureImporter)AssetImporter.GetAtPath(PicturePath);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "без артефактов сжатия на весь экран");

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.InRange(9.8f, 10.0f), "вся дорожка видео (9.9 с)");
            Assert.That(clip.channels, Is.EqualTo(2));
            var audio = (AudioImporter)AssetImporter.GetAtPath(MusicPath);
            Assert.That(audio.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.PCM));
            Assert.That(audio.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad), "играет сразу, без задержки");
        }

        [Test]
        public void CardStandsOnTheRoadWithATouchTrigger()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Events/Event_hypergamy.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<HypergamyEvent>(), Is.Not.Null);
            var card = prefab.transform.Find("Card");
            var trigger = card.Find("Trigger");
            var capsule = trigger.GetComponent<CapsuleCollider>();
            Assert.That(capsule.isTrigger, Is.True, "машина проезжает сквозь, касание ловит триггер");
            Assert.That(capsule.radius, Is.InRange(1.6f, 2.4f), "по размаху карточки");
            Assert.That(capsule.center.y - capsule.height / 2f, Is.LessThanOrEqualTo(0.1f), "от самой дороги");
            Assert.That(trigger.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(trigger.gameObject.layer, Is.EqualTo(2), "лучи колёс, камеры и ИИ его не видят");
            Assert.That(trigger.GetComponent<HypergamyContact>(), Is.Not.Null);

            var pivot = card.Find("Pivot");
            Assert.That(pivot.Find("Model"), Is.Not.Null, "рама из Blender");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PicturePath);
            foreach (var name in new[] { "PictureFront", "PictureBack" })
            {
                var quad = pivot.Find(name);
                Assert.That(quad, Is.Not.Null, name);
                var material = quad.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(texture), $"{name}: картинка пользователя");
                float aspect = quad.localScale.x / quad.localScale.y;
                Assert.That(aspect, Is.EqualTo(texture.width / (float)texture.height).Within(0.01f), $"{name}: пропорции картинки");
                Assert.That(quad.localScale.y, Is.GreaterThan(3.5f), "хорошо видно на трассе");
            }
        }

        [Test]
        public void FullScreenPanelIsAboveTheGameInterfaceForTenSeconds()
        {
            var main = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/RandomCarPanel.asset");
            var overlay = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/RandomCarOverlayPanel.asset");
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.sortingOrder, Is.GreaterThan(main.sortingOrder), "поверх игрового интерфейса");
            Assert.That(HypergamyScreen.Duration, Is.EqualTo(10f), "ровно 10 секунд");
            Assert.That(HypergamyScreen.DuckGain, Is.InRange(0.05f, 0.5f), "остальной звук тише музыки");
        }
    }
}
