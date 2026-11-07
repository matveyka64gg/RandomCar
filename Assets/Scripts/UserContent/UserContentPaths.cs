using System;
using System.IO;
using UnityEngine;

namespace RandomCar.UserContent
{
    /// Где лежит пользовательский контент (музыка и наклейки игрока). Отдельно от ресурсов игры:
    /// встроенные ресурсы не перезаписываются, контент не отправляется в сеть.
    ///  • Библиотека (импортированные копии, списки) — Application.persistentDataPath/UserContent: переживает обновление игры
    ///    (Windows: AppData\LocalLow\DefaultCompany\RandomCar, Android: личная папка приложения).
    ///  • Папки «положи файл» (только Windows) — UserContent/Music и UserContent/Decals рядом с RandomCar.exe
    ///    (в редакторе — в корне проекта). Файлы из них не копируются и не удаляются игрой.
    public static class UserContentPaths
    {
        /// Папки рядом с игрой есть только на ПК (на Android у приложения нет «папки игры», файлы выбирают системным окном).
        public static bool HasDropFolders =>
#if UNITY_EDITOR || UNITY_STANDALONE
            true;
#else
            false;
#endif

        /// Корень папок «положи файл»: …/RandomCar/UserContent (Windows) или <проект>/UserContent (редактор). Null на Android.
        public static string DropRoot => HasDropFolders ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "UserContent") : null;

        public static string MusicDropFolder => HasDropFolders ? Path.Combine(DropRoot, "Music") : null;
        public static string DecalDropFolder => HasDropFolders ? Path.Combine(DropRoot, "Decals") : null;

        /// Библиотека пользователя в личной папке игры.
        public static string LibraryRoot => Path.Combine(Application.persistentDataPath, "UserContent");
        public static string MusicLibraryFolder => Path.Combine(LibraryRoot, "Music");

        /// Создать папки «положи файл» с короткой подсказкой внутри (один раз; ошибки доступа — не повод падать).
        public static void EnsureDropFolders()
        {
            if (!HasDropFolders) return;
            try
            {
                Directory.CreateDirectory(MusicDropFolder);
                Directory.CreateDirectory(DecalDropFolder);
                WriteHint(Path.Combine(MusicDropFolder, "README.txt"),
                    "Положите сюда свою музыку (OGG, MP3, WAV) — RandomCar найдёт её при запуске и при открытии раздела «Моя музыка».\r\n" +
                    "Удаление трека из библиотеки игры не удаляет файл из этой папки.\r\n");
                WriteHint(Path.Combine(DecalDropFolder, "README.txt"),
                    "Положите сюда свои картинки для наклеек (PNG с прозрачностью или JPEG) — RandomCar добавит их в гараж при запуске.\r\n" +
                    "До 8 МБ и 4096×4096 точек, в игре картинка уменьшается до 1024 точек по большей стороне.\r\n");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[RandomCar] Папки UserContent не созданы ({DropRoot}): {error.Message}");
            }
        }

        static void WriteHint(string path, string text)
        {
            if (!File.Exists(path)) File.WriteAllText(path, text);
        }

        /// Ключ файла без чтения содержимого: путь, размер, время изменения (повторное сканирование не читает знакомые файлы).
        public static string QuickKey(FileInfo file) => $"{file.FullName.ToLowerInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";

        /// Хэш содержимого (первые 12 байт SHA-256 в hex): один и тот же файл под другим именем — тот же id.
        public static string Hash(Stream stream)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] h = sha.ComputeHash(stream);
            return BitConverter.ToString(h, 0, 12).Replace("-", "").ToLowerInvariant();
        }

        public static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Hash(stream);
        }

        /// Открыть папку в проводнике (Windows): кнопка «Открыть папку».
        public static void Reveal(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            Application.OpenURL(new Uri(folder).AbsoluteUri);
        }
    }
}
