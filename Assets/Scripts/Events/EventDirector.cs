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
    /// Расписание событий (хаос v4). События ставятся на трассе **впереди живых игроков** (по очереди, если их
    /// несколько) на расстоянии, которое игрок проедет за предупреждение плюс запас на реакцию: он успевает
    /// увидеть событие и въезжает в него, пока оно идёт («удары» ждут его подъезда, «зоны» держатся, пока он
    /// не проедет). Действует событие на всех в зоне — ИИ и другие игроки получают то же самое.
    ///
    /// Поверх расписания — система активных событий и комбинаций: директор знает, что идёт на трассе
    /// (категории, перекрытая ширина дороги, физические тела), и сборщик (ChaosComposer) добавляет к событию
    /// партнёра «поверх участка» или «сразу за ним» по правилам совместимости (ChaosRules) либо собирает
    /// сцену из рецепта («Боулинг на минном поле»). Накал (EventIntensity) растёт от круга к кругу: чаще встречи,
    /// больше одновременных, больше комбинаций, новые события открываются; на последнем круге — финальная сцена
    /// и салют. Хаос — база частоты, «Безумие» — ещё и редкие события и больше всего сразу.
    /// В сетевой игре директор работает только на хосте (клиенты получают Launch с тем же местом, сидом и номером).
    public sealed class EventDirector : MonoBehaviour
    {
        const float RetryDelay = 0.5f;
        const float StepDelay = 0.35f;

        /// Отложенный шаг комбинации: какое событие и к какому розыгрышу его прицепить.
        sealed class PendingStep
        {
            public EventDefinition Definition;
            public ComboLink Link;
            public RaceEvent Base;
            public float At;
            public string Title;
            public int Tries;
        }

        [SerializeField] RaceManager race;
        [SerializeField] EventSettings settings;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] List<RaceEvent> events = new List<RaceEvent>();
        [SerializeField] int seed = 2024;

        readonly Dictionary<string, List<RaceEvent>> pools = new Dictionary<string, List<RaceEvent>>();
        readonly List<RaceEvent> active = new List<RaceEvent>();
        readonly List<RacerState> racerStates = new List<RacerState>();
        readonly List<EventZone> zones = new List<EventZone>();
        readonly List<HazardInfo> hazards = new List<HazardInfo>();
        readonly List<RoadBlock> blocks = new List<RoadBlock>();
        readonly List<PendingStep> steps = new List<PendingStep>();
        readonly List<ChainRequest> chainRequests = new List<ChainRequest>();
        EventScheduler scheduler;
        EventScheduler gagScheduler;
        System.Random gagRandom;
        float nextGagAt = float.PositiveInfinity;
        int gagTurn;
        ChaosComposer composer;
        EventPlacer placer;
        LanePicker lanes;
        EventContext context;
        System.Random showRandom;
        float nextAt = float.PositiveInfinity;
        float quietFor;
        int focusTurn;
        int lastWaveLap;
        int serial;
        bool finaleDone;
        float nextShowAt = float.PositiveInfinity;

        /// Текущий директор сцены (ИИ спрашивает у него опасности впереди). Сбрасывается в OnDestroy.
        public static EventDirector Current { get; private set; }

        public RaceManager Race => race;

        /// Цепная реакция v5: сильно подброшенная машина — на месте её приземления появляется новый трюк.
        sealed class ChainRequest
        {
            public int Slot;
            public float Progress;
            public float Lateral;
            public int Generation;
            public float At;
        }

        /// Трюки, которые могут появиться на месте приземления (событие вызывает событие).
        static readonly string[] LandingGags = { "spring", "popmine", "gum", "oil", "popmine", "spring" };

        /// Сколько трюков (v5) и событий, вызванных другими событиями, было за гонку (проверка).
        public int GagCount { get; private set; }
        public int ChainEventCount { get; private set; }

        /// Накал гонки для интерфейса и проверок.
        public EventIntensity Intensity { get; private set; } = new EventIntensity(0f, ChaosLevel.Moderate);

        /// Опасности и бонусы на дороге в этом кадре (для ИИ).
        public IReadOnlyList<HazardInfo> Hazards => hazards;

        /// Перекрытые участки дороги в этом кадре (проверка, что проезд остаётся).
        public IReadOnlyList<RoadBlock> Blocks => blocks;

        /// Новый круг поднял накал (номер круга, ступень накала): плашка «Круг 2 — жарче».
        public event Action<int, int> WaveStarted;

        /// Собрана комбинация или сцена: название и число событий в ней (плашка «КОМБО: …»).
        public event Action<string, int> ComboStarted;

        /// Последний круг: финальная сцена и салют.
        public event Action FinaleStarted;

        /// Сигнал события (мина задета, бочка взорвалась): для сети.
        public event Action<RaceEvent, int, int, Vector3> SignalRaised;

        /// Идущие события (предупреждение, действие, завершение).
        public IReadOnlyList<RaceEvent> ActiveEvents => active;

        /// Все события игры (обычные, трюки, шоу) — для меню полигона испытаний.
        public IReadOnlyList<EventDefinition> Definitions => settings.Events;

        public EventSettings Settings => settings;

        /// Только хост (или одиночная игра) сам запускает события.
        public bool Authoritative { get; set; } = true;

        /// Отладка и проверки: false — расписание не запускает событий само (только Force/ForceRecipe).
        public bool AutoLaunch { get; set; } = true;

        /// Хаос сетевой гонки (его выбирает хост в лобби); null — из настроек игрока.
        public ChaosLevel? ChaosOverride { get; set; }

        ChaosLevel Chaos => ChaosOverride ?? GameSettings.Chaos;

        /// Сколько физических тел событий сейчас на трассе (бюджет физики).
        public int BodiesInUse => ChaosBody.Active.Count;

        /// Событие началось: интерфейс, звук, сеть.
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
                if (!pools.TryGetValue(e.Id, out var list)) pools[e.Id] = list = new List<RaceEvent>();
                list.Add(e);
                e.gameObject.SetActive(false);
            }
            // Два потока: обычные события (участки трассы) и трюки v5 (мгновенные происшествия рядом с машиной).
            // Три потока: обычные события, трюки v5 и шоу-события v6 (Верити, EL DRIFTO BUS).
            scheduler = new EventScheduler(settings.Events.Where(d => !d.Gag && !d.Spectacle).ToList(), seed);
            gagScheduler = new EventScheduler(settings.Events.Where(d => d.Gag).ToList(), seed + 7);
            spectacleScheduler = new EventScheduler(settings.Events.Where(d => d.Spectacle).ToList(), seed + 11);
            spectacleRandom = new System.Random(seed + 12);
            gagRandom = new System.Random(seed + 8);
            composer = new ChaosComposer(settings.Events.Where(d => !d.Gag && !d.Spectacle).ToList(), seed + 3);
            lanes = new LanePicker(seed + 2);
            showRandom = new System.Random(seed + 4);
            context = new EventContext { Race = race, Camera = followCamera, Random = scheduler, Director = this };
            Current = this;
            race.RaceRestarted += OnRestart;
            race.RaceStarted += OnStart;
            race.RaceFinished += OnFinished;
            CarController.Launched += OnCarLaunched;
        }

        static float TrackPathIndex(float progress, int count) => Track.TrackPath.Wrap(progress) * count % count;

        void OnDestroy()
        {
            if (Current == this) Current = null;
            CarController.Launched -= OnCarLaunched;
            if (race == null) return;
            race.RaceRestarted -= OnRestart;
            race.RaceStarted -= OnStart;
            race.RaceFinished -= OnFinished;
        }

        void OnRestart()
        {
            CancelAll();
            nextAt = float.PositiveInfinity;
            nextGagAt = float.PositiveInfinity;
            nextShowAt = float.PositiveInfinity;
            nextSpectacleAt = float.PositiveInfinity;
            quietFor = 0f;
            GagCount = ChainEventCount = 0;
            lastWaveLap = 0;
            finaleDone = false;
            Intensity = new EventIntensity(0f, Chaos);
            BindTrack();
        }

        void OnFinished()
        {
            CancelAll();
            // Финиш — салют впереди игрока (только украшение).
            if (Chaos != ChaosLevel.Off && race.Player != null && ChaosFx.Instance != null && race.Course != null)
            {
                float p = Track.TrackPath.Wrap(race.Player.Progress + 70f / race.Course.Length);
                ChaosFx.Instance.Volley(race.Course.PointAt(p), race.Course.DirectionAt(p), race.Course.SideAt(p), race.Course.HalfWidth, 8, 40f, showRandom);
            }
        }

        /// Трасса текущей карты: расстановщик и контекст событий пересоздаются при смене карты.
        void BindTrack()
        {
            var course = race.Course;
            if (course == null || context.Track == course) return;
            context.Track = course;
            placer = new EventPlacer(course.Length, p => Mathf.Abs(course.CurvatureAt(Mathf.FloorToInt(TrackPathIndex(p, course.Count)), 4)), new System.Random(seed + 1))
            {
                RoadHalf = course.HalfWidth,
                Wild = Chaos != ChaosLevel.Off,
            };
        }

        void OnStart()
        {
            ChaosPower.Set(Chaos);
            float grace = settings.Grace(Chaos);
            nextAt = Time.time + grace;
            // Трюки «безумия» — с первых секунд, даже пока машины разъезжаются со старта.
            nextGagAt = Time.time + grace + 0.3f;
            nextShowAt = Time.time + (Chaos >= ChaosLevel.Mayhem ? 6f : 20f);
            nextSpectacleAt = Time.time + settings.FirstSpectacle(Chaos);
        }

        float Delay()
        {
            var range = settings.Interval(Chaos);
            return Chaos == ChaosLevel.Off ? float.PositiveInfinity : scheduler.Range(range.x, range.y);
        }

        float GagDelay()
        {
            var range = settings.GagInterval(Chaos);
            return Chaos == ChaosLevel.Off ? float.PositiveInfinity : Mathf.Lerp(range.x, range.y, (float)gagRandom.NextDouble());
        }

        public void CancelAll()
        {
            foreach (var list in pools.Values)
                foreach (var e in list) e.Cancel();
            active.Clear();
            steps.Clear();
            chainRequests.Clear();
        }

        void Update()
        {
            active.RemoveAll(e => !e.IsRunning);
            hazards.Clear();
            blocks.Clear();
            foreach (var e in active)
            {
                e.CollectHazards(hazards);
                e.CollectBlocks(blocks);
            }
            ChaosPower.Set(Chaos == ChaosLevel.Off ? ChaosLevel.Moderate : Chaos);
            if (placer != null) placer.Wild = Chaos != ChaosLevel.Off;
            if (race.Phase != RacePhase.Running) return;
            HandleDebugKeys();
            if (!Authoritative)
            {
                // Клиент сети: события присылает хост, накал, финальная плашка и салют (украшение) — по своему кругу.
                if (race.Player == null || Chaos == ChaosLevel.Off) return;
                Intensity = new EventIntensity(RaceFraction(race.Player), Chaos);
                int myLap = Mathf.Max(0, race.Player.Laps.Laps) + 1;
                if (myLap > lastWaveLap)
                {
                    if (lastWaveLap > 0 && myLap <= race.TotalLaps)
                    {
                        WaveStarted?.Invoke(myLap, Intensity.Stage);
                        if (myLap == race.TotalLaps && !finaleDone)
                        {
                            finaleDone = true;
                            FinaleStarted?.Invoke();
                            Volley(race.Player, 6, 90f);
                            nextShowAt = Time.time + 5f;
                        }
                    }
                    lastWaveLap = myLap;
                }
                UpdateShow(race.Player, myLap);
                return;
            }
            if (Chaos == ChaosLevel.Off || !AutoLaunch) return;
            BindTrack();
            CollectState();
            var focus = PickFocus(out var focusRacer, out int humans);
            if (focusRacer == null) return;
            // Накал и круги — по живому игроку (событие для ИИ не сбивает счёт кругов и волны).
            var pace = focusRacer.Human || race.Player == null ? focusRacer : race.Player;
            Intensity = new EventIntensity(RaceFraction(pace), Chaos);

            // Новый круг игрока: волна (пара или сцена) и плашка накала; последний круг — финальная сцена и салют.
            int lap = Mathf.Max(0, pace.Laps.Laps) + 1;
            if (lap > lastWaveLap)
            {
                if (lastWaveLap > 0 && lap <= race.TotalLaps)
                {
                    WaveStarted?.Invoke(lap, Intensity.Stage);
                    bool last = lap == race.TotalLaps;
                    if (last && !finaleDone) StartFinale(focus, pace);
                    else StartCombo(focus, scene: true, finale: false);
                    nextAt = Time.time + Delay() * Intensity.GapFactor;
                }
                lastWaveLap = lap;
            }

            UpdateSteps();
            UpdateShow(pace, lap);
            UpdateGags(humans);
            UpdateSpectacles();
            UpdateChains();

            quietFor = NothingAhead(focus) ? quietFor + Time.deltaTime : 0f;
            bool due = Time.time >= nextAt || quietFor >= Intensity.MaxQuiet;
            if (!due) return;
            int limit = Intensity.MaxActive(settings.MaxActive(Chaos)) + Mathf.Min(2, humans - 1);
            if (BigActive >= limit)
            {
                nextAt = Time.time + RetryDelay;
                return;
            }

            bool launched;
            if (steps.Count == 0 && composer.Roll() < Intensity.ComboChance) launched = StartCombo(focus, composer.Roll() < Intensity.SceneChance, false);
            else launched = LaunchSingle(focus) != null;
            if (!launched)
            {
                // Места нет (машины вплотную к возможным зонам), экземпляр занят или бюджет физики исчерпан.
                nextAt = Time.time + RetryDelay;
                return;
            }
            quietFor = 0f;
            focusTurn++;
            // Несколько игроков делят поток событий: каждому — свой, но в сумме не шумнее разумного.
            nextAt = Time.time + Delay() * Intensity.GapFactor / Mathf.Sqrt(Mathf.Max(1, humans));
        }

        // ---------- выбор и комбинации ----------

        /// Обычные (не трюки и не шоу) события на трассе сейчас.
        int BigActive
        {
            get
            {
                int n = 0;
                foreach (var e in active) if (!e.Definition.Gag && !e.Definition.Spectacle) n++;
                return n;
            }
        }

        int GagsActive
        {
            get
            {
                int n = 0;
                foreach (var e in active) if (e.Definition.Gag) n++;
                return n;
            }
        }

        /// Шоу-события на трассе сейчас.
        public int SpectaclesActive
        {
            get
            {
                int n = 0;
                foreach (var e in active) if (e.Definition.Spectacle) n++;
                return n;
            }
        }

        int HeavyActive
        {
            get
            {
                int n = 0;
                foreach (var e in active) if (e.Definition.IsHeavy) n++;
                return n;
            }
        }

        /// Тяжёлых уже столько, сколько разрешает Хаос (в «безумии» — до трёх сразу).
        bool HeavyFull => HeavyActive >= EventSettings.MaxHeavy(Chaos);

        /// Событие сейчас можно: открыто Хаосом и накалом, тяжёлые разрешены, есть место в бюджете физики.
        bool Allowed(EventDefinition d)
        {
            if (!d.Unlocked(Chaos, Intensity.Level)) return false;
            if (race.Course != null && !d.Fits(race.Course.HalfWidth)) return false;
            if (d.IsHeavy && (!Intensity.AllowHeavy || HeavyFull)) return false;
            if (d.Bodies > 0 && BodiesInUse + d.Bodies > settings.MaxBodies(Chaos)) return false;
            return true;
        }

        RaceEvent LaunchSingle(RacerState focus)
        {
            var definition = scheduler.Pick(HeavyFull, Allowed, Intensity.Wild);
            return definition == null ? null : TryLaunchAhead(definition, focus, 0f);
        }

        // ---------- шоу-события v6: большие представления рядом с участниками ----------

        EventScheduler spectacleScheduler;
        System.Random spectacleRandom;
        float nextSpectacleAt = float.PositiveInfinity;

        /// Сколько шоу-событий запущено за гонку (проверка).
        public int SpectacleCount { get; private set; }

        /// Шоу-событие раз в полминуты («безумие») или чаще («абсурд»): Верити или EL DRIFTO BUS — рядом с участником,
        /// чаще с живым игроком, иначе с соперником рядом с ним (чтобы игрок видел). На всех уровнях Хаоса, кроме
        /// «выключен»; уровень меняет только частоту (EventSettings.SpectacleInterval).
        void UpdateSpectacles()
        {
            if (!settings.SpectaclesOn(Chaos) || Time.time < nextSpectacleAt) return;
            if (SpectaclesActive >= settings.MaxSpectacles(Chaos))
            {
                nextSpectacleAt = Time.time + 1f;
                return;
            }
            var racer = PickSpectacleFocus();
            // Шоу не делят бюджет физических тел с обычными событиями: в «умеренно» (10 тел) мячи и бочки занимали его
            // почти всегда, и автобус (одно тело) молча выпадал из выбора. Сколько шоу сразу — ограничивает MaxSpectacles.
            // То же шоу дважды подряд не выпадает, если есть другое (три шоу чередуются).
            var last = spectacleScheduler.Last;
            var definition = racer == null ? null
                : spectacleScheduler.Pick(false, d => d != last && FreeInstance(d.Id, false) != null, true)
                  ?? spectacleScheduler.Pick(false, d => FreeInstance(d.Id, false) != null, true);
            if (definition == null || LaunchSpectacle(definition, racer, false) == null)
            {
                nextSpectacleAt = Time.time + 1.5f;
                return;
            }
            var range = settings.SpectacleInterval(Chaos);
            nextSpectacleAt = Time.time + Mathf.Lerp(range.x, range.y, (float)spectacleRandom.NextDouble());
        }

        /// Ради кого шоу: живой игрок (чаще) или ближайший к нему соперник; без людей — любой участник.
        Racer PickSpectacleFocus()
        {
            var alive = race.Racers.Where(r => !r.Finished && !r.Retired && r.Controller != null).ToList();
            if (alive.Count == 0) return null;
            var humans = alive.Where(r => r.Human).ToList();
            if (humans.Count == 0) return alive[spectacleRandom.Next(alive.Count)];
            var me = humans[SpectacleCount % humans.Count];
            if (spectacleRandom.NextDouble() < 0.65) return me;
            Racer best = null;
            float bestD = 90f;
            foreach (var r in alive)
            {
                if (r == me) continue;
                float d = Vector3.Distance(r.Car.position, me.Car.position);
                if (d < bestD)
                {
                    bestD = d;
                    best = r;
                }
            }
            return best ?? me;
        }

        /// Место шоу: впереди участника на столько, сколько он проедет за предупреждение и подход (TriggerLead),
        /// сторона дороги — случайная (событие само проверит, есть ли там место, и выберет другую).
        RaceEvent LaunchSpectacle(EventDefinition definition, Racer racer, bool forced)
        {
            var instance = FreeInstance(definition.Id, false);
            if (instance == null || race.Course == null) return null;
            float speed = Mathf.Max(racer.Controller.Speed, 14f);
            float ahead = speed * (definition.WarningSeconds + definition.TriggerLead) + definition.ZoneLength * 0.2f;
            // Поперёк: в половине случаев — там, где участник едет сейчас (наедет, если не свернёт), иначе — другая полоса.
            // Верити и автобус берут из этого только сторону выезда, ГИПЕРГАМИЯ встаёт ровно здесь.
            float half = race.Course.HalfWidth;
            float lateral;
            if (spectacleRandom.NextDouble() < 0.5)
            {
                float p = racer.Progress;
                lateral = Vector3.Dot(racer.Car.position - race.Course.PointAt(p), race.Course.SideAt(p));
                lateral = Mathf.Clamp(lateral, -(half - 2.5f), half - 2.5f);
                if (Mathf.Abs(lateral) < 0.05f) lateral = 0.05f;
            }
            else
            {
                float side = spectacleRandom.NextDouble() < 0.5 ? -1f : 1f;
                lateral = side * half * Mathf.Lerp(0.2f, 0.6f, (float)spectacleRandom.NextDouble());
            }
            var spot = new HazardSpot
            {
                Progress = Track.TrackPath.Wrap(racer.Progress + ahead / race.Course.Length),
                Lateral = lateral,
                Seed = NextSeed(),
                FocusSlot = racer.Slot,
            };
            spectacleScheduler.Commit(definition);
            Launch(definition, instance, spot);
            SpectacleCount++;
            Debug.Log($"[RandomCar] Шоу №{SpectacleCount}{(forced ? " (принудительно)" : "")}: {definition.Id} для «{racer.Name}», Хаос {Chaos}, {ahead:F0} м впереди, поперёк {spot.Lateral:F1} м");
            return instance;
        }

        // ---------- трюки v5: поток мгновенных происшествий рядом с машинами ----------

        /// Трюк каждые доли секунды (в «безумии» ~0.3 с): пружина под колёсами, рука с обочины, наковальня сверху,
        /// НЛО над машиной. Ради кого — по очереди: живой игрок, ближайший к нему соперник (чтобы игрок видел),
        /// любой участник. Трюки не ждут и не занимают участок трассы — накладываются на всё остальное.
        void UpdateGags(int humans)
        {
            if (Time.time < nextGagAt) return;
            int limit = settings.MaxGags(Chaos) + Mathf.Min(6, (humans - 1) * 3);
            if (GagsActive >= limit)
            {
                nextGagAt = Time.time + 0.1f;
                return;
            }
            var racer = PickGagFocus();
            // Машина только что приземлилась (или ещё в воздухе) — трюк рядом с ней, «мимо»: на соседней полосе,
            // чуть впереди, на обочине. Хаос вокруг не стихает, а машина успевает проехать между полётами.
            bool nearMiss = racer != null && !Ready(racer);
            var definition = racer == null ? null : gagScheduler.Pick(false, d => d.Unlocked(Chaos, 1f) && (d.Bodies == 0 || BodiesInUse + d.Bodies <= settings.MaxBodies(Chaos))
                                                                             && !(nearMiss && d.Place == GagPlace.Car) && FreeInstance(d.Id, false) != null, true);
            if (definition == null || racer == null || LaunchGag(definition, GagSpot(definition, racer, nearMiss)) == null)
            {
                nextGagAt = Time.time + 0.1f;
                return;
            }
            nextGagAt = Time.time + GagDelay() * Intensity.GagFactor / Mathf.Sqrt(Mathf.Max(1, humans));
        }

        /// Ради кого трюк: игрок (почти половина), ближайший к игроку соперник, любой участник.
        Racer PickGagFocus()
        {
            // Машину, которая ещё летит после удара, новые трюки не выбирают своей целью: между полётами
            // она успевает приземлиться и проехать (цепные реакции в полёте случаются сами).
            var alive = race.Racers.Where(r => !r.Finished && !r.Retired).ToList();
            if (alive.Count == 0) return null;
            var humans = alive.Where(r => r.Human).ToList();
            gagTurn++;
            double roll = gagRandom.NextDouble();
            // В «безумии» в самого игрока — примерно каждый пятый трюк, в «абсурде» — каждый третий.
            if (humans.Count > 0 && roll < 0.2) return humans[gagTurn % humans.Count];
            if (humans.Count > 0 && roll < 0.7)
            {
                // Ближайший к игроку соперник: происшествие у игрока на глазах.
                var me = humans[gagTurn % humans.Count];
                Racer best = null;
                float bestD = float.MaxValue;
                foreach (var r in alive)
                {
                    if (r == me) continue;
                    float d = Vector3.Distance(r.Car.position, me.Car.position);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = r;
                    }
                }
                if (best != null && bestD < 120f) return best;
            }
            return alive[gagRandom.Next(alive.Count)];
        }

        /// Машину можно бить прямо: она на земле хотя бы полторы секунды после последнего полёта.
        bool Ready(Racer r)
        {
            // После полёта — время спокойно проехать: в «безумии» 5 с на земле, в «абсурде» 2.5 с.
            const float calm = 5f;
            return r.Controller.IsGrounded && (r.Controller.GroundedFor > calm || Time.time - r.Controller.LaunchedAt > calm + 7f);
        }

        /// Место трюка относительно машины: где она окажется к концу предупреждения, на её полосе, сбоку, над ней.
        HazardSpot GagSpot(EventDefinition definition, Racer racer, bool nearMiss = false)
        {
            var course = race.Course;
            float length = course.Length;
            float warn = Mathf.Max(0.2f, definition.WarningSeconds);
            // Где машина будет к моменту срабатывания (по её скорости — и в повороте тоже), плюс сдвиг по типу трюка.
            float t = definition.Place switch
            {
                GagPlace.Under => warn + 0.05f,
                GagPlace.Ahead => warn + 0.2f,
                GagPlace.Side => warn + 0.15f,
                // Сверху (утки, магнит) — заранее и подальше: падение и подлёт видны впереди, а не над крышей.
                GagPlace.Above => warn + 1.3f,
                _ => warn,
            };
            Predict(racer, t, out float progress, out float lateral);
            float extra = definition.Place switch
            {
                GagPlace.Ahead => Rand(2f, 9f),
                GagPlace.Side => Rand(-2f, 6f),
                GagPlace.Above => Rand(-4f, 10f),
                _ => 0f,
            };
            if (definition.Place == GagPlace.Ahead || definition.Place == GagPlace.Under) lateral += Rand(-0.8f, 0.8f);
            if (nearMiss)
            {
                // «Мимо»: в 5–8 м сбоку от машины (к той стороне дороги, где больше места) и немного впереди.
                float away = lateral > 0f ? -1f : 1f;
                lateral += away * Rand(5f, 8f);
                extra += Rand(4f, 16f);
            }
            return new HazardSpot
            {
                Progress = Track.TrackPath.Wrap(progress + extra / length),
                Lateral = Mathf.Clamp(lateral, -course.HalfWidth, course.HalfWidth),
                Seed = NextSeed(),
                FocusSlot = racer.Slot,
            };
        }

        float Rand(float min, float max) => Mathf.Lerp(min, max, (float)gagRandom.NextDouble());

        /// Где машина окажется через seconds по её текущей скорости: прогресс трассы и сдвиг поперёк.
        /// Если прогноз «перескакивает» на чужой участок трассы (шпилька), — по прогрессу и текущей полосе.
        void Predict(Racer racer, float seconds, out float progress, out float lateral)
        {
            var course = race.Course;
            float length = course.Length;
            Vector3 v = racer.Controller.Physics.Body != null ? racer.Controller.Physics.Body.linearVelocity : Vector3.zero;
            v.y = 0f;
            float travel = Mathf.Max(v.magnitude, 4f) * seconds;
            Vector3 p = racer.Car.position + (v.sqrMagnitude > 1f ? v : racer.Car.forward * 4f) * seconds;
            float near = course.Nearest(p);
            float moved = Track.TrackPath.Wrap(near - racer.Progress) * length;
            if (moved > length * 0.5f) moved -= length;
            if (Mathf.Abs(moved - travel) > 20f)
            {
                progress = Track.TrackPath.Wrap(racer.Progress + travel / length);
                lateral = Vector3.Dot(racer.Car.position - course.PointAt(racer.Progress), course.SideAt(racer.Progress));
                return;
            }
            progress = near;
            lateral = Vector3.Dot(p - course.PointAt(near), course.SideAt(near));
        }

        RaceEvent LaunchGag(EventDefinition definition, HazardSpot spot)
        {
            var instance = FreeInstance(definition.Id, false);
            if (instance == null) return null;
            gagScheduler.Commit(definition);
            Launch(definition, instance, spot);
            GagCount++;
            return instance;
        }

        /// Событие вызывает событие: машину сильно подбросило — там, где она приземлится, появится новый трюк.
        void OnCarLaunched(CarController car, float strength, int generation)
        {
            // Новый трюк на месте приземления — часть потока событий: без него (полигон, отладка) не запускается.
            if (!AutoLaunch || !Authoritative || Chaos == ChaosLevel.Off || race == null || race.Phase != RacePhase.Running || race.Course == null) return;
            if (strength < 22f || generation >= 3 || chainRequests.Count >= 6) return;
            if (gagRandom.NextDouble() > (Chaos == ChaosLevel.Absurd ? 0.45 : 0.3)) return;
            var racer = race.Racers.FirstOrDefault(r => r.Controller == car);
            if (racer == null) return;
            // Где приземлится: полёт ~ 2·vy/g, по горизонтали — текущая скорость.
            var body = car.Physics.Body;
            Vector3 v = body.linearVelocity;
            float flight = Mathf.Clamp(2f * Mathf.Max(v.y, 4f) / 9.81f, 0.6f, 5f);
            Vector3 land = car.transform.position + new Vector3(v.x, 0f, v.z) * flight;
            var course = race.Course;
            float p = course.Nearest(land);
            float lateral = Mathf.Clamp(Vector3.Dot(land - course.PointAt(p), course.SideAt(p)), -course.HalfWidth + 1f, course.HalfWidth - 1f);
            chainRequests.Add(new ChainRequest { Slot = racer.Slot, Progress = p, Lateral = lateral, Generation = generation + 1, At = Time.time + Mathf.Max(0.1f, flight - 0.6f) });
        }

        void UpdateChains()
        {
            for (int i = chainRequests.Count - 1; i >= 0; i--)
            {
                var c = chainRequests[i];
                if (Time.time < c.At) continue;
                chainRequests.RemoveAt(i);
                var id = LandingGags[gagRandom.Next(LandingGags.Length)];
                var definition = settings.Events.FirstOrDefault(d => d.Id == id);
                if (definition == null || race.BySlot(c.Slot) == null) continue;
                launchTitle = "ЦЕПНАЯ РЕАКЦИЯ";
                var launched = LaunchGag(definition, new HazardSpot { Progress = c.Progress, Lateral = c.Lateral, Seed = NextSeed(), FocusSlot = c.Slot });
                launchTitle = null;
                if (launched != null) ChainEventCount++;
            }
        }

        /// Комбинация: сцена из рецепта (scene) или якорь из расписания плюс случайный совместимый партнёр.
        bool StartCombo(RacerState focus, bool scene, bool finale)
        {
            ChaosRecipe recipe = scene ? composer.PickRecipe(Intensity, finale, Allowed) : null;
            EventDefinition anchorDef;
            string title;
            var rest = new List<(EventDefinition, ComboLink)>();
            if (recipe != null)
            {
                anchorDef = composer.Find(recipe.Steps[0].Id);
                title = recipe.Title;
                for (int i = 1; i < recipe.Steps.Length; i++) rest.Add((composer.Find(recipe.Steps[i].Id), recipe.Steps[i].Link));
            }
            else
            {
                anchorDef = scheduler.Pick(HeavyFull, Allowed, Intensity.Wild);
                if (anchorDef == null) return false;
                var partner = scheduler.Pick(!Intensity.Wild, ChaosComposer.PartnerFilter(anchorDef, Allowed, Intensity.Wild), Intensity.Wild);
                if (partner == null) return TryLaunchAhead(anchorDef, focus, 0f) != null;
                title = $"{anchorDef.Title} + {partner.Title}";
                rest.Add((partner, composer.PartnerLink(anchorDef, partner, race.Course.HalfWidth, Intensity.Wild)));
            }
            launchTitle = title;
            var anchor = TryLaunchAhead(anchorDef, focus, 0f);
            launchTitle = null;
            if (anchor == null) return recipe != null && LaunchSingle(focus) != null;
            for (int i = 0; i < rest.Count; i++)
                steps.Add(new PendingStep { Definition = rest[i].Item1, Link = rest[i].Item2, Base = anchor, At = Time.time + StepDelay * (i + 1), Title = title });
            ComboCount++;
            if (recipe != null) SceneCount++;
            ComboStarted?.Invoke(title, rest.Count + 1);
            return true;
        }

        /// Название комбинации для следующего запуска (уходит в сеть вместе с событием).
        string launchTitle;

        /// Сколько комбинаций и сцен собрано за гонку (проверка динамики).
        public int ComboCount { get; private set; }
        public int SceneCount { get; private set; }

        /// Отложенные шаги комбинаций: «поверх участка» (если правила разрешают и проезд остаётся) или «сразу за».
        void UpdateSteps()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (Time.time < s.At) continue;
                // Базовое событие уже кончилось или отменено — шаг теряет смысл.
                if (s.Base == null || !s.Base.IsRunning || s.Base.Stage == EventStage.Ending)
                {
                    steps.RemoveAt(i--);
                    continue;
                }
                if (!Allowed(s.Definition) || BigActive >= Intensity.MaxActive(settings.MaxActive(Chaos)) + 2)
                {
                    if (++s.Tries > 6) steps.RemoveAt(i--);
                    else s.At = Time.time + RetryDelay;
                    continue;
                }
                launchTitle = s.Title;
                var launched = TryLaunchRelative(s.Definition, s.Base, s.Link) ?? (s.Link == ComboLink.Stack ? TryLaunchRelative(s.Definition, s.Base, ComboLink.After) : null);
                launchTitle = null;
                if (launched == null && ++s.Tries <= 4)
                {
                    s.At = Time.time + RetryDelay;
                    continue;
                }
                steps.RemoveAt(i--);
                if (launched == null) continue;
                // Следующий шаг той же сцены цепляется к только что поставленному («сразу за» ним).
                foreach (var next in steps)
                    if (next.Base == s.Base && next.Title == s.Title && next.Link == ComboLink.After) next.Base = launched;
            }
        }

        /// Поставить событие относительно другого: на тот же участок или сразу за ним, с честностью и правилами наложения.
        RaceEvent TryLaunchRelative(EventDefinition picked, RaceEvent anchor, ComboLink link)
        {
            var definition = Substituted(picked);
            var instance = FreeInstance(definition.Id);
            if (instance == null) return null;
            CollectState();
            float length = race.Course.Length;
            float progress = link == ComboLink.Stack
                ? anchor.Progress
                : Track.TrackPath.Wrap(anchor.Progress + (anchor.ZoneLength * 0.5f + definition.ZoneLength * 0.5f + ChaosRules.ChainGap) / length);
            if (!placer.IsFair(definition, progress, racerStates) || placer.Conflicts(definition, progress, zones)) return null;
            if (definition.NeedsStraight && !placer.IsStraight(progress, definition.ZoneLength)) return null;
            var course = race.Course;
            if (link == ComboLink.Stack && !ChaosRules.CanStack(anchor.Definition, definition, course.HalfWidth, Intensity.Wild)) return null;
            // Поверх участка — на противоположную от якоря сторону, если кто-то из них перекрывает полосы.
            float anchorLateral = anchor.Placement.Lateral;
            float lateral = link == ComboLink.Stack && (definition.Blocks > 0f || anchor.Definition.Blocks > 0f)
                ? -Mathf.Sign(anchorLateral == 0f ? 1f : anchorLateral) * course.HalfWidth * 0.5f
                : lanes.Next(false, 0f, course.HalfWidth, FairnessLimits.HazardRadiusFor(course.HalfWidth));
            var spot = new HazardSpot { Progress = progress, Lateral = lateral, Seed = NextSeed(), FocusSlot = anchor.Placement.FocusSlot };
            scheduler.Commit(picked);
            Launch(definition, instance, spot);
            return instance;
        }

        /// Последний круг: финальная сцена, плашка и салют вдоль трассы.
        void StartFinale(RacerState focus, Racer focusRacer)
        {
            finaleDone = true;
            FinaleStarted?.Invoke();
            if (!StartCombo(focus, scene: true, finale: true)) StartCombo(focus, scene: false, finale: false);
            Volley(focusRacer, 6, 90f);
            nextShowAt = Time.time + 5f;
        }

        /// Салют впереди игрока: последний круг — часто, «Безумие» — иногда весь заезд. Только украшение.
        void UpdateShow(Racer focusRacer, int lap)
        {
            if (Time.time < nextShowAt) return;
            bool last = lap >= race.TotalLaps;
            if (!last && Chaos < ChaosLevel.Mayhem)
            {
                nextShowAt = float.PositiveInfinity;
                return;
            }
            Volley(focusRacer, last ? 4 : 3, 60f);
            nextShowAt = Time.time + (last ? UnityEngine.Random.Range(6f, 10f) : UnityEngine.Random.Range(14f, 22f));
        }

        void Volley(Racer focusRacer, int count, float spread)
        {
            if (ChaosFx.Instance == null || race.Course == null) return;
            float length = race.Course.Length;
            // Салют — далеко впереди и невысоко: к раскрытию он перед камерой, а не над крышей машины.
            float speed = Mathf.Max(focusRacer.Controller.Speed, 15f);
            float p = Track.TrackPath.Wrap(focusRacer.Progress + (speed * 4.5f + 60f) / length);
            ChaosFx.Instance.Volley(race.Course.PointAt(p), race.Course.DirectionAt(p), race.Course.SideAt(p), race.Course.HalfWidth, count, spread, showRandom);
        }

        /// Временная волна (например, обстрел «Тель-Авива» вместе с сиреной): событие id ставится впереди игрока
        /// по тем же правилам честности, места и тяжёлых. Только хост; false — сейчас нельзя.
        public bool LaunchWave(string id)
        {
            if (!Authoritative || race.Phase != RacePhase.Running || Chaos == ChaosLevel.Off) return false;
            var definition = settings.Events.FirstOrDefault(d => d.Id == id);
            if (definition == null) return false;
            if (definition.IsHeavy && (HeavyFull || (!Intensity.Wild && scheduler.Last != null && scheduler.Last.IsHeavy))) return false;
            if (BigActive >= Intensity.MaxActive(settings.MaxActive(Chaos)) + 1) return false;
            BindTrack();
            CollectState();
            var focus = PickFocus(out var focusRacer, out _);
            if (focusRacer == null || TryLaunchAhead(definition, focus, 0f) == null) return false;
            quietFor = 0f;
            return true;
        }

        /// Доля пройденной гонки участником: 0 — старт, 1 — финиш.
        float RaceFraction(Racer r) =>
            Mathf.Clamp01((Mathf.Max(0, r.Laps.Laps) + (r.Laps.Started ? r.Progress : 0f)) / race.TotalLaps);

        /// Чей сейчас черёд: живые игроки по очереди, а каждое третье событие — впереди одной из машин ИИ
        /// (тоже по очереди): хаос достаётся всем участникам, ИИ, уехавший вперёд, не избегает его.
        /// Без людей — все машины по очереди.
        RacerState PickFocus(out Racer racer, out int humans)
        {
            racer = null;
            humans = 0;
            var candidates = new List<int>();
            var others = new List<int>();
            for (int i = 0; i < racerStates.Count; i++)
                if (racerStates[i].Human) candidates.Add(i);
                else others.Add(i);
            humans = candidates.Count;
            if (candidates.Count == 0) for (int i = 0; i < racerStates.Count; i++) candidates.Add(i);
            if (candidates.Count == 0) return default;
            var state = humans > 0 && others.Count > 0 && focusTurn % 3 == 2
                ? racerStates[others[(focusTurn / 3) % others.Count]]
                : racerStates[candidates[focusTurn % candidates.Count]];
            racer = race.BySlot(state.Slot);
            return state;
        }

        /// Впереди у игрока (до 300 м) нет идущего события и он не внутри зоны.
        bool NothingAhead(RacerState focus)
        {
            float length = race.Course.Length;
            foreach (var e in active)
            {
                if (e.Stage == EventStage.Ending) continue;
                float d = Track.TrackPath.Wrap(e.Progress - focus.Progress) * length;
                if (d > length * 0.5f) d -= length;
                if (d > -e.ZoneLength * 0.5f && d < 300f) return false;
            }
            return true;
        }

        /// Замена события картой (например, «Обломки» вместо «Ракеты» в «Тель-Авиве»): вес и очередь — исходного.
        EventDefinition Substituted(EventDefinition definition)
        {
            string id = race.Map != null ? race.Map.Substitute(definition.Id) : definition.Id;
            if (id == definition.Id) return definition;
            return settings.Events.FirstOrDefault(d => d.Id == id) ?? definition;
        }

        /// Поставить событие впереди игрока: на расстоянии, которое он проедет за предупреждение, запас на реакцию
        /// и время «прочитать» сцену. Сторона дороги — по очереди полос; часть плохих — на его полосу.
        RaceEvent TryLaunchAhead(EventDefinition picked, RacerState focus, float extraDistance)
        {
            var definition = Substituted(picked);
            var instance = FreeInstance(definition.Id);
            if (instance == null) return null;
            CollectState();
            float speed = Mathf.Max(focus.Speed, 14f);
            float distance;
            if (Intensity.Wild)
            {
                // «Безумие»: событие возникает прямо перед носом — машина въезжает в него, едва оно раскрылось.
                float warn = Mathf.Max(definition.WarningSeconds * Intensity.WarningScale, Intensity.MinWarning);
                float lead = warn * (definition.Trigger ? 0.6f : 0.9f) + lanes.Range(0f, 0.5f);
                distance = Mathf.Clamp(speed * lead + definition.ZoneLength * 0.25f, 16f, 120f) + extraDistance;
            }
            else
            {
                float lead = Mathf.Max(definition.WarningSeconds, FairnessLimits.MinWarningSeconds) + FairnessLimits.MinReactionSeconds
                             + Intensity.ExtraLead + lanes.Range(0f, 1.2f);
                distance = Mathf.Clamp(speed * lead, 55f, 330f) + extraDistance;
            }
            if (!placer.TryPlaceAhead(definition, focus, distance, racerStates, zones, out float progress)) return null;

            var course = race.Course;
            var focusRacer = race.BySlot(focus.Slot);
            float focusLateral = focusRacer != null ? Vector3.Dot(focusRacer.Car.position - course.PointAt(focus.Progress), course.SideAt(focus.Progress)) : 0f;
            bool aim = !definition.IsGood && lanes.Roll() < Intensity.AimChance;
            float lateral = lanes.Next(aim, focusLateral, course.HalfWidth, FairnessLimits.HazardRadiusFor(course.HalfWidth));
            // Наложение на идущее событие (слой поверх участка): на сторону, противоположную перекрывающему полосы.
            foreach (var z in zones)
            {
                if (z.Definition == null || z.Definition.Blocks <= 0f && definition.Blocks <= 0f) continue;
                float d = Mathf.Min(placer.Ahead(z.Progress, progress), placer.Ahead(progress, z.Progress));
                if (d < (z.Length + definition.ZoneLength) * 0.5f) lateral = -Mathf.Sign(z.Lateral == 0f ? 1f : z.Lateral) * course.HalfWidth * 0.5f;
            }
            var spot = new HazardSpot { Progress = progress, Lateral = lateral, Seed = NextSeed(), FocusSlot = focus.Slot };
            scheduler.Commit(picked);
            Launch(definition, instance, spot);
            return instance;
        }

        void CollectState()
        {
            racerStates.Clear();
            foreach (var r in race.Racers)
                if (!r.Finished && !r.Retired) racerStates.Add(new RacerState(r.Progress, Mathf.Max(0f, r.Controller.Speed), r.Human, r.Slot));
            zones.Clear();
            foreach (var e in active) zones.Add(new EventZone(e.Progress, e.ZoneLength, e.Definition, e.Placement.Lateral));
        }

        RaceEvent FreeInstance(string id, bool warn = true)
        {
            if (!pools.TryGetValue(id, out var list))
            {
                if (warn) Debug.LogWarning($"[RandomCar] событие {id} не подготовлено в сцене");
                return null;
            }
            foreach (var e in list) if (!e.IsRunning) return e;
            return null;
        }

        /// Запуск в заданном месте (используется и для событий, пришедших от хоста по сети).
        public void Launch(EventDefinition definition, RaceEvent instance, HazardSpot spot, int launchSerial = 0, float clockOffset = 0f)
        {
            BindTrack();
            active.Add(instance);
            if (launchSerial <= 0) launchSerial = ++serial;
            instance.Begin(definition, spot, context, launchSerial, clockOffset);
            instance.ComboTitle = launchTitle;
            EventStarted?.Invoke(instance);
        }

        int NextSeed() => Mathf.FloorToInt(scheduler.Range(0f, 1f) * int.MaxValue);

        /// Событие от хоста (сетевая игра): то же место, сид и номер, что у хоста; clockOffset — на сколько
        /// хост запустил его раньше. false — нет такого события или свободного экземпляра.
        public bool LaunchRemote(string id, HazardSpot spot, int launchSerial = 0, float clockOffset = 0f, string comboTitle = null)
        {
            if (race.Phase != RacePhase.Running) return false;
            var definition = settings.Events.FirstOrDefault(d => d.Id == id);
            if (definition == null) return false;
            // Хост мог запустить событие в тот миг, когда его прошлый экземпляр у хоста уже вернулся в пул,
            // а здесь (сообщение пришло с задержкой) ещё доигрывает завершение — такой экземпляр забираем.
            var instance = FreeInstance(id) ?? Reclaim(id);
            if (instance == null) return false;
            launchTitle = string.IsNullOrEmpty(comboTitle) ? null : comboTitle;
            Launch(definition, instance, spot, launchSerial, clockOffset);
            launchTitle = null;
            if (!string.IsNullOrEmpty(comboTitle))
            {
                // Плашка комбинации у клиента — по первому событию с новым названием.
                if (comboTitle != lastRemoteCombo) ComboStarted?.Invoke(comboTitle, 0);
                lastRemoteCombo = comboTitle;
            }
            return true;
        }

        string lastRemoteCombo;

        RaceEvent Reclaim(string id)
        {
            if (!pools.TryGetValue(id, out var list)) return null;
            var victim = list.Where(e => e.IsRunning).OrderByDescending(e => e.Stage).ThenByDescending(e => e.StageProgress).FirstOrDefault();
            if (victim == null) return null;
            victim.Cancel();
            active.Remove(victim);
            return victim;
        }

        // ---------- сигналы событий (сеть) ----------

        /// Событие подало сигнал (мина задета машиной, которую считает этот компьютер): сеть разошлёт его.
        public void RaiseSignal(RaceEvent source, int code, int index, Vector3 value) => SignalRaised?.Invoke(source, code, index, value);

        /// Сигнал пришёл по сети: событие с этим номером применяет его (повтор безвреден).
        public bool ApplySignal(int launchSerial, int code, int index, Vector3 value)
        {
            foreach (var e in active)
            {
                if (e.Serial != launchSerial) continue;
                e.ReceiveSignal(code, index, value);
                return true;
            }
            return false;
        }

        public RaceEvent BySerial(int launchSerial) => active.FirstOrDefault(e => e.Serial == launchSerial);

        /// Отладка и проверки: событие по id перед машиной игрока (на честном расстоянии). Только в редакторе
        /// и отладочной сборке — в обычной игре события сами выбирают место.
        public bool Force(string id)
        {
            if (race.Phase != RacePhase.Running || !Authoritative) return false;
            var definition = settings.Events.FirstOrDefault(d => d.Id == id);
            if (definition == null) return false;
            // Трюк — рядом с машиной игрока, как в обычном потоке; шоу — впереди игрока, как в своём потоке.
            if (definition.Gag) return race.Player != null && LaunchGag(definition, GagSpot(definition, race.Player)) != null;
            if (definition.Spectacle) return race.Player != null && LaunchSpectacle(definition, race.Player, true) != null;
            var instance = FreeInstance(id);
            if (instance == null) return false;
            var player = race.Player;
            float speed = Mathf.Max(player.Controller.Speed, FairnessLimits.AssumedMinSpeed) * 1.15f;
            float distance = speed * (definition.WarningSeconds + FairnessLimits.MinReactionSeconds) + definition.ZoneLength * 0.5f + 5f;
            var spot = new HazardSpot
            {
                Progress = Track.TrackPath.Wrap(player.Progress + distance / race.Course.Length),
                Lateral = Vector3.Dot(player.Car.position - race.Course.PointAt(player.Progress), race.Course.SideAt(player.Progress)),
                Seed = NextSeed(),
                FocusSlot = player.Slot,
            };
            scheduler.Commit(definition);
            Launch(definition, instance, spot);
            return true;
        }

        /// Полигон испытаний: событие id перед машиной игрока — та же реализация и тот же запуск, что в гонке (трюк — рядом
        /// с машиной, шоу — впереди, как в своём потоке, обычное — на участке трассы впереди на полосе машины). Без
        /// расписания, накала, правил честности и замен карты. aheadMeters ≤ 0 — по длине зоны (34–80 м).
        /// null — нет такого события, нет свободного экземпляра или гонка не идёт.
        public RaceEvent LaunchDemo(string id, float aheadMeters = -1f)
        {
            if (race.Phase != RacePhase.Running || !Authoritative || race.Player == null || race.Course == null) return null;
            var definition = settings.Events.FirstOrDefault(d => d.Id == id);
            if (definition == null) return null;
            var player = race.Player;
            if (definition.Gag) return LaunchGag(definition, GagSpot(definition, player));
            if (definition.Spectacle) return LaunchSpectacle(definition, player, true);
            var instance = FreeInstance(id);
            if (instance == null) return null;
            var course = race.Course;
            float distance = aheadMeters > 0f ? aheadMeters : Mathf.Clamp(28f + definition.ZoneLength * 0.5f, 34f, 80f);
            var spot = new HazardSpot
            {
                Progress = Track.TrackPath.Wrap(player.Progress + distance / course.Length),
                Lateral = Vector3.Dot(player.Car.position - course.PointAt(player.Progress), course.SideAt(player.Progress)),
                Seed = NextSeed(),
                FocusSlot = player.Slot,
            };
            Launch(definition, instance, spot);
            Debug.Log($"[RandomCar] Полигон: событие {id} в {distance:F0} м перед машиной");
            return instance;
        }

        /// Отладка и проверки: сцена из рецепта перед игроком.
        public bool ForceRecipe(string recipeId)
        {
            if (race.Phase != RacePhase.Running || !Authoritative) return false;
            var recipe = ChaosRules.Recipes.FirstOrDefault(r => r.Id == recipeId);
            if (recipe == null) return false;
            CollectState();
            var focus = racerStates.FirstOrDefault(s => s.Slot == race.Player.Slot);
            launchTitle = recipe.Title;
            var anchor = TryLaunchAhead(composer.Find(recipe.Steps[0].Id), focus, 0f);
            launchTitle = null;
            if (anchor == null) return false;
            for (int i = 1; i < recipe.Steps.Length; i++)
                steps.Add(new PendingStep { Definition = composer.Find(recipe.Steps[i].Id), Link = recipe.Steps[i].Link, Base = anchor, At = Time.time + StepDelay * i, Title = recipe.Title });
            ComboStarted?.Invoke(recipe.Title, recipe.Steps.Length);
            return true;
        }

        /// Ближайшее событие впереди машины (для плашки интерфейса): по расстоянию вдоль трассы.
        public RaceEvent NearestAhead(Racer racer, out float distance)
        {
            distance = float.MaxValue;
            RaceEvent best = null;
            float length = race.Course.Length;
            foreach (var e in active)
            {
                if (e.Stage == EventStage.Ending) continue;
                float d = Track.TrackPath.Wrap(e.Progress - racer.Progress) * length - e.ZoneLength * 0.5f;
                if (d > length - e.ZoneLength) d = 0f;   // уже внутри зоны
                if (d < distance)
                {
                    distance = d;
                    best = e;
                }
            }
            return best;
        }

        /// Отладка и автопроверки: остановить время, когда первое активное событие дойдёт до стадии и прогресса.
        public void DebugPauseAt(EventStage stage, float progress)
        {
            debugPauseStage = stage;
            debugPauseProgress = progress;
        }

        EventStage debugPauseStage = EventStage.Idle;
        float debugPauseProgress;

        void LateUpdate()
        {
            if (debugPauseStage == EventStage.Idle) return;
            foreach (var e in active)
            {
                if (e.Stage == debugPauseStage && e.StageProgress >= debugPauseProgress)
                {
                    Time.timeScale = 0f;
                    debugPauseStage = EventStage.Idle;
                    return;
                }
            }
        }

        void HandleDebugKeys()
        {
            if (!Debug.isDebugBuild || Keyboard.current == null) return;
            // F1–F12: событие перед машиной игрока (только отладка).
            for (int i = 0; i < settings.Events.Count && i < 12; i++)
            {
                var key = Keyboard.current[Key.F1 + i];
                if (key != null && key.wasPressedThisFrame) Force(settings.Events[i].Id);
            }
        }
    }
}
