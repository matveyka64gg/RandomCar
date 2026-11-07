using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using RandomCar.PowerUps;
using RandomCar.Race;
using RandomCar.Save;
using RandomCar.Track;
using RandomCar.UI;
using UnityEngine;

namespace RandomCar.Arena
{
    /// Что сейчас с испытанием.
    public enum DemoState { None, Running, Finished, Failed }

    /// Режим «Полигон испытаний»: отдельная карта, одна машина игрока (любая из каталога) и по желанию три машины-мишени,
    /// любое событие игры по кнопке — та же реализация, что в гонке (EventDirector.LaunchDemo), повтор, стоп, сброс.
    /// Изоляция от гонки: свободная езда (RaceManager.FreeRoam — нет отсчёта, кругов, финиша, результатов, рекордов,
    /// кредитов, сторожа продвижения), поток событий выключен (AutoLaunch = false — сами события не появляются, цепные
    /// трюки не запускаются), ящиков «?» нет. На выходе всё возвращается как было.
    /// Неуязвимость: машина не получает последствий событий (замедления, нелепого руля, сплющивания — системы
    /// повреждений в игре нет) и не возвращается на трассу сама; физика (толчки, подбросы, кувырки, удары) — полностью.
    public sealed class TestArena : MonoBehaviour
    {
        /// Машины-мишени: сколько метров впереди машины игрока и где поперёк дороги (м) — в зоне испытаний.
        static readonly Vector2[] TargetSpots = { new Vector2(42f, -5.2f), new Vector2(60f, 5.2f), new Vector2(78f, -5.2f) };

        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;
        [SerializeField] FollowCamera follow;
        [SerializeField] ArenaCamera observer;
        PowerUpDirector powerUps;
        // Как было до входа: на выходе возвращается ровно это (а не «как обычно»).
        bool savedRecord, savedAutoLaunch;
        ChaosLevel? savedChaos;

        public bool Active { get; private set; }
        public string CarKey { get; private set; }
        public bool Invulnerable { get; private set; } = true;
        public bool Targets { get; private set; } = true;
        public bool Observing { get; private set; }
        /// Меню полигона открыто: машина стоит (управление — у меню), закрыто — едет.
        public bool MenuOpen { get; private set; }

        /// Выбранное событие и текущий запуск (тот же экземпляр пула, номер розыгрыша — чтобы отличать повтор).
        public string SelectedId { get; private set; }
        public RaceEvent Demo { get; private set; }
        public int DemoSerial { get; private set; }
        public float DemoStartedAt { get; private set; }
        public string LastError { get; private set; } = "";
        public int Launches { get; private set; }
        public RaceManager Race => race;
        public ArenaCamera Observer => observer;
        public EventDirector Director => director;

        /// Что-то поменялось (машина, режим, запуск) — меню обновляет кнопки.
        public event Action Changed;

        public DemoState State
        {
            get
            {
                if (Demo == null) return string.IsNullOrEmpty(LastError) ? DemoState.None : DemoState.Failed;
                return Demo.IsRunning && Demo.Serial == DemoSerial ? DemoState.Running : DemoState.Finished;
            }
        }

        public void Configure(RaceManager raceManager, EventDirector eventDirector, FollowCamera followCamera, ArenaCamera arenaCamera)
        {
            race = raceManager;
            director = eventDirector;
            follow = followCamera;
            observer = arenaCamera;
        }

        void Awake()
        {
            if (observer != null) observer.Focus = Focus;
        }

        // ---------- вход и выход ----------

        /// Карта полигона загружена и привязана к гонке: машина, режим, свободная езда. Старт — race.Restart().
        public void Begin()
        {
            Active = true;
            LastError = "";
            Demo = null;
            // При входе меню открыто: сразу виден список событий, машина стоит.
            MenuOpen = true;
            if (powerUps == null) powerUps = FindFirstObjectByType<PowerUpDirector>();
            if (powerUps != null) powerUps.BoxesEnabled = false;
            savedRecord = race.RecordBestTimes;
            savedAutoLaunch = director.AutoLaunch;
            savedChaos = director.ChaosOverride;
            race.FreeRoam = true;
            race.RecordBestTimes = false;
            race.FreeRoamRecovery = !Invulnerable;
            director.AutoLaunch = false;
            // Хаос меняет только частоту событий (а она здесь — по кнопке): сила и поведение те же, что в обычной гонке.
            director.ChaosOverride = ChaosLevel.Mayhem;
            if (string.IsNullOrEmpty(CarKey) || race.Catalog.Cars.All(c => c.Key != CarKey)) CarKey = GameSettings.Car;
            Spawn();
            Debug.Log($"[RandomCar] Полигон: вход, машина {CarKey}, неуязвимость {Invulnerable}, мишени {Targets}");
        }

        /// После race.Restart(): мишени стоят на ручнике, меню решает, едет ли машина игрока.
        public void AfterRestart()
        {
            ParkTargets();
            SetMenuOpen(MenuOpen);
        }

        public void End()
        {
            StopDemo();
            SetObserver(false);
            SetMenuOpen(false);
            Active = false;
            race.FreeRoam = false;
            race.FreeRoamRecovery = true;
            race.RecordBestTimes = savedRecord;
            director.AutoLaunch = savedAutoLaunch;
            director.ChaosOverride = savedChaos;
            if (powerUps != null) powerUps.BoxesEnabled = true;
            if (race.Player != null && race.Player.Controller != null) race.Player.Controller.IgnoreConsequences = false;
            Debug.Log($"[RandomCar] Полигон: выход, запусков {Launches}");
        }

        // ---------- машины ----------

        void Spawn()
        {
            var catalog = race.Catalog;
            var entry = catalog.Get(CarKey);
            CarKey = entry.Key;
            // Своя машина — со своей сборкой из гаража (если куплена), остальные — заводские.
            var build = GarageStore.OwnsCar(entry) ? GarageStore.Active(entry, catalog) : CarBuild.Default(entry);
            var list = new List<Participant>
            {
                new Participant { Name = GameSettings.PlayerName, CarKey = entry.Key, Build = build, Kind = ParticipantKind.LocalPlayer },
            };
            if (Targets)
                foreach (var car in catalog.Cars.Where(c => c.Key != entry.Key).Take(TargetSpots.Length))
                    list.Add(new Participant { Name = "Мишень: " + car.DisplayName, CarKey = car.Key, Build = CarBuild.Default(car), Kind = ParticipantKind.Ai });
            race.Spawn(list);
            var course = race.Course;
            var player = race.Player;
            int k = 0;
            foreach (var r in race.Racers)
            {
                if (r == player) continue;
                var spot = TargetSpots[Mathf.Min(k++, TargetSpots.Length - 1)];
                float p = TrackPath.Wrap(player.StartProgress + spot.x / course.Length);
                r.StartProgress = p;
                r.StartPosition = course.PointAt(p) + course.SideAt(p) * spot.y + Vector3.up * 0.3f;
                r.StartRotation = Quaternion.LookRotation(course.DirectionAt(p));
                // Мишени не ездят: водитель-ИИ не нужен. Иначе каждый «Сброс» заново строил бы ему гоночную линию
                // (~20 мс на машину — заметный рывок кадра).
                if (r.Car.TryGetComponent<AiDriver>(out var ai)) Destroy(ai);
            }
            ApplyFlags();
        }

        /// Мишени без водителя: стоят на ручнике, удары и взрывы швыряют их по-настоящему.
        void ParkTargets()
        {
            foreach (var r in race.Racers)
            {
                if (r == race.Player || r.Controller == null) continue;
                if (r.Car.TryGetComponent<AiDriver>(out var ai)) ai.enabled = false;
                r.Controller.SetInput(new CarInput { Handbrake = true });
                r.Controller.Locked = true;
            }
        }

        void ApplyFlags()
        {
            race.FreeRoamRecovery = !Invulnerable;
            if (race.Player != null && race.Player.Controller != null) race.Player.Controller.IgnoreConsequences = Invulnerable;
        }

        public void SelectCar(string key)
        {
            if (!Active || key == CarKey) return;
            CarKey = key;
            RespawnAll();
        }

        public void SetInvulnerable(bool on)
        {
            Invulnerable = on;
            ApplyFlags();
            Changed?.Invoke();
        }

        public void SetTargets(bool on)
        {
            if (Targets == on) return;
            Targets = on;
            if (Active) RespawnAll();
        }

        void RespawnAll()
        {
            StopDemo();
            Spawn();
            race.Restart();
            AfterRestart();
            Changed?.Invoke();
        }

        // ---------- испытание ----------

        /// Запустить событие перед машиной там, где она сейчас. false — не запустилось (причина в LastError).
        public bool Launch(string id)
        {
            if (!Active) return false;
            StopDemo();
            SelectedId = id;
            LastError = "";
            var ev = director.LaunchDemo(id);
            if (ev == null)
            {
                LastError = "Не запустилось: нет свободного экземпляра события или машина не на трассе.";
                Debug.LogWarning($"[RandomCar] Полигон: «{id}» не запустилось");
                Changed?.Invoke();
                return false;
            }
            Demo = ev;
            DemoSerial = ev.Serial;
            DemoStartedAt = Time.time;
            Launches++;
            if (Observing && observer != null) observer.Frame(race.Player.Car.forward);
            Debug.Log($"[RandomCar] Полигон: запуск №{Launches} «{id}» (розыгрыш {ev.Serial}), машина {CarKey}, неуязвимость {Invulnerable}, мишеней {race.Racers.Count - 1}");
            Changed?.Invoke();
            return true;
        }

        /// Повторить: машина и мишени на исходные места, окружение чистое, тот же запуск заново.
        public bool Repeat()
        {
            if (string.IsNullOrEmpty(SelectedId)) return false;
            ResetAll();
            return Launch(SelectedId);
        }

        /// Остановить испытание: все события и их объекты убраны (машина остаётся, где была).
        public void StopDemo()
        {
            if (director != null) director.CancelAll();
            HypergamyScreen.Instance?.Stop("испытание остановлено");
            Demo = null;
            Changed?.Invoke();
        }

        /// Сброс: события убраны, машина и мишени на исходных местах, следы шин стёрты.
        public void ResetAll()
        {
            if (!Active) return;
            StopDemo();
            race.Restart();
            AfterRestart();
            Changed?.Invoke();
        }

        /// «Удар» ждёт подъезда машины — сработать сейчас.
        public bool TriggerNow()
        {
            if (State != DemoState.Running || !Demo.WaitingForApproach) return false;
            Demo.CommitNow();
            return true;
        }

        /// ГИПЕРГАМИЯ без подъезда: коснуться карточки.
        public bool TouchNow() => State == DemoState.Running && Demo is HypergamyEvent h && h.TouchNow();

        public bool CanTriggerNow => State == DemoState.Running && Demo.WaitingForApproach;
        public bool CanTouchNow => State == DemoState.Running && Demo is HypergamyEvent h && !h.Caught;

        // ---------- камера и меню ----------

        public void SetObserver(bool on)
        {
            Observing = on && Active;
            if (observer != null)
            {
                if (Observing && !observer.enabled && race.Player != null) observer.Frame(race.Player.Car.forward);
                observer.enabled = Observing;
            }
            if (follow != null && Active) follow.enabled = !Observing;
            Changed?.Invoke();
        }

        /// Меню открыто — машина стоит на тормозах и не слушает водителя (клавиши у меню; удары событий её швыряют как
        /// обычно), закрыто — водитель снова за рулём. Педаль тормоза здесь не годится: на месте она включает задний ход.
        public void SetMenuOpen(bool open)
        {
            MenuOpen = open && Active;
            var player = race.Player;
            if (player == null || player.Car == null) return;
            if (player.Car.TryGetComponent<PlayerDriver>(out var driver)) driver.enabled = !MenuOpen;
            player.Controller.SetInput(new CarInput());
            player.Controller.Locked = MenuOpen;
        }

        /// Куда смотрит камера обзора: на событие, пока оно идёт, иначе — на место в 55 м перед машиной.
        public Vector3 Focus()
        {
            if (State == DemoState.Running && Demo.Center != Vector3.zero) return Demo.Center;
            var player = race.Player;
            if (player == null || race.Course == null) return Vector3.zero;
            float p = TrackPath.Wrap(player.Progress + 55f / race.Course.Length);
            return race.Course.PointAt(p);
        }

        void Update()
        {
            if (!Active) return;
            // Камера машины включается и меню, и паузой — пока идёт обзор, она выключена.
            if (Observing && follow != null && follow.enabled) follow.enabled = false;
        }
    }
}
