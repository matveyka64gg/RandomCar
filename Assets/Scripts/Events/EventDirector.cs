using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Race;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RandomCar.Events
{
    /// Расписание событий: таймер по уровню Хаоса, взвешенный выбор, правило тяжёлых, равновероятная цель.
    /// Одновременно идёт не больше одного события. Объекты событий заранее лежат в сцене (пул по одному на тип).
    public sealed class EventDirector : MonoBehaviour
    {
        [SerializeField] RaceManager race;
        [SerializeField] EventSettings settings;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] List<RaceEvent> events = new List<RaceEvent>();
        [SerializeField] int seed = 2024;

        readonly Dictionary<string, RaceEvent> byId = new Dictionary<string, RaceEvent>();
        EventScheduler scheduler;
        EventContext context;
        float nextAt = float.PositiveInfinity;
        RaceEvent active;

        public RaceEvent Active => active != null && active.IsRunning ? active : null;
        public EventSettings Settings => settings;

        /// Последнее событие, которое началось: для интерфейса.
        public event Action<RaceEvent> EventStarted;

        public void Configure(RaceManager raceManager, EventSettings eventSettings, FollowCamera cameraFollow, List<RaceEvent> eventObjects)
        {
            race = raceManager;
            settings = eventSettings;
            followCamera = cameraFollow;
            events = eventObjects;
        }

        void Awake()
        {
            foreach (var e in events)
            {
                if (e == null) continue;
                byId[e.Id] = e;
                e.gameObject.SetActive(false);
            }
            scheduler = new EventScheduler(settings.Events, seed);
            context = new EventContext { Race = race, Track = race.Course, Camera = followCamera, Random = scheduler, Director = this };
            race.RaceRestarted += OnRestart;
            race.RaceStarted += OnStart;
            race.RaceFinished += CancelAll;
        }

        void OnDestroy()
        {
            if (race == null) return;
            race.RaceRestarted -= OnRestart;
            race.RaceStarted -= OnStart;
            race.RaceFinished -= CancelAll;
        }

        void OnRestart()
        {
            CancelAll();
            nextAt = float.PositiveInfinity;
        }

        void OnStart() => nextAt = Time.time + settings.StartGrace + scheduler.NextDelay(GameSettings.Chaos, settings.ModerateInterval, settings.ManyInterval) * 0.5f;

        public void CancelAll()
        {
            foreach (var e in byId.Values) e.Cancel();
            active = null;
        }

        void Update()
        {
            if (race.Phase != RacePhase.Running) return;
            HandleDebugKeys();
            if (Active != null) return;
            if (GameSettings.Chaos == ChaosLevel.Off) return;
            if (Time.time < nextAt) return;

            var definition = scheduler.Pick();
            var target = race.Racers[scheduler.PickTarget(race.Racers.Count)];
            Launch(definition, target);
        }

        /// Принудительный запуск (отладка и проверки): событие по id, цель по индексу участника.
        public bool Force(string id, int targetIndex)
        {
            if (race.Phase != RacePhase.Running || !byId.ContainsKey(id)) return false;
            if (Active != null) active.Cancel();
            var definition = settings.Events.First(d => d.Id == id);
            var target = race.Racers[Mathf.Clamp(targetIndex, 0, race.Racers.Count - 1)];
            Launch(definition, target);
            return true;
        }

        void Launch(EventDefinition definition, Racer target)
        {
            if (!byId.TryGetValue(definition.Id, out var ev))
            {
                Debug.LogWarning($"[RandomCar] событие {definition.Id} не подготовлено в сцене");
                return;
            }
            active = ev;
            ev.Begin(definition, target, context);
            NotifyAi(ev, definition);
            EventStarted?.Invoke(ev);

            float duration = definition.WarningSeconds + definition.ActiveSeconds + 1.5f;
            float delay = scheduler.NextDelay(GameSettings.Chaos, settings.ModerateInterval, settings.ManyInterval);
            nextAt = Time.time + Mathf.Max(delay, duration);
        }

        /// ИИ видит то же предупреждение, что и игрок: объезжает опасность, едет в полезную зону.
        void NotifyAi(RaceEvent ev, EventDefinition definition)
        {
            float until = Time.time + definition.WarningSeconds + definition.ActiveSeconds;
            foreach (var racer in race.Racers)
            {
                if (racer.IsPlayer) continue;
                var ai = racer.Car.GetComponent<AiDriver>();
                if (ai != null) ai.ReactToHazard(ev, definition.IsGood, until);
            }
        }

        /// Отладка и автопроверки: остановить время, когда активное событие дойдёт до стадии и прогресса.
        public void DebugPauseAt(EventStage stage, float progress)
        {
            debugPauseStage = stage;
            debugPauseProgress = progress;
        }

        EventStage debugPauseStage = EventStage.Idle;
        float debugPauseProgress;

        void LateUpdate()
        {
            if (debugPauseStage == EventStage.Idle || active == null) return;
            if (active.Stage == debugPauseStage && active.StageProgress >= debugPauseProgress)
            {
                Time.timeScale = 0f;
                debugPauseStage = EventStage.Idle;
            }
        }

        void HandleDebugKeys()
        {
            if (!Debug.isDebugBuild || Keyboard.current == null) return;
            // F1–F12: принудительное событие на машину игрока.
            for (int i = 0; i < settings.Events.Count && i < 12; i++)
            {
                var key = Keyboard.current[Key.F1 + i];
                if (key != null && key.wasPressedThisFrame)
                {
                    int player = race.Racers.ToList().IndexOf(race.Player);
                    Force(settings.Events[i].Id, player);
                }
            }
        }
    }
}
