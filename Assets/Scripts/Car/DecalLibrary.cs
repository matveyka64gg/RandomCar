using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace RandomCar.Car
{
    /// Источник текстуры слоя наклейки: встроенная (DecalArt) или своя картинка игрока (CustomDecals).
    public static class DecalLibrary
    {
        public static Texture2D Resolve(DecalLayer layer)
        {
            if (layer == null || string.IsNullOrEmpty(layer.Source)) return null;
            if (layer.Source.StartsWith("builtin:", StringComparison.Ordinal)) return DecalArt.Get(layer.Source.Substring(8), layer.Number);
            if (layer.Source.StartsWith("number:", StringComparison.Ordinal)) return DecalArt.Get(layer.Source, layer.Number);
            if (layer.IsCustom) return CustomDecals.Load(layer.Source.Substring(7));
            return null;
        }

        public static string NameOf(DecalLayer layer)
        {
            if (layer == null) return "";
            if (layer.IsCustom) return "Своя картинка";
            string id = layer.Source.StartsWith("builtin:", StringComparison.Ordinal) ? layer.Source.Substring(8) : layer.Source;
            var info = DecalArt.Info(id);
            if (info == null) return "Наклейка";
            return id.StartsWith("number:", StringComparison.Ordinal) ? $"{info.Name} {layer.Number}" : info.Name;
        }
    }

    /// Свои наклейки игрока: PNG/JPEG с диска. Проверяются (формат по сигнатуре, размер файла, разрешение по
    /// заголовку — до декодирования), уменьшаются до MaxSide и сохраняются в persistentDataPath/Decals как PNG
    /// с именем по хэшу содержимого. Файлы игрока никуда не отправляются.
    public static class CustomDecals
    {
        public const long MaxFileBytes = 8L * 1024 * 1024;
        public const int MaxSourceSide = 4096;
        public const int MaxSide = 1024;
        public const int MaxCount = 32;

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();

        static string folderOverride;

        /// Уничтожить временную текстуру и в игре, и в редакторе (тесты, импорт из гаража в редакторе).
        static void Release(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        public static string Folder => folderOverride ?? Path.Combine(Application.persistentDataPath, "Decals");

        /// Другая папка библиотеки (тесты не трогают картинки игрока); null — обычная.
        public static void UseFolder(string folder)
        {
            folderOverride = folder;
            foreach (var t in cache.Values)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            cache.Clear();
        }

        /// Сохранённые картинки (id = хэш), новые первыми.
        public static List<string> List()
        {
            var result = new List<string>();
            if (!Directory.Exists(Folder)) return result;
            var files = new DirectoryInfo(Folder).GetFiles("*.png");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            foreach (var f in files) result.Add(Path.GetFileNameWithoutExtension(f.Name));
            return result;
        }

        public static Texture2D Load(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (cache.TryGetValue(id, out var tex) && tex != null) return tex;
            string path = Path.Combine(Folder, id + ".png");
            if (!File.Exists(path)) return null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException error)
            {
                Debug.LogWarning($"[RandomCar] Наклейка {id} не прочитана: {error.Message}");
                return null;
            }
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Custom_" + id, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            if (!tex.LoadImage(bytes, false))
            {
                Release(tex);
                return null;
            }
            tex.Apply(true, true);
            cache[id] = tex;
            return tex;
        }

        public static void Delete(string id)
        {
            if (cache.TryGetValue(id, out var tex) && tex != null) Release(tex);
            cache.Remove(id);
            string path = Path.Combine(Folder, id + ".png");
            if (File.Exists(path)) File.Delete(path);
        }

        public enum ImageKind { Unknown, Png, Jpeg }

        /// Результат проверки и импорта.
        public sealed class ImportResult
        {
            public bool Ok;
            public string Id;
            public string Error;
        }

        /// Формат по первым байтам (расширение файла не проверяется — оно может врать).
        public static ImageKind Sniff(byte[] head)
        {
            if (head == null || head.Length < 8) return ImageKind.Unknown;
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 && head[4] == 0x0D && head[5] == 0x0A) return ImageKind.Png;
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ImageKind.Jpeg;
            return ImageKind.Unknown;
        }

        /// Ширина и высота из заголовка (PNG — IHDR, JPEG — маркер SOF). Без декодирования картинки.
        public static bool TryReadSize(byte[] data, ImageKind kind, out int width, out int height)
        {
            width = height = 0;
            if (kind == ImageKind.Png)
            {
                if (data.Length < 24) return false;
                width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
                return width > 0 && height > 0;
            }
            if (kind == ImageKind.Jpeg)
            {
                int i = 2;
                while (i + 9 < data.Length)
                {
                    if (data[i] != 0xFF) { i++; continue; }
                    byte marker = data[i + 1];
                    if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
                    int len = (data[i + 2] << 8) | data[i + 3];
                    if (len < 2) return false;
                    bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                    if (sof)
                    {
                        height = (data[i + 5] << 8) | data[i + 6];
                        width = (data[i + 7] << 8) | data[i + 8];
                        return width > 0 && height > 0;
                    }
                    i += 2 + len;
                }
            }
            return false;
        }

        /// Проверка без декодирования: формат, размер файла, разрешение. Возвращает текст ошибки или null.
        public static string Validate(string path, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "Файл не найден.";
            long length = new FileInfo(path).Length;
            if (length == 0) return "Файл пустой.";
            if (length > MaxFileBytes) return $"Файл слишком большой ({length / (1024f * 1024f):0.0} МБ). Можно до {MaxFileBytes / (1024 * 1024)} МБ.";
            if (List().Count >= MaxCount) return $"Сохранено уже {MaxCount} картинок — удалите ненужные.";
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException error)
            {
                return $"Не удалось прочитать файл: {error.Message}";
            }
            catch (UnauthorizedAccessException)
            {
                return "Нет доступа к файлу.";
            }
            var kind = Sniff(data);
            if (kind == ImageKind.Unknown) return "Это не PNG и не JPEG. Поддерживаются только PNG и JPEG.";
            if (!TryReadSize(data, kind, out int w, out int h)) return "Файл повреждён: не удалось прочитать размер картинки.";
            if (w > MaxSourceSide || h > MaxSourceSide) return $"Слишком большое разрешение ({w}×{h}). Можно до {MaxSourceSide}×{MaxSourceSide}.";
            return null;
        }

        /// Импорт: проверка, декодирование, уменьшение, сохранение PNG. Повторный импорт той же картинки — тот же id.
        public static ImportResult Import(string path)
        {
            string error = Validate(path, out var data);
            if (error != null) return new ImportResult { Error = error };
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(data, false)) return new ImportResult { Error = "Файл повреждён: картинку не удалось прочитать." };
                var result = Downscale(source, MaxSide);
                byte[] png = result.EncodeToPNG();
                if (result != source) Release(result);
                string id = Hash(png);
                Directory.CreateDirectory(Folder);
                string target = Path.Combine(Folder, id + ".png");
                if (!File.Exists(target)) File.WriteAllBytes(target, png);
                else File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                return new ImportResult { Ok = true, Id = id };
            }
            catch (IOException e)
            {
                return new ImportResult { Error = $"Не удалось сохранить картинку: {e.Message}" };
            }
            finally
            {
                Release(source);
            }
        }

        /// Уменьшение с сохранением пропорций (на видеокарте, без чтения большого массива пикселей в память).
        static Texture2D Downscale(Texture2D source, int maxSide)
        {
            int w = source.width, h = source.height;
            if (w <= maxSide && h <= maxSide) return source;
            float k = (float)maxSide / Mathf.Max(w, h);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
            var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var result = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        static string Hash(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] h = sha.ComputeHash(data);
            return BitConverter.ToString(h, 0, 12).Replace("-", "").ToLowerInvariant();
        }
    }
}
