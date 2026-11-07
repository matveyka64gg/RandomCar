using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.UserContent
{
    /// Трек из библиотеки игрока. Происхождение: «папка» — файл лежит в UserContent/Music рядом с игрой (Windows, игра его
    /// только читает), «импорт» — копия, сделанная игрой в личной папке (кнопка «Добавить музыку», на Android — всегда).
    [Serializable]
    public sealed class UserTrack
    {
        public string id;
        public string title;
        public string artist;
        public string file;
        public string ext;
        public bool imported;
        /// Убран из библиотеки, но файл в папке UserContent/Music остался: при сканировании не возвращается.
        public bool hidden;
        public long added;
        public string quickKey;

        public bool Available => !hidden && !string.IsNullOrEmpty(file) && File.Exists(file);
    }

    /// Библиотека своей музыки игрока: список в persistentDataPath/UserContent/music.json, копии импортированных файлов там же
    /// (переживают обновление игры). Повтор одного и того же файла (по содержимому) не создаёт дубликат. Тяжёлая работа
    /// (хэш, копирование) — в фоновом потоке, игра не замирает. Файлы никуда не отправляются.
    public static class UserMusicLibrary
    {
        /// Форматы, которые проигрыватель Unity читает с диска (UnityWebRequestMultimedia): OGG Vorbis, MP3, WAV.
        public static readonly string[] Extensions = { ".ogg", ".mp3", ".wav" };
        public const long MaxFileBytes = 100L * 1024 * 1024;
        public const int MaxTracks = 500;

        [Serializable]
        sealed class Index
        {
            public int version = 1;
            public List<UserTrack> tracks = new List<UserTrack>();
        }

        static Index index;
        static string indexPath;

        /// Список изменился (добавлен, убран, найден в папке) — проигрыватель и экран «Моя музыка» обновляются.
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            index = null;
            indexPath = null;
            Changed = null;
        }

        /// Папка библиотеки и файл списка (подменяются в тестах).
        public static string LibraryFolder { get; private set; }

        public static void UseFolder(string libraryFolder)
        {
            LibraryFolder = libraryFolder;
            indexPath = Path.Combine(Path.GetDirectoryName(libraryFolder) ?? libraryFolder, "music.json");
            index = null;
        }

        static Index Data
        {
            get
            {
                if (index != null) return index;
                if (LibraryFolder == null) UseFolder(UserContentPaths.MusicLibraryFolder);
                index = new Index();
                try
                {
                    if (File.Exists(indexPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(indexPath), index);
                }
                catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[RandomCar] Список своей музыки не прочитан, начат новый: {error.Message}");
                    index = new Index();
                }
                index.tracks.RemoveAll(t => t == null || string.IsNullOrEmpty(t.id));
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
                Debug.LogWarning($"[RandomCar] Список своей музыки не сохранён: {error.Message}");
            }
        }

        /// Треки, которые можно слушать (файл на месте, не убран), по порядку добавления.
        public static List<UserTrack> Tracks => Data.tracks.Where(t => t.Available).OrderBy(t => t.added).ToList();

        public static UserTrack Find(string id) => Data.tracks.Find(t => t.id == id);

        public static AudioType TypeOf(string ext) => ext switch
        {
            ".ogg" => AudioType.OGGVORBIS,
            ".mp3" => AudioType.MPEG,
            ".wav" => AudioType.WAV,
            _ => AudioType.UNKNOWN,
        };

        /// Формат по первым байтам (расширение может врать). Null — неподдерживаемый формат, текст причины — в reason.
        public static string Sniff(byte[] head, out string reason)
        {
            reason = null;
            if (head == null || head.Length < 12)
            {
                reason = "Файл пустой или повреждён.";
                return null;
            }
            bool At(int offset, string tag)
            {
                for (int i = 0; i < tag.Length; i++)
                    if (head[offset + i] != (byte)tag[i]) return false;
                return true;
            }
            if (At(0, "OggS")) return ".ogg";
            if (At(0, "RIFF") && At(8, "WAVE")) return ".wav";
            if (At(0, "ID3") || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)) return ".mp3";
            if (At(4, "ftyp")) reason = "Формат M4A/AAC игра не проигрывает. Поддерживаются OGG, MP3 и WAV.";
            else if (At(0, "fLaC")) reason = "Формат FLAC игра не проигрывает. Поддерживаются OGG, MP3 и WAV.";
            else reason = "Это не аудиофайл OGG, MP3 или WAV.";
            return null;
        }

        /// Результат проверки и подготовки файла (в фоне, без API Unity).
        sealed class Prepared
        {
            public string Error;
            public string Id;
            public string Ext;
            public string Source;
            public string Name;
        }

        static Prepared Prepare(string path, string displayName)
        {
            var result = new Prepared { Source = path, Name = string.IsNullOrEmpty(displayName) ? Path.GetFileName(path) : displayName };
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Fail(result, "Файл не найден.");
                if (info.Length == 0) return Fail(result, "Файл пустой.");
                if (info.Length > MaxFileBytes) return Fail(result, $"Файл слишком большой ({info.Length / (1024f * 1024f):0} МБ). Можно до {MaxFileBytes / (1024 * 1024)} МБ.");
                var head = new byte[16];
                using (var stream = File.OpenRead(path))
                {
                    int read = stream.Read(head, 0, head.Length);
                    if (read < head.Length) return Fail(result, "Файл пустой или повреждён.");
                    result.Ext = Sniff(head, out string reason);
                    if (result.Ext == null) return Fail(result, reason);
                    stream.Position = 0;
                    result.Id = UserContentPaths.Hash(stream);
                }
                return result;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return Fail(result, $"Не удалось прочитать файл: {error.Message}");
            }
        }

        static Prepared Fail(Prepared p, string error)
        {
            p.Error = error;
            return p;
        }

        /// Итог импорта: текст для игрока и добавлен ли новый трек.
        public sealed class Result
        {
            public bool Ok;
            public bool Added;
            public string Message;
            public UserTrack Track;
        }

        /// Добавить файл в библиотеку (копия в личную папку игры). deleteSource — источник временный (Android: копия из окна выбора).
        public static async Task<Result> ImportAsync(string path, string displayName = null, bool deleteSource = false)
        {
            if (LibraryFolder == null) UseFolder(UserContentPaths.MusicLibraryFolder);
            string folder = LibraryFolder;
            var prepared = await Task.Run(() =>
            {
                var p = Prepare(path, displayName);
                if (p.Error == null)
                {
                    try
                    {
                        Directory.CreateDirectory(folder);
                        string target = Path.Combine(folder, p.Id + p.Ext);
                        if (!File.Exists(target)) File.Copy(path, target);
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    {
                        p.Error = $"Не удалось сохранить трек: {error.Message}";
                    }
                }
                if (deleteSource) TryDelete(path);
                return p;
            });
            return Apply(prepared, imported: true, folder);
        }

        /// То же без фонового потока (тесты, редактор).
        public static Result Import(string path, string displayName = null)
        {
            if (LibraryFolder == null) UseFolder(UserContentPaths.MusicLibraryFolder);
            var p = Prepare(path, displayName);
            if (p.Error == null)
            {
                Directory.CreateDirectory(LibraryFolder);
                string target = Path.Combine(LibraryFolder, p.Id + p.Ext);
                if (!File.Exists(target)) File.Copy(path, target);
            }
            return Apply(p, imported: true, LibraryFolder);
        }

        static Result Apply(Prepared p, bool imported, string folder)
        {
            if (p.Error != null) return new Result { Message = $"{p.Name}: {p.Error}" };
            var existing = Find(p.Id);
            if (existing != null)
            {
                bool wasHidden = existing.hidden || !existing.Available;
                existing.hidden = false;
                // Файл трека пропал (удалили из папки) — теперь у игры есть своя копия.
                if (!existing.Available && imported)
                {
                    existing.imported = true;
                    existing.file = Path.Combine(folder, p.Id + p.Ext);
                }
                Save();
                if (wasHidden) Changed?.Invoke();
                return new Result { Ok = true, Added = wasHidden, Track = existing, Message = wasHidden ? $"Снова в библиотеке: {existing.title}" : $"Уже в библиотеке: {existing.title}" };
            }
            if (Data.tracks.Count(t => !t.hidden) >= MaxTracks) return new Result { Message = $"В библиотеке уже {MaxTracks} треков — удалите ненужные." };
            var (artist, title) = MusicTitles.FromFileName(p.Name);
            var track = new UserTrack
            {
                id = p.Id,
                title = title,
                artist = artist,
                ext = p.Ext,
                imported = imported,
                file = imported ? Path.Combine(folder, p.Id + p.Ext) : p.Source,
                added = DateTime.UtcNow.Ticks,
            };
            Data.tracks.Add(track);
            Save();
            Changed?.Invoke();
            return new Result { Ok = true, Added = true, Track = track, Message = $"Добавлено: {(string.IsNullOrEmpty(artist) ? title : artist + " — " + title)}" };
        }

        /// Найти новые файлы в папке UserContent/Music (Windows). Знакомые файлы (путь+размер+время) не перечитываются,
        /// один и тот же трек под другим именем не дублируется, убранные из библиотеки не возвращаются.
        /// Хэши считаются в фоновом потоке; список меняется в главном.
        public static async Task<string> ScanDropFolderAsync(string folder = null)
        {
            var fresh = FindNew(folder ?? UserContentPaths.MusicDropFolder, out string error);
            if (error != null) return error;
            if (fresh.Count == 0) return null;
            var prepared = await Task.Run(() => fresh.Select(f => (f, Prepare(f.FullName, f.Name))).ToList());
            return ApplyScan(prepared);
        }

        /// То же без фонового потока (тесты).
        public static string ScanDropFolder(string folder)
        {
            var fresh = FindNew(folder, out string error);
            if (error != null) return error;
            return fresh.Count == 0 ? null : ApplyScan(fresh.Select(f => (f, Prepare(f.FullName, f.Name))).ToList());
        }

        /// Неподходящие файлы папки (битые, не тот формат) за эту сессию: не перепроверяются при каждом открытии раздела.
        static readonly HashSet<string> rejected = new HashSet<string>();

        static List<FileInfo> FindNew(string folder, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<FileInfo>();
            var known = new HashSet<string>(Data.tracks.Where(t => t.quickKey != null).Select(t => t.quickKey));
            known.UnionWith(rejected);
            try
            {
                return new DirectoryInfo(folder).GetFiles("*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant()) && !known.Contains(UserContentPaths.QuickKey(f)))
                    .ToList();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = $"Папка с музыкой недоступна: {e.Message}";
                return new List<FileInfo>();
            }
        }

        // Файлы, пропавшие из папки, не показываются (Available проверяет диск); список не чистим: вернётся файл — вернётся трек.
        static string ApplyScan(List<(FileInfo file, Prepared p)> prepared)
        {
            int added = 0, failed = 0;
            foreach (var (file, p) in prepared)
            {
                if (p.Error != null)
                {
                    failed++;
                    rejected.Add(UserContentPaths.QuickKey(file));
                    Debug.LogWarning($"[RandomCar] Своя музыка: {file.Name} пропущен — {p.Error}");
                    continue;
                }
                var existing = Find(p.Id);
                if (existing != null)
                {
                    // Тот же трек уже есть (импортом или под другим именем): запомнить файл, чтобы не читать его снова.
                    if (existing.quickKey == null && !existing.imported) existing.quickKey = UserContentPaths.QuickKey(file);
                    if (!existing.imported && !File.Exists(existing.file))
                    {
                        existing.file = file.FullName;
                        existing.quickKey = UserContentPaths.QuickKey(file);
                    }
                    continue;
                }
                var (artist, title) = MusicTitles.FromFileName(file.Name);
                Data.tracks.Add(new UserTrack
                {
                    id = p.Id,
                    title = title,
                    artist = artist,
                    ext = p.Ext,
                    imported = false,
                    file = file.FullName,
                    quickKey = UserContentPaths.QuickKey(file),
                    added = DateTime.UtcNow.Ticks,
                });
                added++;
            }
            Save();
            if (added > 0) Changed?.Invoke();
            if (added == 0 && failed == 0) return null;
            return failed > 0 ? $"Найдено в папке: {added}, не подошло: {failed} (поддерживаются OGG, MP3, WAV)" : $"Найдено в папке: {added}";
        }

        /// Убрать трек из библиотеки. Копию, сделанную игрой, удаляем; файл игрока в UserContent/Music не трогаем
        /// (трек помечается убранным и при сканировании не возвращается).
        public static string Remove(string id)
        {
            var track = Find(id);
            if (track == null) return "Трек уже убран.";
            string name = track.title;
            if (track.imported)
            {
                TryDelete(track.file);
                Data.tracks.Remove(track);
                Save();
                Changed?.Invoke();
                return $"Удалено из библиотеки: {name}";
            }
            track.hidden = true;
            Save();
            Changed?.Invoke();
            return $"Убрано из библиотеки: {name}. Файл остался в папке UserContent/Music.";
        }

        static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[RandomCar] Файл не удалён ({path}): {error.Message}");
            }
        }
    }
}
