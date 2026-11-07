using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Материалы ГИПЕРГАМИИ — файлы пользователя, не заменять: картинка (копия присланного изображения, байт в байт)
    /// и музыка (звуковая дорожка его видео IMG_5606.MP4, без обрезки и изменений, перекодирована в WAV только потому,
    /// что Unity не читает AAC). Настройки импорта картинки: без растяжения до степени двойки (иначе на весь экран она
    /// вышла бы квадратной), без сжатия с потерями, с мипами (карточка на дороге видна издалека).
    public static class HypergamyAssets
    {
        public const string PicturePath = "Assets/Art/Textures/Events/hypergamy.jpg";
        public const string MusicPath = "Assets/Art/Audio/Sfx/Events/hypergamy.wav";

        public static Texture2D Picture()
        {
            if (AssetImporter.GetAtPath(PicturePath) is TextureImporter importer)
            {
                bool dirty = importer.npotScale != TextureImporterNPOTScale.None || importer.textureCompression != TextureImporterCompression.Uncompressed
                             || importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled || importer.maxTextureSize < 1024
                             || importer.textureType != TextureImporterType.Default || importer.anisoLevel < 4;
                if (dirty)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = true;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.anisoLevel = 4;
                    importer.maxTextureSize = 1024;
                    importer.sRGBTexture = true;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PicturePath);
            if (texture == null) throw new InvalidOperationException($"нет картинки ГИПЕРГАМИИ {PicturePath}");
            return texture;
        }

        public static AudioClip Music()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            if (clip == null) throw new InvalidOperationException($"нет музыки ГИПЕРГАМИИ {MusicPath}");
            return clip;
        }

        /// Цвет краёв картинки (среднее по рамке в 4 пикселя): им заполняется экран вокруг вписанной картинки.
        public static Color EdgeColor()
        {
            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!probe.LoadImage(File.ReadAllBytes(PicturePath))) return Color.white;
                int w = probe.width, h = probe.height;
                const int band = 4;
                Color sum = Color.clear;
                int count = 0;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (x >= band && x < w - band && y >= band && y < h - band) continue;
                    sum += probe.GetPixel(x, y);
                    count++;
                }
                var c = count > 0 ? sum / count : Color.white;
                c.a = 1f;
                return c;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// Материал картинки на карточке — без освещения: цвета как в оригинале (под тёплым солнцем «Пустыни» Lit давал
        /// кремовый фон вместо белого) и одинаково читается днём и ночью на «Тель-Авиве».
        public static Material PictureMaterial()
        {
            var texture = Picture();
            const string path = "Assets/Art/Materials/M_HypergamyPicture.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "M_HypergamyPicture" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
