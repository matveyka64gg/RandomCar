using System;
using System.Collections;
using System.Linq;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Core
{
    /// Ключи запуска для проверок собранной игры без человека за клавиатурой (игрой в обычном запуске не используются):
    /// -rc-map Ключ — карта; -rc-race — сразу одиночная гонка, свою машину ведёт ИИ (автопилот), рекорды и кредиты
    /// не пишутся; -rc-chaos 0|1|2 — Хаос на эту сессию; -rc-fpslog — каждые 10 с в лог: средний и минимальный FPS,
    /// память; -rc-arena-check [кругов] — полигон испытаний: все события по очереди, итоги в лог.
    /// Сетевые ключи (-rc-host, -rc-join) разбирает NetSession.
    public sealed class DevCommandLine : MonoBehaviour
    {
        [SerializeField] GameFlow flow;

        public void Configure(GameFlow gameFlow) => flow = gameFlow;

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("-rc-fpslog")) StartCoroutine(FpsLog());
            if (args.Contains("-rc-race")) StartCoroutine(Race(args));
            // Свой контент игрока (бета 0.1): папки UserContent, окно выбора файлов (-rc-content-dialog), воспроизведение своего трека.
            if (args.Contains("-rc-content-check")) StartCoroutine(ContentCheck(args));
            // Полигон испытаний сразу после меню (проверка интерфейса на разных разрешениях): -rc-arena [-rc-arena-event id]
            // [-rc-shot путь.png] — снимок экрана вместе с интерфейсом через 5 с.
            if (args.Contains("-rc-arena")) StartCoroutine(ArenaShot(args));
            // Полигон испытаний: все события по очереди на автопилоте, итоги и FPS в лог (ArenaSelfCheck).
            if (args.Contains("-rc-arena-check"))
            {
                int rounds = int.TryParse(Value(args, "-rc-arena-check"), out int r) ? Mathf.Clamp(r, 1, 20) : 1;
                // -rc-arena-only id,id,… — только эти события (нагрузочный прогон повторами).
                StartCoroutine(Arena.ArenaSelfCheck.Run(flow, rounds, 22f, Value(args, "-rc-arena-only")));
            }
        }

        static string Value(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : null;
        }

        IEnumerator Race(string[] args)
        {
            while (flow.State != FlowState.Menu) yield return null;
            string map = Value(args, "-rc-map");
            if (map != null && flow.Maps.Get(map) != null && flow.Maps.Get(map).Key != GameSettings.Map)
            {
                flow.SelectMap(map);
                while (flow.State != FlowState.Menu) yield return null;
            }
            string chaos = Value(args, "-rc-chaos");
            if (chaos != null && int.TryParse(chaos, out int level) && flow.Director != null) flow.Director.ChaosOverride = (Events.ChaosLevel)Mathf.Clamp(level, 0, (int)Events.ChaosLevel.Absurd);
            flow.Race.RecordBestTimes = false;
            // -rc-noboxes — без ящиков бонусов (проверка самой трассы).
            if (args.Contains("-rc-noboxes") && PowerUps.PowerUpDirector.Current != null) PowerUps.PowerUpDirector.Current.BoxesEnabled = false;
            var telemetry = flow.Race.GetComponent<RandomCar.Race.RaceTelemetry>();
            if (telemetry == null) telemetry = flow.Race.gameObject.AddComponent<RandomCar.Race.RaceTelemetry>();
            flow.StartRace();
            yield return null;
            var me = flow.Race.Player;
            if (me == null) yield break;
            if (me.Car.TryGetComponent<PlayerDriver>(out var driver)) driver.enabled = false;
            var ai = me.Car.gameObject.AddComponent<AiDriver>();
            ai.Init(flow.Race.Course, flow.Race.Cars, AiSkill.ForDifficulty(1, 0.5f), 17);
            Debug.Log($"[RandomCar] Проверка: гонка на {flow.Race.Map.Key}, автопилот, Хаос {flow.Director.Intensity.Chaos}, кругов {flow.Race.TotalLaps}, трасса {flow.Race.Course.Length:F0} м");
            // Итог гонки в лог; -rc-exit — выйти из игры после финиша (пакетная проверка нескольких карт).
            while (flow.Race.Phase != RandomCar.Race.RacePhase.Finished) yield return null;
            Debug.Log($"[RandomCar] Проверка: финиш на {flow.Race.Map.Key}, кругов {flow.Race.TotalLaps}; {telemetry.Report()}");
            if (args.Contains("-rc-exit"))
            {
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit();
            }
        }

        IEnumerator ContentCheck(string[] args)
        {
            while (flow.State != FlowState.Menu) yield return null;
            var scan = UserContent.UserMusicLibrary.ScanDropFolderAsync();
            while (!scan.IsCompleted) yield return null;
            Debug.Log($"[RandomCar] Проверка контента: папка музыки «{UserContent.UserContentPaths.MusicDropFolder}» → {scan.Result ?? "новых нет"}");
            string decals = UserContent.UserDecalFolder.Scan();
            Debug.Log($"[RandomCar] Проверка контента: папка наклеек «{UserContent.UserContentPaths.DecalDropFolder}» → {decals ?? "новых нет"}");
            if (args.Contains("-rc-content-dialog") && UI.FileDialog.Supported)
            {
                var files = UI.FileDialog.OpenAudio("Проверка: своя музыка");
                Debug.Log($"[RandomCar] Проверка контента: окно Windows вернуло файлов {files.Count}: {string.Join(" | ", files)}");
                foreach (var f in files)
                {
                    var import = UserContent.UserMusicLibrary.ImportAsync(f);
                    while (!import.IsCompleted) yield return null;
                    Debug.Log($"[RandomCar] Проверка контента: импорт музыки → {import.Result.Message}");
                }
                string image = UI.FileDialog.OpenImage("Проверка: картинка для наклейки");
                if (image != null)
                {
                    var r = Car.CustomDecals.Import(image);
                    Debug.Log($"[RandomCar] Проверка контента: импорт картинки → {(r.Ok ? "добавлена " + r.Id : r.Error)}");
                }
            }
            var tracks = UserContent.UserMusicLibrary.Tracks;
            Debug.Log($"[RandomCar] Проверка контента: своих треков {tracks.Count}: {string.Join(" | ", tracks.Select(t => $"{t.artist} - {t.title} ({(t.imported ? "копия игры" : "из папки")})"))}");
            var ids = Car.CustomDecals.List();
            Debug.Log($"[RandomCar] Проверка контента: своих картинок {ids.Count}; загружаются: {ids.Count(id => Car.CustomDecals.Load(id) != null)}");
            var player = Audio.MusicPlayer.Instance;
            if (player != null && tracks.Count > 0)
            {
                var track = player.UserTrack(tracks[tracks.Count - 1].id);
                player.Play(track);
                float until = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < until && (player.Current != track || !player.IsPlaying)) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                Debug.Log($"[RandomCar] Проверка контента: играет «{player.Current?.Title}», свой={player.Current?.IsUser}, клип {(player.Current?.Clip != null ? player.Current.Clip.length.ToString("F1") + " с" : "не загружен")}, " +
                          $"идёт={player.IsPlaying}, позиция {player.Progress:P0}, треков в плейлисте {player.TrackCount}");
            }
            if (args.Contains("-rc-exit"))
            {
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit();
            }
        }

        IEnumerator ArenaShot(string[] args)
        {
            while (flow.State != FlowState.Menu) yield return null;
            flow.EnterArena();
            while (!flow.InArena || flow.State != FlowState.Racing) yield return null;
            string ev = Value(args, "-rc-arena-event");
            if (ev != null)
            {
                yield return new WaitForSeconds(1f);
                flow.Arena.Launch(ev);
            }
            string shot = Value(args, "-rc-shot");
            if (shot == null) yield break;
            yield return new WaitForSeconds(5f);
            ScreenCapture.CaptureScreenshot(shot);
            Debug.Log($"[RandomCar] Проверка: снимок полигона {Screen.width}×{Screen.height} → {shot}");
        }

        IEnumerator FpsLog()
        {
            while (true)
            {
                float start = Time.realtimeSinceStartup;
                int frames = 0;
                float worst = 0f;
                while (Time.realtimeSinceStartup - start < 10f)
                {
                    frames++;
                    worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                    yield return null;
                }
                float elapsed = Time.realtimeSinceStartup - start;
                long memory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                Debug.Log($"[RandomCar] FPS: средний {frames / elapsed:F0}, минимальный {1f / Mathf.Max(0.0001f, worst):F0}, память {memory} МБ, " +
                          $"экран {Screen.width}×{Screen.height}, сцена {flow.State}");
            }
        }
    }
}
