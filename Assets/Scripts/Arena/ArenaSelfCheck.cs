using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Arena
{
    /// Проверка полигона без человека: по очереди каждое событие игры через кнопки полигона (сброс → запуск), машину
    /// ведёт автопилот (въезжает в зону, как игрок), «Сработать сейчас» и «Коснуться картинки» нажимаются, если событие
    /// их ждёт. На каждое событие — строка в лог: чем кончилось, какие стадии прошло, что досталось машине, сколько
    /// ошибок; на круг — FPS, память и сколько осталось тел событий, событий и играющих звуков (утечки при повторах).
    /// Запуск: ключ сборки -rc-arena-check [кругов] или из редактора: flow.StartCoroutine(ArenaSelfCheck.Run(flow, 1)).
    public static class ArenaSelfCheck
    {
        public static int Failures { get; private set; }
        public static bool Running { get; private set; }

        public static IEnumerator Run(GameFlow flow, int rounds = 1, float maxSeconds = 22f, string only = null)
        {
            Running = true;
            Failures = 0;
            int errors = 0;
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Exception || type == LogType.Error) errors++;
            }
            Application.logMessageReceived += OnLog;
            int recoveries = 0;
            void OnRecovered(RandomCar.Race.Racer r) => recoveries++;
            flow.Race.Recovered += OnRecovered;
            try
            {
                while (flow.State != FlowState.Menu && !flow.InArena) yield return null;
                if (!flow.InArena) flow.EnterArena();
                while (!flow.InArena || flow.State != FlowState.Racing) yield return null;
                var arena = flow.Arena;
                arena.SetMenuOpen(false);
                var ids = flow.Director.Definitions.Select(d => d.Id).Where(id => only == null || only.Split(',').Contains(id)).ToList();
                Debug.Log($"[RandomCar] Полигон-проверка: событий {ids.Count}, кругов {rounds}, машина {arena.CarKey}, неуязвимость {arena.Invulnerable}");
                for (int round = 1; round <= rounds; round++)
                {
                    int frames = 0;
                    float worst = 0f, started = Time.realtimeSinceStartup;
                    int ok = 0;
                    foreach (var id in ids)
                    {
                        arena.ResetAll();
                        yield return null;
                        var player = arena.Race.Player;
                        var car = player.Controller;
                        if (player.Car.TryGetComponent<PlayerDriver>(out var driver)) driver.enabled = false;
                        var ai = player.Car.GetComponent<AiDriver>() ?? player.Car.gameObject.AddComponent<AiDriver>();
                        ai.enabled = true;
                        ai.Init(arena.Race.Course, arena.Race.Cars, AiSkill.ForDifficulty(1, 0.5f), 17);
                        int effects0 = car.EffectsReceived, launches0 = car.Launches, ignored0 = car.IgnoredConsequences, errors0 = errors;
                        int targets0 = arena.Race.Racers.Where(r => r != player).Sum(r => r.Controller.Launches + r.Controller.EffectsReceived);
                        int recovered0 = recoveries;
                        if (!arena.Launch(id))
                        {
                            Failures++;
                            Debug.LogWarning($"[RandomCar] Полигон-проверка: {id}: НЕ ЗАПУСТИЛОСЬ — {arena.LastError}");
                            continue;
                        }
                        var ev = arena.Demo;
                        float t0 = Time.time;
                        bool sawActive = false, sawEnding = false, triggered = false, touched = false;
                        while (arena.State == DemoState.Running && Time.time - t0 < maxSeconds)
                        {
                            if (ev.Stage == EventStage.Active) sawActive = true;
                            if (ev.Stage == EventStage.Ending) sawEnding = true;
                            if (!triggered && arena.CanTriggerNow && Time.time - t0 > 1.2f) triggered = arena.TriggerNow();
                            if (!touched && arena.CanTouchNow && Time.time - t0 > 2.5f) touched = arena.TouchNow();
                            frames++;
                            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                            yield return null;
                        }
                        bool finished = arena.State == DemoState.Finished;
                        float seconds = Time.time - t0;
                        if (!finished) arena.StopDemo();
                        // Гипергамия: эффект на экране держится 10 с и после конца события — снять, чтобы следующее было видно.
                        RandomCar.UI.HypergamyScreen.Instance?.Stop("проверка полигона");
                        yield return null;
                        int err = errors - errors0;
                        bool good = (finished || sawActive) && err == 0;
                        if (good) ok++;
                        else Failures++;
                        Debug.Log($"[RandomCar] Полигон-проверка: {id}: {(finished ? "закончилось" : "остановлено по времени")} за {seconds:F1} с, " +
                                  $"стадии {(sawActive ? "действие" : "—")}{(sawEnding ? "+конец" : "")}{(triggered ? ", «сработать сейчас»" : "")}{(touched ? ", «коснуться»" : "")}; " +
                                  $"машине: воздействий {car.EffectsReceived - effects0}, подбросов {car.Launches - launches0}, без последствий {car.IgnoredConsequences - ignored0}, возвратов {recoveries - recovered0}; " +
                                  $"мишеням: {arena.Race.Racers.Where(r => r != player).Sum(r => r.Controller.Launches + r.Controller.EffectsReceived) - targets0}; " +
                                  $"тел после стопа {ChaosBody.Active.Count}, событий {flow.Director.ActiveEvents.Count(e => e.IsRunning)}; ошибок {err}{(good ? "" : " — ПРОБЛЕМА")}");
                    }
                    arena.ResetAll();
                    yield return null;
                    float elapsed = Time.realtimeSinceStartup - started;
                    int sounds = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying);
                    int objects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                    long memory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                    Debug.Log($"[RandomCar] Полигон-проверка: круг {round}: успешно {ok}/{ids.Count}, FPS средний {frames / Mathf.Max(0.01f, elapsed):F0}, " +
                              $"худший кадр {1f / Mathf.Max(0.0001f, worst):F0}, память {memory} МБ, объектов {objects}, играет звуков {sounds}, " +
                              $"тел {ChaosBody.Active.Count}, событий {flow.Director.ActiveEvents.Count(e => e.IsRunning)}");
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                flow.Race.Recovered -= OnRecovered;
                Running = false;
                var p = flow.Arena != null && flow.Arena.Race.Player != null ? flow.Arena.Race.Player.Car : null;
                if (p != null)
                {
                    if (p.TryGetComponent<AiDriver>(out var ai)) Object.Destroy(ai);
                    if (p.TryGetComponent<PlayerDriver>(out var driver)) driver.enabled = true;
                }
                Debug.Log($"[RandomCar] Полигон-проверка окончена: проблем {Failures}");
            }
        }
    }
}
