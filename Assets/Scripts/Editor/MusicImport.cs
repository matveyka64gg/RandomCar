using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RandomCar.Audio;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RandomCar.EditorTools
{
    /// Музыка и звуковые файлы проекта.
    /// - Папки: Assets/Art/Audio/Music (корень — общий плейлист, подпапка — плейлист карты),
    ///   Assets/Art/Audio/Sfx (звуковые эффекты: сирена и т. п.). Музыку и эффекты не смешиваем.
    /// - Форматы, которые Unity не импортирует (m4a, aac, flac, opus, wma, webm), перекодируются ffmpeg в Ogg Vorbis
    ///   рядом с исходником; исходник переносится в Assets/Art/Audio/Originals~ (Unity такие папки не импортирует).
    /// - Файлы со словами «сирена», «siren», «alarm», «тревога» в имени из папки музыки переносятся в Sfx.
    /// - Настройки импорта: музыка — потоковая (Streaming, Vorbis), эффекты — сжатые в памяти.
    /// - MusicLibrary собирается автоматически при изменении файлов в Assets/Art/Audio.
    public sealed class MusicImport : AssetPostprocessor
    {
        public const string AudioRoot = "Assets/Art/Audio";
        public const string MusicRoot = AudioRoot + "/Music";
        public const string SfxRoot = AudioRoot + "/Sfx";
        public const string OriginalsRoot = AudioRoot + "/Originals~";
        public const string LibraryPath = "Assets/Settings/MusicLibrary.asset";
        const float TargetLufs = -15f;

        static readonly string[] Convertible = { ".m4a", ".aac", ".flac", ".opus", ".wma", ".webm", ".mp4", ".m4b", ".alac" };
        static readonly string[] SfxWords = { "сирен", "siren", "alarm", "тревог" };
        static bool pending;

        void OnPreprocessAudio()
        {
            var importer = (AudioImporter)assetImporter;
            if (assetPath.StartsWith(MusicRoot + "/"))
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.55f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.loadInBackground = true;
            }
            else if (assetPath.StartsWith(SfxRoot + "/Events/"))
            {
                // Озвучка событий (крик «EL DRIFTO!» — файл пользователя): без сжатия с потерями, распакована заранее,
                // чтобы звучала как оригинал и начиналась без задержки.
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.loadInBackground = false;
            }
            else if (assetPath.StartsWith(SfxRoot + "/"))
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
            }
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p => p.StartsWith(AudioRoot + "/"));
            if (!touched || pending) return;
            pending = true;
            EditorApplication.delayCall += () =>
            {
                pending = false;
                Refresh();
            };
        }

        [MenuItem("RandomCar/Audio/Update Music Library")]
        public static MusicLibrary RefreshFromMenu() => Refresh();

        [MenuItem("RandomCar/Audio/Reset Track Names")]
        public static void ResetNames()
        {
            var library = Refresh();
            foreach (var t in library.Tracks) ApplyAutoName(t, AssetDatabase.GetAssetPath(t.Clip));
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        /// Полная обработка папки звука: перекодировка, перенос эффектов, сборка библиотеки.
        public static MusicLibrary Refresh()
        {
            ArtLibrary.EnsureFolder(MusicRoot);
            ArtLibrary.EnsureFolder(SfxRoot);
            bool changed = ConvertUnsupported();
            changed |= MoveSoundEffects();
            if (changed) AssetDatabase.Refresh();
            return BuildLibrary();
        }

        // ---------- перекодировка ----------

        static bool ConvertUnsupported()
        {
            var sources = Directory.GetFiles(AudioRoot, "*.*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("~/") && Convertible.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .ToList();
            if (sources.Count == 0) return false;
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                Debug.LogWarning($"[RandomCar] {sources.Count} аудиофайлов в формате, который Unity не импортирует (например, {Path.GetFileName(sources[0])}). " +
                                 "Установите ffmpeg (winget install ffmpeg) или перекодируйте их в .ogg / .mp3 / .wav вручную.");
                return false;
            }
            bool any = false;
            try
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    string src = sources[i];
                    EditorUtility.DisplayProgressBar("RandomCar: музыка", $"Перекодировка {Path.GetFileName(src)}", (float)i / sources.Count);
                    string ogg = Path.ChangeExtension(src, ".ogg");
                    if (!File.Exists(ogg))
                    {
                        var (code, output) = Run(ffmpeg, $"-hide_banner -loglevel error -y -i \"{src}\" -vn -map_metadata -1 -c:a libvorbis -q:a 6 \"{ogg}\"");
                        if (code != 0 || !File.Exists(ogg))
                        {
                            Debug.LogError($"[RandomCar] Не удалось перекодировать {src}: {output}");
                            continue;
                        }
                    }
                    // Исходник — в Originals~ (папки с ~ Unity не импортирует), с той же относительной структурой.
                    string relative = src.Substring(AudioRoot.Length + 1);
                    string keep = Path.Combine(OriginalsRoot, relative).Replace('\\', '/');
                    Directory.CreateDirectory(Path.GetDirectoryName(keep));
                    if (File.Exists(keep)) File.Delete(src);
                    else File.Move(src, keep);
                    if (File.Exists(src + ".meta")) File.Delete(src + ".meta");
                    Debug.Log($"[RandomCar] Перекодировано: {relative} → {Path.GetFileName(ogg)} (исходник: {keep})");
                    any = true;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return any;
        }

        /// ffmpeg из PATH или из стандартного места winget.
        static string FindFfmpeg()
        {
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
            foreach (string dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        static (int code, string output) Run(string exe, string args)
        {
            var info = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using var process = Process.Start(info);
            var stdout = process.StandardOutput.ReadToEndAsync();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stderr + stdout.Result);
        }

        // ---------- звуковые эффекты из папки музыки ----------

        static bool MoveSoundEffects()
        {
            bool any = false;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (!SfxWords.Any(name.Contains)) continue;
                // Подпапка сохраняется: Music/TelAviv/сирена.mp3 → Sfx/TelAviv/сирена.mp3.
                string sub = Path.GetDirectoryName(path).Replace('\\', '/').Substring(MusicRoot.Length).TrimStart('/');
                string folder = sub.Length > 0 ? $"{SfxRoot}/{sub}" : SfxRoot;
                ArtLibrary.EnsureFolder(folder);
                string target = $"{folder}/{Path.GetFileName(path)}";
                string error = AssetDatabase.MoveAsset(path, target);
                if (string.IsNullOrEmpty(error))
                {
                    // Перенос не переимпортирует файл: применяем настройки эффектов (не потоковые).
                    AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                    Debug.Log($"[RandomCar] Звуковой эффект перенесён из музыки: {path} → {target}");
                    any = true;
                }
                else Debug.LogError($"[RandomCar] Не удалось перенести {path}: {error}");
            }
            return any;
        }

        // ---------- библиотека ----------

        static MusicLibrary BuildLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<MusicLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            var old = library.Tracks.Where(t => t != null && !string.IsNullOrEmpty(t.Id)).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var tracks = new List<MusicTrack>();
            string ffmpeg = FindFfmpeg();
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                string folder = Path.GetDirectoryName(path).Replace('\\', '/');
                string playlist = folder == MusicRoot ? MusicLibrary.MainPlaylist : folder.Substring(MusicRoot.Length + 1).Split('/')[0];
                var track = old.TryGetValue(guid, out var existing) ? existing : new MusicTrack { Id = guid };
                track.Clip = clip;
                track.Playlist = playlist;
                if (string.IsNullOrWhiteSpace(track.Title)) ApplyAutoName(track, path);
                if (track.Loudness == 0f && ffmpeg != null) track.Loudness = MeasureLoudness(ffmpeg, path);
                track.Gain = track.Loudness < 0f ? Mathf.Clamp(Mathf.Pow(10f, (TargetLufs - track.Loudness) / 20f), 0.1f, 1f) : 1f;
                tracks.Add(track);
            }
            library.Tracks = tracks.OrderBy(t => t.Playlist == MusicLibrary.MainPlaylist ? 0 : 1).ThenBy(t => t.Playlist)
                .ThenBy(t => Path.GetFileName(AssetDatabase.GetAssetPath(t.Clip)), StringComparer.OrdinalIgnoreCase).ToList();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            var summary = library.Tracks.GroupBy(t => t.Playlist).Select(g => $"{g.Key}: {g.Count()}");
            Debug.Log($"[RandomCar] Музыкальная библиотека: {library.Tracks.Count} треков ({string.Join(", ", summary)})");
            return library;
        }

        static void ApplyAutoName(MusicTrack track, string path)
        {
            var (artist, title) = MusicTitles.FromFileName(Path.GetFileName(path));
            track.Artist = artist;
            track.Title = title;
        }

        /// Интегральная громкость по EBU R128 (LUFS) — чтобы громкие и тихие треки звучали ровно.
        static float MeasureLoudness(string ffmpeg, string path)
        {
            var (code, output) = Run(ffmpeg, $"-hide_banner -nostats -i \"{Path.GetFullPath(path)}\" -filter_complex ebur128=framelog=quiet -f null -");
            var match = Regex.Matches(output, @"I:\s+(-?\d+(?:\.\d+)?)\s+LUFS").Cast<Match>().LastOrDefault();
            if (code != 0 || match == null)
            {
                Debug.LogWarning($"[RandomCar] Громкость {Path.GetFileName(path)} не измерена: {output.Trim()}");
                return 0f;
            }
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
