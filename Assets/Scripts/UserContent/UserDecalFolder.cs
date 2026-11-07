using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.UserContent
{
    /// Папка UserContent/Decals рядом с игрой (Windows): PNG и JPEG оттуда сами попадают в библиотеку наклеек (CustomDecals).
    /// Повторное сканирование не читает знакомые файлы и не создаёт дубликатов; картинка, удалённая игроком в гараже,
    /// из папки обратно не возвращается (пока он не добавит её кнопкой). Список — persistentDataPath/UserContent/decals_folder.json.
    public static class UserDecalFolder
    {
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        [Serializable]
        sealed class Entry
        {
            public string quickKey;
            public string source;
            public string id;
            public bool removed;
        }

        [Serializable]
        sealed class Index
        {
            public List<Entry> files = new List<Entry>();
        }

        static Index index;
        static string indexPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            index = null;
            indexPath = null;
        }

        /// Файл списка (подменяется в тестах).
        public static void UseIndex(string path)
        {
            indexPath = path;
            index = null;
        }

        static Index Data
        {
            get
            {
                if (index != null) return index;
                indexPath ??= Path.Combine(UserContentPaths.LibraryRoot, "decals_folder.json");
                index = new Index();
                try
                {
                    if (File.Exists(indexPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(indexPath), index);
                }
                catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[RandomCar] Список папки наклеек не прочитан: {error.Message}");
                    index = new Index();
                }
                return index;
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");
                File.WriteAllText(indexPath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[RandomCar] Список папки наклеек не сохранён: {error.Message}");
            }
        }

        /// Найти новые картинки в папке и добавить в библиотеку. Возвращает текст для игрока или null (ничего нового).
        public static string Scan(string folder = null)
        {
            folder ??= UserContentPaths.DecalDropFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            List<FileInfo> files;
            try
            {
                files = new DirectoryInfo(folder).GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant())).OrderBy(f => f.Name).ToList();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return $"Папка наклеек недоступна: {error.Message}";
            }
            int added = 0;
            var errors = new List<string>();
            bool changed = false;
            foreach (var file in files)
            {
                string key = UserContentPaths.QuickKey(file);
                if (Data.files.Any(e => e.quickKey == key)) continue;
                string source;
                try
                {
                    source = UserContentPaths.HashFile(file.FullName);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    errors.Add($"{file.Name}: {error.Message}");
                    continue;
                }
                var same = Data.files.FirstOrDefault(e => e.source == source);
                if (same != null)
                {
                    // Тот же файл переименовали или скопировали — не дубликат.
                    Data.files.Add(new Entry { quickKey = key, source = source, id = same.id, removed = same.removed });
                    changed = true;
                    continue;
                }
                var result = CustomDecals.Import(file.FullName);
                changed = true;
                if (!result.Ok)
                {
                    errors.Add($"{file.Name}: {result.Error}");
                    // Неподходящий файл запоминаем, чтобы не проверять при каждом открытии; исправленный (новое время изменения) проверится снова.
                    Data.files.Add(new Entry { quickKey = key, source = source, id = null });
                    continue;
                }
                Data.files.Add(new Entry { quickKey = key, source = source, id = result.Id });
                added++;
            }
            if (changed) Save();
            if (added == 0 && errors.Count == 0) return null;
            string message = added > 0 ? $"Из папки UserContent/Decals добавлено картинок: {added}." : "";
            if (errors.Count > 0) message += (message.Length > 0 ? " " : "") + $"Не подошло: {errors.Count} ({errors[0]})";
            foreach (var e in errors) Debug.LogWarning($"[RandomCar] Папка наклеек: {e}");
            return message;
        }

        /// Игрок удалил свою картинку в гараже: из папки она больше сама не вернётся.
        public static void Forget(string id)
        {
            bool changed = false;
            foreach (var e in Data.files.Where(e => e.id == id))
            {
                e.removed = true;
                changed = true;
            }
            if (changed) Save();
        }
    }
}
