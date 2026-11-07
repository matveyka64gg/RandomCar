using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Maps;
using RandomCar.Save;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Race
{
    public enum RacePhase { Idle, Countdown, Running, Finished }

    public sealed class Racer
    {
        public string Name;
        public bool IsPlayer;
        public ParticipantKind Kind;
        public Participant Participant;
        public Color Color = Color.white;
        public Transform Car;
        public CarController Controller;
        public LapCounter Laps;
        public float Progress;
        public readonly List<float> LapTimes = new List<float>();
        public float LapStart;
        public bool Finished;
        public float FinishTime;
        public int Slot;
        /// Копия характеристик с настройкой сборки (уничтожается вместе с машиной), иначе null.
        public CarSpec TunedSpec;
        /// Награда за гонку в кредитах (только у игрока).
        public int Reward;
        public Vector3 StartPosition;
        public Quaternion StartRotation;
        public float StartProgress;
        /// Сетевой участник отключился: машина убрана, в результатах — «сошёл».
        public bool Retired;
        /// Сколько секунд машина далеко от трассы (улетела после удара, упала с моста): потом — возврат.
        public float LostFor;
        /// Сколько секунд ближайшая точка трассы «перескочила» далеко от прогресса (приземлился на чужом участке).
        public float JumpFor;
        /// Сторож «нет продвижения»: сколько метров вдоль трассы проехано с отметки и когда она поставлена.
        public float Advance;
        public float AdvanceSince;
        public float LastProgress;

        /// Живой игрок: свой или удалённый человек (не ИИ хоста). События ставятся впереди людей.
        public bool Human => Kind == ParticipantKind.LocalPlayer || (Kind == ParticipantKind.Remote && Participant != null && Participant.ClientId != ulong.MaxValue);

        /// Общий прогресс гонки: круги плюс доля круга. Пока машина не пересекла линию старта, прогресс отрицательный.
        /// Сошедшие — позади всех.
        public float Score => Retired ? -100f : Laps.Laps + Progress - (Laps.Started ? 0f : 1f);
    }

    /// Гонка на загруженной карте: участники появляются на решётке из префабов каталога, отсчёт со стартовыми
    /// огнями, круги, время, позиции, финиш, таблица лучших для каждой карты. Рестарт возвращает машины на решётку,
    /// мир карты не пересоздаётся.
    public sealed class RaceManager : MonoBehaviour
    {
        public const int DefaultLaps = 3;

        /// Кругов в этой гонке: задаёт карта (длинный «Тель-Авив» — 2).
        public int TotalLaps => map != null ? map.Laps : DefaultLaps;
        public const float CountdownSeconds = 3f;
        const float LampInterval = 0.5f;

        [SerializeField] CarCatalog catalog;
        [SerializeField] int aiSeed = 2024;

        readonly List<Racer> racers = new List<Racer>();
        readonly List<CarController> cars = new List<CarController>();
        MapDefinition map;
        Transform carsRoot;

        public CarCatalog Catalog => catalog;
        public MapDefinition Map => map;
        public TrackPath Course => map != null ? map.Track.Path : null;
        public IReadOnlyList<Racer> Racers => racers;
        public Racer Player { get; private set; }
        public RacePhase Phase { get; private set; } = RacePhase.Idle;
        public float Countdown { get; private set; } = CountdownSeconds;
        public float RaceTime { get; private set; }
        public List<float> BestTimes { get; private set; } = new List<float>();

        /// Место игрока в таблице лучших после финиша, с нуля. -1, если не попал.
        public int BestRank { get; private set; } = -1;

        /// Номер попытки: растёт при каждом рестарте. Системы (события, камера) сбрасываются по нему.
        public int Attempt { get; private set; }

        /// Записывать результат в таблицу лучших (проверочные заезды автопилотом выключают).
        public bool RecordBestTimes { get; set; } = true;

        /// Свободная езда (полигон испытаний): без отсчёта, кругов, таймера круга, финиша, результатов, рекордов,
        /// кредитов и сторожа продвижения (стоять на месте можно сколько угодно).
        public bool FreeRoam { get; set; }

        /// Свободная езда: возвращать на трассу перевернувшиеся и улетевшие машины, как в гонке (на полигоне —
        /// в режиме «уязвимость»; в «неуязвимости» машина остаётся там, куда её забросило, до «Сброса»).
        public bool FreeRoamRecovery { get; set; } = true;

        /// Отсчёт можно держать (сеть: ждём, пока все загрузятся).
        public bool HoldCountdown { get; set; }

        public event Action RaceRestarted;
        public event Action RaceStarted;
        public event Action RaceFinished;

        /// Участник финишировал (свой или ИИ этого компьютера; удалённых сообщает сеть через ReportFinish).
        public event Action<Racer> RacerFinished;

        /// Сменились карта или участники: камера, звук и интерфейс переподключаются.
        public event Action RacersChanged;

        public int PlayerPosition => Player == null ? 1 : Standings().IndexOf(Player) + 1;

        /// Порядок гонки: финишировавшие — по времени финиша, остальные — по пройденному пути, сошедшие — последними.
        /// В сети финиш другой машины приходит от её владельца, поэтому одного «пути» мало.
        public static int CompareRacers(Racer a, Racer b)
        {
            if (a.Retired != b.Retired) return a.Retired ? 1 : -1;
            if (a.Finished && b.Finished) return a.FinishTime.CompareTo(b.FinishTime);
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            return b.Score.CompareTo(a.Score);
        }

        public List<Racer> Standings()
        {
            var order = new List<Racer>(racers);
            order.Sort(CompareRacers);
            return order;
        }

        public void Configure(CarCatalog carCatalog) => catalog = carCatalog;

        /// Привязка к загруженной карте.
        public void Bind(MapDefinition mapDefinition)
        {
            map = mapDefinition;
            BestTimes = BestTimesStore.Load(BestTimesStore.PathFor(map.Key));
        }

        /// Участники по списку: старые машины убираются, новые ставятся на решётку по порядку.
        public void Spawn(IReadOnlyList<Participant> participants)
        {
            Despawn();
            if (carsRoot == null)
            {
                carsRoot = new GameObject("Cars").transform;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(carsRoot.gameObject, gameObject.scene);
            }
            for (int i = 0; i < participants.Count && i < MapDefinition.MaxGrid; i++)
            {
                var participant = participants[i];
                var entry = catalog.Get(participant.CarKey);
                var pose = map.GridPose(i);
                participant.Build ??= CarBuild.Default(entry);
                participant.Build.Sanitize(entry, catalog, participant.Kind != ParticipantKind.Remote);
                // Машина создаётся под выключенным родителем: характеристики с настройкой ставятся до Awake.
                var holder = new GameObject("Spawn");
                holder.SetActive(false);
                var go = Instantiate(entry.Prefab, pose.position, pose.rotation, holder.transform);
                go.name = $"{participant.Kind}_{entry.Key}_{i}";
                var tuned = CarTuning.Apply(entry.Spec, participant.Build);
                if (tuned != entry.Spec) go.GetComponent<CarPhysics>().SetSpec(tuned);
                if (participant.Kind == ParticipantKind.LocalPlayer) go.AddComponent<PlayerDriver>();
                else if (participant.Kind == ParticipantKind.Ai) go.AddComponent<AiDriver>();
                go.transform.SetParent(carsRoot, true);
                Destroy(holder);
                // Чужую машину (другой игрок, ИИ хоста) двигает сеть: тело кинематическое, физику считает владелец.
                if (participant.Kind == ParticipantKind.Remote) go.GetComponent<CarPhysics>().SetSimulated(false);
                // Воздушный поводок после подбросов событий: машина приземляется на дорогу.
                go.GetComponent<CarController>().Guide = Course;
                var controller = go.GetComponent<CarController>();
                var cosmetics = go.GetComponent<CarCosmetics>();
                if (cosmetics != null) cosmetics.Apply(participant.Build, catalog);

                var racer = new Racer
                {
                    Name = participant.Name,
                    IsPlayer = participant.Kind == ParticipantKind.LocalPlayer,
                    Kind = participant.Kind,
                    Participant = participant,
                    Color = cosmetics != null ? cosmetics.PaintColor : Color.white,
                    Car = go.transform,
                    Controller = controller,
                    Slot = i,
                    StartPosition = pose.position,
                    StartRotation = pose.rotation,
                    StartProgress = Course.Nearest(pose.position),
                    TunedSpec = tuned != entry.Spec ? tuned : null,
                };
                racers.Add(racer);
                cars.Add(controller);
                if (racer.IsPlayer) Player = racer;
            }
            Player ??= racers.FirstOrDefault();
            RacersChanged?.Invoke();
        }

        void Despawn()
        {
            foreach (var r in racers)
            {
                if (r.Car != null) Destroy(r.Car.gameObject);
                if (r.TunedSpec != null) Destroy(r.TunedSpec);
            }
            racers.Clear();
            cars.Clear();
            Player = null;
            Phase = RacePhase.Idle;
        }

        /// Возвращает всех на решётку и запускает отсчёт.
        public void Restart()
        {
            PrepareGrid();
            Phase = RacePhase.Countdown;
            RaceRestarted?.Invoke();
            // Полигон: сразу едем, без отсчёта и стартовых огней.
            if (FreeRoam) StartRace();
        }

        /// Машины на решётке, гонка не идёт (фон главного меню).
        public void PrepareGrid()
        {
            Attempt++;
            if (SkidMarks.Instance != null) SkidMarks.Instance.Clear();
            var random = new System.Random(aiSeed + Attempt);
            foreach (var r in racers)
            {
                var ai = r.Car.GetComponent<AiDriver>();
                if (ai != null && r.Kind == ParticipantKind.Ai)
                    ai.Init(Course, cars, AiSkill.ForDifficulty(GameSettings.AiDifficulty, (float)random.NextDouble()), random.Next());
                r.Controller.ResetTo(r.StartPosition, r.StartRotation);
                r.Controller.Locked = true;
                r.Laps = new LapCounter(r.StartProgress);
                r.Progress = r.StartProgress;
                r.LapTimes.Clear();
                r.LapStart = 0f;
                r.Finished = false;
                r.FinishTime = 0f;
                r.Retired = false;
                if (r.Car != null && !r.Car.gameObject.activeSelf) r.Car.gameObject.SetActive(true);
            }
            Phase = RacePhase.Idle;
            Countdown = CountdownSeconds;
            RaceTime = 0f;
            BestRank = -1;
            if (map != null && map.StartLights != null) map.StartLights.SetLit(0);
        }

        /// Останавливает гонку без отсчёта (например, при выходе в меню).
        public void Stop()
        {
            Phase = RacePhase.Idle;
            foreach (var r in racers) r.Controller.Locked = true;
            if (map != null && map.StartLights != null) map.StartLights.SetLit(0);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (Phase)
            {
                case RacePhase.Countdown:
                    if (HoldCountdown) break;
                    Countdown -= dt;
                    float elapsed = CountdownSeconds - Countdown;
                    if (map != null && map.StartLights != null) map.StartLights.SetLit(Mathf.Clamp(Mathf.FloorToInt(elapsed / LampInterval) + 1, 0, 5));
                    if (Countdown <= 0f) StartRace();
                    break;

                case RacePhase.Running:
                    RaceTime += dt;
                    UpdateRacers();
                    break;
            }
        }

        /// Синхронизация отсчёта по сети: сколько секунд осталось до старта.
        public void SetCountdown(float secondsLeft) => Countdown = Mathf.Clamp(secondsLeft, 0f, CountdownSeconds);

        void StartRace()
        {
            Phase = RacePhase.Running;
            RaceTime = 0f;
            if (map != null && map.StartLights != null) map.StartLights.SetLit(0);
            foreach (var racer in racers)
            {
                racer.Controller.Locked = false;
                // Отсчёт и облёт камеры идут дольше 6 с: без сброса сторож продвижения «возвращал на трассу» всех прямо с решётки.
                racer.Advance = 0f;
                racer.AdvanceSince = Time.time;
                racer.LastProgress = racer.Progress;
            }
            RaceStarted?.Invoke();
        }

        void UpdateRacers()
        {
            if (FreeRoam)
            {
                // Полигон: только прогресс по трассе (события ставятся по нему) и, если включено, возврат на трассу.
                foreach (var racer in racers)
                {
                    if (racer.Retired || racer.Kind == ParticipantKind.Remote) continue;
                    if (FreeRoamRecovery && (racer.Controller.NeedsRecovery || OutOfBounds(racer))) Recover(racer);
                    TrackProgress(racer);
                }
                return;
            }
            foreach (var racer in racers)
            {
                if (racer.Finished || racer.Retired) continue;

                if (racer.Kind != ParticipantKind.Remote)
                {
                    // Порядок и короткое замыкание — как раньше (у проверок есть счётчики времени), причина — в лог.
                    string reason = null;
                    if (racer.Controller.NeedsRecovery) reason = "перевёрнута или застряла";
                    else if (OutOfBounds(racer)) reason = "далеко от трассы";
                    else if (NoAdvance(racer)) reason = "нет продвижения 6 с";
                    if (reason != null) Recover(racer, reason);
                }
                if (racer.LostFor < 0.3f) TrackProgress(racer);
                if (!racer.Laps.Update(racer.Progress)) continue;

                racer.LapTimes.Add(RaceTime - racer.LapStart);
                racer.LapStart = RaceTime;
                // Финиш удалённой машины засчитывает её владелец (сообщение по сети), а не копия на этом компьютере.
                if (racer.Laps.Laps >= TotalLaps && racer.Kind != ParticipantKind.Remote) FinishRacer(racer);
            }

            if (Player != null && Player.Finished) FinishRace();
        }

        /// Прогресс по ближайшей точке трассы. Хаос v5: улетевшая машина может приземлиться на другом участке
        /// (через шпильку, на соседний серпантин) — прогресс не перескакивает больше чем на JumpLimit метров,
        /// такая машина считается «потерянной» и возвращается туда, где была на трассе.
        void TrackProgress(Racer racer)
        {
            float p = Course.Nearest(racer.Car.position);
            float jump = TrackPath.Wrap(p - racer.Progress) * Course.Length;
            if (jump > Course.Length * 0.5f) jump -= Course.Length;
            // Полёт вдоль трассы (молот отправил вперёд) — честный прогресс: по прямой машина пролетела примерно
            // столько же, сколько по трассе. Перелёт через шпильку на чужой участок — нет.
            float straight = Vector3.Distance(Course.PointAt(racer.Progress), racer.Car.position);
            bool alongTrack = jump > 0f && jump < JumpMax && jump < straight * 1.5f + 25f;
            if (Mathf.Abs(jump) > JumpLimit && racer.Laps.Started && !alongTrack)
            {
                racer.JumpFor += Time.deltaTime;
                return;
            }
            racer.JumpFor = 0f;
            racer.Progress = p;
        }

        const float JumpLimit = 90f;
        const float JumpMax = 260f;

        /// Машина далеко за пределами трассы (выбило за край, упала с моста или эстакады).
        /// Порог щедрый: объезды и набережная «Тель-Авива» под ним не попадают. В «безумии» машина может
        /// улететь высоко и далеко — её возвращают только упавшую ниже дороги, улетевшую очень далеко
        /// или приземлившуюся на чужом участке трассы.
        bool OutOfBounds(Racer racer)
        {
            Vector3 axis = Course.PointAt(racer.Progress);
            Vector3 d = racer.Car.position - axis;
            float lateral = Mathf.Abs(Vector3.Dot(d, Course.SideAt(racer.Progress)));
            bool wild = Events.ChaosPower.Current.Wild;
            // В «безумии» улетевшая в песок машина, уже стоящая на земле далеко от дороги, возвращается быстрее:
            // хаос — в полёте, а не в долгой дороге назад.
            bool farOff = wild && racer.Controller.IsGrounded && lateral > Course.HalfWidth + 14f;
            bool lost = -d.y > (wild ? 16f : 12f) || lateral > Course.HalfWidth + (wild ? 220f : 60f) || d.y > (wild ? 260f : 40f)
                        || racer.JumpFor > 0f && racer.Controller.IsGrounded || farOff;
            racer.LostFor = lost ? racer.LostFor + Time.deltaTime : 0f;
            return racer.LostFor > (farOff ? 1.8f : wild ? 1.6f : 1.2f);
        }

        /// Сторож продвижения: за 6 с участник проехал вдоль трассы меньше 12 м (застрял за ограждением, в заторе
        /// у препятствия, бьётся о стену) — его возвращают на дорогу. Правило одно для игрока и ИИ.
        bool NoAdvance(Racer racer)
        {
            if (racer.Controller.Locked)
            {
                racer.AdvanceSince = Time.time;
                racer.Advance = 0f;
                racer.LastProgress = racer.Progress;
                return false;
            }
            float step = TrackPath.Wrap(racer.Progress - racer.LastProgress) * Course.Length;
            if (step > Course.Length * 0.5f) step -= Course.Length;
            racer.LastProgress = racer.Progress;
            racer.Advance += step;
            // Полёт после удара события (кувырок, НЛО, гравитационный сбой) — не «застрял»: время в воздухе не считается.
            if (!racer.Controller.IsGrounded && racer.Car.position.y > Course.PointAt(racer.Progress).y - 2f) racer.AdvanceSince += Time.deltaTime;
            if (racer.Advance >= 12f)
            {
                racer.Advance = 0f;
                racer.AdvanceSince = Time.time;
                return false;
            }
            return Time.time - racer.AdvanceSince > 6f;
        }

        /// Машина на крыше или застряла: ставим на трассу рядом, на свою половину дороги. Правило одно для всех.
        void Recover(Racer racer, string reason = null)
        {
            if (reason != null && Phase == RacePhase.Running)
            {
                Vector3 at = racer.Car.position, axisNow = Course.PointAt(racer.Progress);
                Debug.Log($"[RandomCar] Возврат на трассу: {racer.Name}, {reason}, круг {racer.Laps.Laps + 1}, {racer.Progress * Course.Length:F0} м трассы, " +
                          $"точка ({at.x:F0}; {at.y:F1}; {at.z:F0}), над осью {at.y - axisNow.y:F1} м, скорость {racer.Controller.Speed * 3.6f:F0} км/ч");
            }
            // Улетевшую машину ставим туда, где она была на трассе (ближайшая точка могла оказаться на другом участке).
            float p = racer.LostFor > 1f ? racer.Progress : Course.Nearest(racer.Car.position);
            Vector3 axis = Course.PointAt(p);
            Vector3 side = Course.SideAt(p);
            float lateral = Mathf.Clamp(Vector3.Dot(racer.Car.position - axis, side), -3.5f, 3.5f);
            racer.Controller.ResetTo(axis + side * lateral + Vector3.up * 0.4f, Quaternion.LookRotation(Course.DirectionAt(p)));
            racer.Progress = p;
            racer.LostFor = 0f;
            racer.JumpFor = 0f;
            racer.Advance = 0f;
            racer.AdvanceSince = Time.time;
            racer.LastProgress = p;
            racer.Controller.Locked = false;
            // Две секунды защиты: машина снова едет и успевает набрать ход, а не попадает в следующую ловушку.
            racer.Controller.Protect(CarController.RespawnProtection);
            Recovered?.Invoke(racer);
            if (TryGetComponent<RaceTelemetry>(out var telemetry)) telemetry.NoteRecovery(racer);
        }

        void FinishRacer(Racer racer)
        {
            racer.Finished = true;
            racer.FinishTime = RaceTime;
            racer.Controller.Locked = true;
            RacerFinished?.Invoke(racer);
        }

        public Racer BySlot(int slot) => racers.FirstOrDefault(r => r.Slot == slot);

        /// Участник по его машине (null — машина не в гонке).
        public static Racer Find(Car.CarController car)
        {
            if (car == null || instance == null) return null;
            foreach (var r in instance.racers) if (r.Controller == car) return r;
            return null;
        }

        static RaceManager instance;

        void OnEnable() => instance = this;

        void OnDisable()
        {
            if (instance == this) instance = null;
        }

        /// Машину вернули на трассу (застряла, перевернулась, улетела): для проверок.
        public event System.Action<Racer> Recovered;

        /// Сеть: владелец машины сообщил её финиш (время по его часам гонки, старт у всех общий).
        public void ReportFinish(int slot, float time)
        {
            var racer = BySlot(slot);
            if (racer == null || racer.Finished || racer.Retired) return;
            racer.Finished = true;
            racer.FinishTime = time;
            racer.Controller.Locked = true;
        }

        /// Сеть: участник отключился — машина убирается с трассы, в результатах «сошёл».
        public void Retire(int slot)
        {
            var racer = BySlot(slot);
            if (racer == null || racer.Retired || racer == Player) return;
            racer.Retired = true;
            racer.Controller.Locked = true;
            racer.Car.gameObject.SetActive(false);
            RacersChanged?.Invoke();
        }

        /// Машины всех участников (для ИИ и автопилота).
        public IReadOnlyList<CarController> Cars => cars;

        void FinishRace()
        {
            Phase = RacePhase.Finished;
            foreach (var racer in racers) racer.Controller.Locked = true;

            if (RecordBestTimes)
            {
                BestRank = BestTimesStore.Add(BestTimes, Player.FinishTime);
                BestTimesStore.Save(BestTimesStore.PathFor(map != null ? map.Key : null), BestTimes);
                // Кредиты за гонку: место, обогнанные соперники, рекорд трассы.
                if (Player.Kind == ParticipantKind.LocalPlayer)
                    Player.Reward = GarageStore.RecordRace(PlayerPosition, racers.Count, map != null ? map.Key : null, BestRank == 0);
            }
            RaceFinished?.Invoke();
        }

        /// Участники одиночной игры: игрок на своей сборке и ИИ на машинах близкой производительности
        /// (заводская настройка, разные цвета и диски — только вид).
        public static List<Participant> SinglePlayer(CarCatalog catalog, string playerCar, CarBuild playerBuild, int count = 4, int seed = 0)
        {
            var player = catalog.Get(playerCar);
            var list = new List<Participant>
            {
                // В таблице мест — никнейм игрока из настроек (а не название машины).
                new Participant { Name = Core.GameSettings.PlayerName, CarKey = player.Key, Build = playerBuild, Kind = ParticipantKind.LocalPlayer },
            };
            float rating = player.Spec != null ? player.Spec.Rating : 0f;
            var rivals = catalog.Cars.Where(c => c.Key != player.Key && c.Spec != null)
                .OrderBy(c => Mathf.Abs(Mathf.Log(Mathf.Max(1f, c.Spec.Rating) / Mathf.Max(1f, rating)))).ToList();
            var random = new System.Random(seed);
            for (int i = 0; list.Count < count && rivals.Count > 0; i++)
            {
                var car = rivals[i % rivals.Count];
                list.Add(new Participant { Name = car.DisplayName, CarKey = car.Key, Build = RivalBuild(car, catalog, random), Kind = ParticipantKind.Ai });
            }
            // Игрок стартует не с поула: ставим его в середину решётки.
            if (list.Count > 2) (list[0], list[2]) = (list[2], list[0]);
            return list;
        }

        /// Вид соперника: цвет из палитры и диски, совместимые с машиной. Характеристики — заводские.
        static CarBuild RivalBuild(CarEntry car, CarCatalog catalog, System.Random random)
        {
            var build = CarBuild.Default(car);
            if (catalog.Palette.Count > 0 && random.NextDouble() < 0.6)
                build.Paint.Body = catalog.Palette[random.Next(catalog.Palette.Count)].Color;
            var rims = catalog.PartsFor(car, PartSlot.Rims);
            if (rims.Count > 0 && random.NextDouble() < 0.5) build.Set(PartSlot.Rims, rims[random.Next(rims.Count)].Variant);
            build.Paint.BodyFinish = (PaintFinish)random.Next(0, 3);
            return build;
        }
    }
}
