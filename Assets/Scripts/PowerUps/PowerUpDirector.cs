using System;
using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Events;
using RandomCar.Race;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.PowerUps
{
    /// Применение бонуса: кто, что, номер (сеть), цель (ракеты), точка и направление (мина, разряд, клаксон).
    public struct PowerUpUse
    {
        public int Slot;
        public PowerUpKind Kind;
        public int Serial;
        public int Target;
        public Vector3 Position;
        public Vector3 Forward;
    }

    /// Бонусы в гонке: ящики «?» на трассе, у каждого участника — один бонус в запасе, применение (клавиша E,
    /// ИИ — сам), ракеты, пулемёты, мины, разряд, клаксон, турбо, прыжок и щит.
    ///
    /// Сеть (как у событий): хост — единственный источник выдачи и применения бонусов (у клиента — просьбы
    /// PickupRequested/UseRequested, хост проверяет и рассылает Granted/Used); полёт ракет, очереди и мины считает
    /// каждый компьютер одинаково по присланным данным, а попадание в машину решает компьютер, который считает её
    /// физику (HitReported → хост → всем). Силы к машине применяет только её владелец (CarController.Physics.Simulated).
    public sealed class PowerUpDirector : MonoBehaviour, IChaosReactive
    {
        const float RocketHitRadius = 2.6f;
        const float MineTouchRadius = 2.3f;
        const float MineBlastRadius = 5.5f;
        const float GunRate = 12f;
        const float BoxHeight = 1.1f;

        [SerializeField] RaceManager race;
        [SerializeField] PowerUpSettings settings;
        [SerializeField] List<Transform> boxes = new List<Transform>();
        [SerializeField] List<RocketRig> rockets = new List<RocketRig>();
        [SerializeField] List<GunRig> guns = new List<GunRig>();
        [SerializeField] List<LineRenderer> tracers = new List<LineRenderer>();
        [SerializeField] List<Transform> mines = new List<Transform>();
        [SerializeField] List<BubbleRig> bubbles = new List<BubbleRig>();
        [SerializeField] List<LineRenderer> bolts = new List<LineRenderer>();

        // ---------- состояние ----------

        sealed class Holder
        {
            public PowerUpKind Item;
            public float GotAt;
            public float AiUseAt;
            public float BlockedHintAt;
        }

        sealed class Box
        {
            public Transform T;
            public BoxPlace Place;
            public Vector3 Home;
            public bool Active;
            public float RespawnAt;
            public float PendingUntil;
            public float Phase;
        }

        sealed class Rocket
        {
            public RocketRig Rig;
            public bool Live;
            public int Serial, Owner, Target;
            public bool Leader, Diving;
            public Vector3 Position, Velocity;
            public float Born, TrackDistance, Lateral;
        }

        sealed class Gun
        {
            public GunRig Rig;
            public int Slot = -1;
            public float Until;
            public int Rounds;
            public float NextShot;
            public bool Left;
            public float Recoil;
            /// Поворот турели к цели (градусы рыскания относительно машины).
            public float Yaw;
        }

        sealed class Bullet
        {
            public bool Live;
            public int Shooter;
            public Vector3 Position, Direction;
            public float Travel;
            public LineRenderer Tracer;
        }

        sealed class DropMine
        {
            public Transform T;
            public Renderer Lamp;
            public bool Live;
            public int Serial, Owner;
            public Vector3 Position;
            public float ArmedAt, DieAt, OwnerSafeUntil;
        }

        sealed class Bolt
        {
            public LineRenderer Line;
            public int From, To;
            public float Until;
        }

        readonly Dictionary<int, Holder> holders = new Dictionary<int, Holder>();
        readonly List<Box> boxList = new List<Box>();
        readonly List<Rocket> rocketList = new List<Rocket>();
        readonly List<Gun> gunList = new List<Gun>();
        readonly List<Bullet> bulletList = new List<Bullet>();
        readonly List<DropMine> mineList = new List<DropMine>();
        readonly List<Bolt> boltList = new List<Bolt>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        MaterialPropertyBlock props;
        System.Random random = new System.Random(77);
        TrackPath boundCourse;
        int serial;
        int nextBullet;
        float aiCheckAt;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public static PowerUpDirector Current { get; private set; }

        public PowerUpSettings Settings => settings;

        /// Хост или одиночная игра: выдаёт и применяет бонусы. У клиента — только просьбы хосту.
        public bool Authoritative { get; set; } = true;

        // ---------- события (сеть, интерфейс, проверка) ----------

        /// Клиент: своя машина въехала в ящик (просьба хосту).
        public event Action<int, int> PickupRequested;
        /// Клиент: игрок нажал «использовать» (просьба хосту).
        public event Action<int> UseRequested;
        /// Бонус выдан участнику (слот, вид, номер ящика): хост рассылает, интерфейс показывает.
        public event Action<int, PowerUpKind, int> Granted;
        /// Бонус применён: хост рассылает, все запускают одно и то же.
        public event Action<PowerUpUse> Used;
        /// Попадание решено здесь (машина этого компьютера): номер, слот пострадавшего, точка — для сети.
        public event Action<int, int, Vector3> HitReported;
        /// Попадание (для интерфейса и проверки): вид, кто, в кого, поглотила ли защита.
        public event Action<PowerUpKind, int, int, bool> HitLanded;
        /// Ракета захватила цель (вид, стрелок, цель): рамка над целью и предупреждение у неё.
        public event Action<PowerUpKind, int, int> RocketLocked;
        /// Пытался взять ящик, держа бонус (слот): подсказка «сначала используй».
        public event Action<int> PickupRefused;

        // Счётчики для проверки.
        public int Pickups { get; private set; }
        public int Uses { get; private set; }
        public int RocketHits { get; private set; }
        public int BulletHits { get; private set; }
        public int MineHits { get; private set; }
        public int BulletsFired { get; private set; }
        /// Что задела последняя пуля (отладка).
        public string LastBulletHit { get; private set; } = "";
        public int BoxCount => boxList.Count;
        public readonly Dictionary<PowerUpKind, int> UsesByKind = new Dictionary<PowerUpKind, int>();
        public readonly Dictionary<PowerUpKind, int> HitsByKind = new Dictionary<PowerUpKind, int>();

        public void Configure(RaceManager raceManager, PowerUpSettings powerUpSettings, List<Transform> boxPool, List<RocketRig> rocketPool,
                              List<GunRig> gunPool, List<LineRenderer> tracerPool, List<Transform> minePool, List<BubbleRig> bubblePool,
                              List<LineRenderer> boltPool)
        {
            race = raceManager;
            settings = powerUpSettings;
            boxes = boxPool;
            rockets = rocketPool;
            guns = gunPool;
            tracers = tracerPool;
            mines = minePool;
            bubbles = bubblePool;
            bolts = boltPool;
        }

        void Awake()
        {
            Current = this;
            props = new MaterialPropertyBlock();
            foreach (var b in boxes) b.gameObject.SetActive(false);
            foreach (var r in rockets)
            {
                r.Root.gameObject.SetActive(false);
                if (r.Engine != null)
                {
                    r.Engine.clip = SoundBank.RocketHiss;
                    r.Engine.loop = true;
                }
                rocketList.Add(new Rocket { Rig = r });
            }
            foreach (var g in guns)
            {
                g.Root.gameObject.SetActive(false);
                gunList.Add(new Gun { Rig = g });
            }
            foreach (var t in tracers)
            {
                t.gameObject.SetActive(false);
                bulletList.Add(new Bullet { Tracer = t });
            }
            foreach (var m in mines)
            {
                m.gameObject.SetActive(false);
                var lamp = m.Find("Lamp");
                mineList.Add(new DropMine { T = m, Lamp = lamp != null ? lamp.GetComponent<Renderer>() : null });
            }
            foreach (var b in bubbles) b.Root.gameObject.SetActive(false);
            foreach (var l in bolts)
            {
                l.gameObject.SetActive(false);
                boltList.Add(new Bolt { Line = l });
            }
            race.RaceRestarted += ResetAll;
            race.RacersChanged += ResetAll;
            race.RaceFinished += ClearProjectiles;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            ChaosFx.Unregister(this);
            if (race == null) return;
            race.RaceRestarted -= ResetAll;
            race.RacersChanged -= ResetAll;
            race.RaceFinished -= ClearProjectiles;
        }

        void OnEnable() => ChaosFx.Register(this);

        /// Новая гонка или карта: ящики по трассе заново, бонусы у всех пустые, снаряды убраны.
        void ResetAll()
        {
            holders.Clear();
            ClearProjectiles();
            boundCourse = null;
            BindCourse();
            Pickups = Uses = RocketHits = BulletHits = MineHits = 0;
            UsesByKind.Clear();
            HitsByKind.Clear();
            ChaosFx.Register(this);
        }

        void ClearProjectiles()
        {
            foreach (var r in rocketList) KillRocket(r);
            foreach (var g in gunList) StopGun(g);
            foreach (var b in bulletList) KillBullet(b);
            foreach (var m in mineList) KillMine(m);
            foreach (var b in boltList)
            {
                b.Until = 0f;
                b.Line.gameObject.SetActive(false);
            }
        }

        /// Ящики «?» на трассе (на полигоне испытаний их нет: там только события).
        public bool BoxesEnabled
        {
            get => boxesEnabled;
            set
            {
                if (boxesEnabled == value) return;
                boxesEnabled = value;
                boundCourse = null;
            }
        }

        bool boxesEnabled = true;

        /// Ящики по трассе текущей карты (у всех игроков одинаково).
        void BindCourse()
        {
            var course = race.Course;
            if (course == null || course == boundCourse) return;
            boundCourse = course;
            boxList.Clear();
            if (!boxesEnabled)
            {
                foreach (var t in boxes) t.gameObject.SetActive(false);
                return;
            }
            var places = PowerUpLayout.Build(course, settings.RowSpacing);
            for (int i = 0; i < boxes.Count; i++)
            {
                var t = boxes[i];
                if (i >= places.Count)
                {
                    t.gameObject.SetActive(false);
                    continue;
                }
                var place = places[i];
                Vector3 p = course.PointAt(place.Progress) + course.SideAt(place.Progress) * place.Lateral;
                Vector3 home = GroundAt(p) + Vector3.up * BoxHeight;
                t.SetPositionAndRotation(home, Quaternion.LookRotation(course.DirectionAt(place.Progress)));
                t.gameObject.SetActive(true);
                boxList.Add(new Box { T = t, Place = place, Home = home, Active = true, Phase = i * 0.37f });
            }
            if (places.Count > boxes.Count) Debug.LogWarning($"[RandomCar] Бонусы: ящиков в пуле {boxes.Count}, на трассе нужно {places.Count}");
        }

        static Vector3 GroundAt(Vector3 p)
        {
            var hitsDown = Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = p;
            foreach (var h in hitsDown)
            {
                if (h.collider.attachedRigidbody != null || h.collider.GetComponentInParent<RaceEvent>() != null) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    result = h.point;
                }
            }
            return result;
        }

        Holder HolderOf(int slot)
        {
            if (!holders.TryGetValue(slot, out var h)) holders[slot] = h = new Holder();
            return h;
        }

        /// Бонус в запасе у участника.
        public PowerUpKind HeldBy(int slot) => holders.TryGetValue(slot, out var h) ? h.Item : PowerUpKind.None;

        /// Пулемёт участника: доля оставшихся патронов (0 — нет пулемёта).
        public float GunLeft(int slot)
        {
            foreach (var g in gunList) if (g.Slot == slot && g.Rounds > 0) return g.Rounds / (float)settings.GunRounds;
            return 0f;
        }

        static bool Simulates(Racer r) => r.Controller.Physics.Simulated;

        static bool Alive(Racer r) => r != null && !r.Finished && !r.Retired && r.Car != null;

        // ---------- кадр ----------

        void Update()
        {
            if (race == null || race.Phase == RacePhase.Idle) return;
            BindCourse();
            float dt = Time.deltaTime;
            UpdateBoxes(dt);
            if (race.Phase != RacePhase.Running) return;
            UpdateRockets(dt);
            UpdateGuns(dt);
            UpdateBullets(dt);
            UpdateMines();
            UpdateBolts();
            if (Time.time >= aiCheckAt)
            {
                aiCheckAt = Time.time + 0.25f;
                UpdateAi();
            }
        }

        void LateUpdate()
        {
            UpdateBubbles();
            // Пулемёт едет на крыше своей машины.
            foreach (var g in gunList)
            {
                if (g.Slot < 0) continue;
                var r = race.BySlot(g.Slot);
                if (r == null || r.Car == null)
                {
                    StopGun(g);
                    continue;
                }
                MountGun(g, r);
            }
        }

        // ---------- ящики ----------

        void UpdateBoxes(float dt)
        {
            var cam = Camera.main;
            Vector3 view = cam != null ? cam.transform.position : Vector3.zero;
            float t = Time.time;
            foreach (var b in boxList)
            {
                if (!b.Active && t >= b.RespawnAt && b.RespawnAt > 0f)
                {
                    b.Active = true;
                    b.T.gameObject.SetActive(true);
                    b.T.localScale = Vector3.one * 0.05f;
                }
                if (b.PendingUntil > 0f && t > b.PendingUntil)
                {
                    // Хост не подтвердил подбор (ящик уже взят другим) — ящик снова виден.
                    b.PendingUntil = 0f;
                    if (b.Active) b.T.gameObject.SetActive(true);
                }
                if (!b.Active || b.PendingUntil > 0f) continue;
                // Парит и вращается (вдали — не анимируется: экономия).
                if ((b.Home - view).sqrMagnitude < 260f * 260f)
                {
                    b.T.SetPositionAndRotation(b.Home + Vector3.up * Mathf.Sin(t * 2.4f + b.Phase) * 0.25f, Quaternion.Euler(12f, t * 90f + b.Phase * 50f, 8f));
                    if (b.T.localScale.x < 1f) b.T.localScale = Vector3.one * Mathf.Min(1f, b.T.localScale.x + dt * 2.5f);
                }
            }
            if (race.Phase != RacePhase.Running) return;
            float radius = settings.PickupRadius;
            foreach (var r in race.Racers)
            {
                if (!Alive(r) || !Simulates(r)) continue;
                Vector3 pos = r.Car.position;
                for (int i = 0; i < boxList.Count; i++)
                {
                    var b = boxList[i];
                    if (!b.Active || b.PendingUntil > 0f) continue;
                    Vector3 d = b.Home - pos;
                    if (Mathf.Abs(d.y) > 3.5f || d.x * d.x + d.z * d.z > radius * radius) continue;
                    var h = HolderOf(r.Slot);
                    if (h.Item != PowerUpKind.None)
                    {
                        // Правило замены: держишь бонус — ящик не берётся (проезжаешь сквозь), сначала используй свой.
                        if (Time.time - h.BlockedHintAt > 2.5f)
                        {
                            h.BlockedHintAt = Time.time;
                            PickupRefused?.Invoke(r.Slot);
                        }
                        continue;
                    }
                    if (Authoritative) Grant(r.Slot, i, Roll(r.Slot));
                    else
                    {
                        b.PendingUntil = Time.time + 1.5f;
                        b.T.gameObject.SetActive(false);
                        PickupRequested?.Invoke(r.Slot, i);
                    }
                }
            }
        }

        /// Бонус по месту в гонке (отстающим чаще ракеты и турбо, лидеру — щит и мины).
        PowerUpKind Roll(int slot)
        {
            var order = race.Standings();
            int rank = order.FindIndex(x => x.Slot == slot);
            return settings.Roll(Mathf.Max(0, rank), order.Count, random.NextDouble());
        }

        /// Хост: просьба клиента взять ящик — проверка (ящик есть, машина рядом, руки свободны) и выдача.
        public bool HandlePickupRequest(int slot, int boxIndex)
        {
            if (!Authoritative || boxIndex < 0 || boxIndex >= boxList.Count) return false;
            var b = boxList[boxIndex];
            var r = race.BySlot(slot);
            if (!b.Active || r == null || !Alive(r) || HolderOf(slot).Item != PowerUpKind.None) return false;
            if ((r.Car.position - b.Home).sqrMagnitude > 12f * 12f) return false;
            Grant(slot, boxIndex, Roll(slot));
            return true;
        }

        void Grant(int slot, int boxIndex, PowerUpKind kind) => ApplyGrant(slot, kind, boxIndex, true);

        /// Выдача бонуса (у хоста — решение, у клиента — пришло от хоста).
        public void ApplyGrant(int slot, PowerUpKind kind, int boxIndex, bool raise = false)
        {
            var h = HolderOf(slot);
            h.Item = kind;
            h.GotAt = Time.time;
            h.AiUseAt = Time.time + Mathf.Lerp(0.6f, 2.5f, (float)random.NextDouble());
            Pickups++;
            if (boxIndex >= 0 && boxIndex < boxList.Count)
            {
                var b = boxList[boxIndex];
                b.Active = false;
                b.PendingUntil = 0f;
                b.RespawnAt = Time.time + settings.RespawnSeconds;
                b.T.gameObject.SetActive(false);
                var fx = ChaosFx.Instance;
                if (fx != null)
                {
                    fx.Blast(b.Home, BlastStyle.Spark, 0.8f);
                    fx.Sound(SoundBank.Pickup, b.Home, 0.9f, 1f);
                    var def = settings.Get(kind);
                    if (def != null) fx.Pop(b.Home + Vector3.up * 1.6f, def.Title.ToUpperInvariant() + "!", def.Color, 0.9f);
                }
            }
            if (raise) Granted?.Invoke(slot, kind, boxIndex);
        }

        /// Отладка и проверки: дать бонус участнику без ящика.
        public void Give(int slot, PowerUpKind kind) => ApplyGrant(slot, kind, -1, true);

        // ---------- применение ----------

        /// Игрок или ИИ применяет свой бонус. У клиента сети — просьба хосту.
        public void RequestUse(int slot)
        {
            if (race.Phase != RacePhase.Running || HeldBy(slot) == PowerUpKind.None) return;
            var r = race.BySlot(slot);
            if (r == null || !Alive(r)) return;
            if (Authoritative) Use(slot);
            else UseRequested?.Invoke(slot);
        }

        /// Хост: проверка (бонус есть) и применение; всем — Used с целью и номером.
        public bool Use(int slot)
        {
            if (!Authoritative) return false;
            var h = HolderOf(slot);
            var r = race.BySlot(slot);
            if (h.Item == PowerUpKind.None || r == null || !Alive(r)) return false;
            var use = new PowerUpUse
            {
                Slot = slot,
                Kind = h.Item,
                Serial = ++serial,
                Target = -1,
                Position = r.Car.position,
                Forward = r.Car.forward,
            };
            if (use.Kind == PowerUpKind.HomingRocket) use.Target = PickRocketTarget(slot, r.Car.position, r.Car.forward);
            if (use.Kind == PowerUpKind.LeaderRocket) use.Target = PickLeader(slot);
            Execute(use);
            Used?.Invoke(use);
            return true;
        }

        /// Применение у всех (у хоста — сразу, у клиентов — по сообщению хоста).
        public void Execute(PowerUpUse use)
        {
            HolderOf(use.Slot).Item = PowerUpKind.None;
            if (use.Serial > serial) serial = use.Serial;
            Uses++;
            UsesByKind.TryGetValue(use.Kind, out int n);
            UsesByKind[use.Kind] = n + 1;
            var user = race.BySlot(use.Slot);
            var fx = ChaosFx.Instance;
            Vector3 at = user != null && user.Car != null ? user.Car.position : use.Position;
            switch (use.Kind)
            {
                case PowerUpKind.HomingRocket:
                case PowerUpKind.LeaderRocket:
                    LaunchRocket(use, user);
                    break;
                case PowerUpKind.MachineGun:
                    StartGun(use.Slot);
                    break;
                case PowerUpKind.Turbo:
                    if (user != null && Simulates(user)) user.Controller.ApplyTurbo(settings.TurboSeconds);
                    fx?.Sound(SoundBank.Whoosh, at, 1f, 0.7f);
                    fx?.Blast(at - (user != null ? user.Car.forward : use.Forward) * 2.2f, BlastStyle.Mini, 0.8f);
                    fx?.Pop(at + Vector3.up * 2.5f, "ТУРБО!", new Color(1f, 0.6f, 0.2f), 1.2f);
                    break;
                case PowerUpKind.SuperJump:
                    if (user != null && Simulates(user)) user.Controller.ApplyJump(16f);
                    fx?.Sound(SoundBank.Coil, at, 1f, 1.2f);
                    fx?.Blast(at, BlastStyle.ColorPuff, 1f, new Color(0.7f, 1f, 0.4f, 0.8f));
                    fx?.Pop(at + Vector3.up * 2.5f, "ХОП!", new Color(0.65f, 1f, 0.3f), 1.3f);
                    break;
                case PowerUpKind.Shock:
                    Shock(use.Slot, at);
                    break;
                case PowerUpKind.Shield:
                    if (user != null && Simulates(user)) user.Controller.RaiseShield(settings.ShieldSeconds);
                    fx?.Sound(SoundBank.Warp, at, 0.9f, 1.4f);
                    fx?.Pop(at + Vector3.up * 2.5f, "ЩИТ!", new Color(0.3f, 1f, 0.9f), 1.2f);
                    break;
                case PowerUpKind.Mine:
                    DropMineAt(use, user);
                    break;
                case PowerUpKind.Horn:
                    Horn(use.Slot, at);
                    break;
            }
        }

        // ---------- выбор цели ----------

        /// Цель самонаводящейся ракеты: ближайший соперник впереди по трассе (до RocketRange м, в переднем секторе);
        /// если впереди никого — ближайший рядом (до 60 м). -1 — целей нет (ракета летит вперёд и взрывается).
        public int PickRocketTarget(int owner, Vector3 from, Vector3 forward)
        {
            var course = race.Course;
            if (course == null) return -1;
            float length = course.Length;
            float myProgress = course.Nearest(from);
            int best = -1;
            float bestAhead = float.MaxValue;
            int near = -1;
            float nearDistance = 60f;
            foreach (var r in race.Racers)
            {
                if (r.Slot == owner || !Alive(r)) continue;
                float ahead = TrackPath.Wrap(r.Progress - myProgress) * length;
                if (ahead > length * 0.5f) ahead -= length;
                Vector3 d = r.Car.position - from;
                float distance = d.magnitude;
                bool inFront = Vector3.Dot(d.normalized, forward) > -0.2f;
                if (ahead > -5f && ahead < settings.RocketRange && inFront && ahead < bestAhead)
                {
                    bestAhead = ahead;
                    best = r.Slot;
                }
                if (distance < nearDistance)
                {
                    nearDistance = distance;
                    near = r.Slot;
                }
            }
            return best >= 0 ? best : near;
        }

        /// Цель ракеты на лидера: первое место по настоящему порядку гонки (круги, сектора, прогресс — RaceManager.Standings),
        /// кроме самого стрелка (если лидер он — ракета летит во второго). Цель фиксируется при запуске.
        public int PickLeader(int owner)
        {
            foreach (var r in race.Standings())
                if (r.Slot != owner && Alive(r)) return r.Slot;
            return -1;
        }

        // ---------- ракеты ----------

        void LaunchRocket(PowerUpUse use, Racer user)
        {
            Rocket rocket = null;
            foreach (var r in rocketList) if (!r.Live) { rocket = r; break; }
            if (rocket == null) rocket = rocketList.Count > 0 ? rocketList[0] : null;
            if (rocket == null) return;
            KillRocket(rocket);
            bool leader = use.Kind == PowerUpKind.LeaderRocket;
            Vector3 forward = user != null && user.Car != null ? user.Car.forward : use.Forward;
            Vector3 start = (user != null && user.Car != null ? user.Car.position : use.Position) + Vector3.up * 1.6f + forward * 2.6f;
            rocket.Live = true;
            rocket.Serial = use.Serial;
            rocket.Owner = use.Slot;
            rocket.Target = use.Target;
            rocket.Leader = leader;
            rocket.Diving = false;
            rocket.Position = start;
            rocket.Velocity = forward * (leader ? settings.LeaderRocketSpeed : settings.RocketSpeed) * 0.6f + Vector3.up * (leader ? 12f : 1f);
            rocket.Born = Time.time;
            var course = race.Course;
            rocket.TrackDistance = course != null ? course.Nearest(start) * course.Length : 0f;
            rocket.Lateral = course != null ? Vector3.Dot(start - course.PointAt(course.Nearest(start)), course.SideAt(course.Nearest(start))) : 0f;
            var rig = rocket.Rig;
            rig.Homing.SetActive(!leader);
            rig.Leader.SetActive(leader);
            rig.Root.SetPositionAndRotation(start, Quaternion.LookRotation(rocket.Velocity));
            rig.Root.gameObject.SetActive(true);
            if (rig.Trail != null) rig.Trail.Play(true);
            if (rig.Engine != null) rig.Engine.Play();
            var fx = ChaosFx.Instance;
            fx?.Sound(SoundBank.Cannon, start, 0.8f, 1.5f);
            fx?.Blast(start - forward * 1.5f, BlastStyle.Mini, 0.7f);
            if (use.Target >= 0) RocketLocked?.Invoke(use.Kind, use.Slot, use.Target);
        }

        void KillRocket(Rocket r)
        {
            r.Live = false;
            if (r.Rig == null) return;
            if (r.Rig.Trail != null) r.Rig.Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (r.Rig.Engine != null) r.Rig.Engine.Stop();
            r.Rig.Root.gameObject.SetActive(false);
        }

        void UpdateRockets(float dt)
        {
            var course = race.Course;
            if (course == null) return;
            float length = course.Length;
            foreach (var rocket in rocketList)
            {
                if (!rocket.Live) continue;
                float age = Time.time - rocket.Born;
                var target = rocket.Target >= 0 ? race.BySlot(rocket.Target) : null;
                if (!Alive(target))
                {
                    if (rocket.Leader)
                    {
                        // Лидер финишировал или ушёл — ракета взрывается в небе (салют), никого не задев.
                        Explode(rocket, rocket.Position, null, false);
                        continue;
                    }
                    rocket.Target = PickRocketTarget(rocket.Owner, rocket.Position, rocket.Velocity.normalized);
                    target = rocket.Target >= 0 ? race.BySlot(rocket.Target) : null;
                }
                float lifetime = rocket.Leader ? settings.LeaderRocketLifetime : settings.RocketLifetime;
                if (age > lifetime)
                {
                    Explode(rocket, rocket.Position, null, false);
                    continue;
                }
                Vector3 aim;
                float speed = rocket.Leader ? settings.LeaderRocketSpeed : settings.RocketSpeed;
                float turn = rocket.Leader ? 8f : 6f;
                if (target == null)
                {
                    // Без цели — вдоль трассы вперёд.
                    float p = course.Nearest(rocket.Position);
                    aim = course.PointAt(p + 30f / length) + Vector3.up * 1.8f;
                }
                else if (rocket.Leader && !rocket.Diving)
                {
                    // Ракета на лидера идёт над трассой (не застревает за домами и скалами) и пикирует вблизи цели.
                    rocket.TrackDistance += speed * dt;
                    float p = TrackPath.Wrap(rocket.TrackDistance / length);
                    float targetLateral = Vector3.Dot(target.Car.position - course.PointAt(target.Progress), course.SideAt(target.Progress));
                    rocket.Lateral = Mathf.MoveTowards(rocket.Lateral, targetLateral, 6f * dt);
                    Vector3 cruise = course.PointAt(p) + course.SideAt(p) * rocket.Lateral + Vector3.up * 16f;
                    float gap = TrackPath.Wrap(target.Progress - p) * length;
                    if (gap < 40f || gap > length - 20f) rocket.Diving = true;
                    Vector3 step = cruise - rocket.Position;
                    rocket.Velocity = step.sqrMagnitude > 0.01f ? step / Mathf.Max(dt, 1e-4f) : rocket.Velocity;
                    rocket.Velocity = Vector3.ClampMagnitude(rocket.Velocity, speed * 1.5f);
                    rocket.Position = cruise;
                    PlaceRocket(rocket);
                    continue;
                }
                else
                {
                    Vector3 targetPoint = target.Car.position + Vector3.up * 0.7f;
                    float rocketP = course.Nearest(rocket.Position);
                    float ahead = TrackPath.Wrap(target.Progress - rocketP) * length;
                    if (ahead > length * 0.5f) ahead -= length;
                    // Издалека — вдоль трассы (повороты, дома, скалы), вблизи — прямо в машину.
                    if (!rocket.Leader && ahead > 45f)
                    {
                        float targetLateral = Vector3.Dot(target.Car.position - course.PointAt(target.Progress), course.SideAt(target.Progress));
                        float p = rocketP + 30f / length;
                        aim = course.PointAt(p) + course.SideAt(p) * targetLateral + Vector3.up * 1.8f;
                    }
                    else aim = targetPoint;
                }
                Vector3 desired = (aim - rocket.Position).normalized;
                Vector3 dir = rocket.Velocity.sqrMagnitude > 0.01f ? rocket.Velocity.normalized : desired;
                dir = Vector3.RotateTowards(dir, desired, turn * dt, 0f);
                rocket.Velocity = dir * speed;
                rocket.Position += rocket.Velocity * dt;
                // Над землёй: не зарывается в асфальт.
                if (Physics.Raycast(rocket.Position + Vector3.up * 3f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore) && ground.rigidbody == null)
                    rocket.Position.y = Mathf.Max(rocket.Position.y, ground.point.y + 0.7f);
                PlaceRocket(rocket);
                CheckRocketHit(rocket);
            }
        }

        void PlaceRocket(Rocket rocket)
        {
            var t = rocket.Rig.Root;
            Vector3 v = rocket.Velocity.sqrMagnitude > 0.01f ? rocket.Velocity : t.forward;
            t.SetPositionAndRotation(rocket.Position, Quaternion.LookRotation(v) * Quaternion.Euler(0f, 0f, Time.time * 360f));
        }

        void CheckRocketHit(Rocket rocket)
        {
            foreach (var r in race.Racers)
            {
                if (!Alive(r) || (r.Slot == rocket.Owner && Time.time - rocket.Born < 1.5f)) continue;
                if ((r.Car.position + Vector3.up * 0.6f - rocket.Position).sqrMagnitude > RocketHitRadius * RocketHitRadius) continue;
                // Попадание решает компьютер, который считает физику этой машины.
                if (!Simulates(r)) continue;
                Explode(rocket, rocket.Position, r, true);
                HitReported?.Invoke(rocket.Serial, r.Slot, rocket.Position);
                return;
            }
        }

        /// Взрыв ракеты: у пострадавшего (его считает этот компьютер) — мощный подброс; ударная волна задевает соседей.
        void Explode(Rocket rocket, Vector3 at, Racer victim, bool apply)
        {
            var fx = ChaosFx.Instance;
            bool leader = rocket.Leader;
            KillRocket(rocket);
            fx?.Blast(at, BlastStyle.Big, leader ? 2f : 1.5f);
            fx?.Flash(at + Vector3.up, new Color(1f, 0.6f, 0.25f), leader ? 12f : 8f, 0.3f);
            fx?.Sound(SoundBank.BigBoom, at, 1f, leader ? 0.8f : 1.05f);
            if (victim == null)
            {
                fx?.Pop(at + Vector3.up * 2f, "ПШШ… МИМО", new Color(0.8f, 0.8f, 0.8f), 0.9f);
                return;
            }
            var kind = leader ? PowerUpKind.LeaderRocket : PowerUpKind.HomingRocket;
            bool absorbed = victim.Controller.Invulnerable;
            if (apply && !absorbed)
            {
                var car = victim.Car;
                Vector3 dir = rocket.Velocity.sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(rocket.Velocity, Vector3.up).normalized : car.forward;
                Vector3 spin = (car.forward * UnityEngine.Random.Range(-1f, 1f) + car.right * UnityEngine.Random.Range(-1f, 1f) + Vector3.up * 0.5f).normalized * (leader ? 11f : 8f);
                victim.Controller.ApplyLaunch(dir * (leader ? 6f : 10f) + Vector3.up * (leader ? 30f : 22f), spin, leader ? "ракета на лидера" : "ракета", 0);
            }
            else if (apply) victim.Controller.ApplyLaunch(Vector3.up, Vector3.zero, "ракета");
            fx?.Pop(at + Vector3.up * 2.5f, absorbed ? "ЩИТ ВЫДЕРЖАЛ!" : leader ? "ЛИДЕР В ПОЛЁТЕ!" : "БА-БАХ!", absorbed ? new Color(0.3f, 1f, 0.9f) : new Color(1f, 0.45f, 0.15f), leader ? 1.8f : 1.5f);
            // Ударная волна: соседние машины, мячи и мины — цепная реакция (саму цель волна не добивает).
            fx?.Shockwave(at, leader ? 11f : 8f, leader ? 13f : 10f, 1, victim.Controller);
            CountHit(kind, rocket.Owner, victim.Slot, absorbed);
        }

        void CountHit(PowerUpKind kind, int owner, int victim, bool absorbed)
        {
            if (!absorbed)
            {
                HitsByKind.TryGetValue(kind, out int n);
                HitsByKind[kind] = n + 1;
                if (kind == PowerUpKind.HomingRocket || kind == PowerUpKind.LeaderRocket) RocketHits++;
                if (kind == PowerUpKind.Mine) MineHits++;
            }
            HitLanded?.Invoke(kind, owner, victim, absorbed);
        }

        /// Сеть: попадание решил другой компьютер (машина его) — взорвать снаряд здесь (силы к машине уже применены владельцем).
        public void ReceiveHit(int hitSerial, int slot, Vector3 point)
        {
            var victim = race.BySlot(slot);
            foreach (var rocket in rocketList)
            {
                if (!rocket.Live || rocket.Serial != hitSerial) continue;
                Explode(rocket, point, victim, victim != null && Simulates(victim));
                return;
            }
            foreach (var mine in mineList)
            {
                if (!mine.Live || mine.Serial != hitSerial) continue;
                DetonateMine(mine, slot);
                return;
            }
        }

        // ---------- пулемёт ----------

        void StartGun(int slot)
        {
            Gun gun = null;
            foreach (var g in gunList) if (g.Slot == slot) gun = g;
            if (gun == null) foreach (var g in gunList) if (g.Slot < 0) { gun = g; break; }
            if (gun == null) return;
            gun.Slot = slot;
            gun.Until = Time.time + settings.GunSeconds;
            gun.Rounds = settings.GunRounds;
            gun.NextShot = Time.time + 0.25f;
            gun.Rig.Root.gameObject.SetActive(true);
            var r = race.BySlot(slot);
            if (r != null) MountGun(gun, r);
            ChaosFx.Instance?.Sound(SoundBank.Clank, gun.Rig.Root.position, 0.8f, 1.3f);
        }

        void StopGun(Gun g)
        {
            g.Slot = -1;
            g.Rounds = 0;
            if (g.Rig != null) g.Rig.Root.gameObject.SetActive(false);
        }

        /// Пулемёт — на крыше над лобовым стеклом (по коллайдеру кузова этой машины).
        void MountGun(Gun g, Racer r)
        {
            // Габариты кузова: верх кабины, чуть впереди её середины.
            Vector3 local = new Vector3(0f, 1.4f, 0.4f);
            if (r.Controller != null)
            {
                var b = r.Controller.BodyBounds;
                local = new Vector3(0f, b.max.y, b.center.z + b.size.z * 0.12f);
            }
            g.Rig.Root.SetPositionAndRotation(r.Car.TransformPoint(local), r.Car.rotation);
            if (g.Rig.Barrels != null) g.Rig.Barrels.localPosition = new Vector3(0f, 0f, -g.Recoil);
            g.Rig.Root.rotation = r.Car.rotation * Quaternion.Euler(0f, g.Yaw, 0f);
        }

        /// Турель доворачивает к сопернику впереди (до 14° от курса, до 75 м): очередь идёт по стволам, а стволы — на цель.
        float AimYaw(Racer shooter)
        {
            float best = 0f, bestScore = float.MaxValue;
            foreach (var o in race.Racers)
            {
                if (o == shooter || !Alive(o)) continue;
                Vector3 d = o.Car.position - shooter.Car.position;
                float distance = d.magnitude;
                if (distance > 75f || distance < 2f) continue;
                float yaw = Vector3.SignedAngle(shooter.Car.forward, Vector3.ProjectOnPlane(d, shooter.Car.up), shooter.Car.up);
                if (Mathf.Abs(yaw) > 14f) continue;
                float score = distance + Mathf.Abs(yaw) * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = yaw;
                }
            }
            return best;
        }

        void UpdateGuns(float dt)
        {
            foreach (var g in gunList)
            {
                if (g.Slot < 0) continue;
                g.Recoil = Mathf.MoveTowards(g.Recoil, 0f, dt * 1.5f);
                var r = race.BySlot(g.Slot);
                if (r == null || !Alive(r) || Time.time > g.Until || g.Rounds <= 0)
                {
                    StopGun(g);
                    continue;
                }
                g.Yaw = Mathf.MoveTowards(g.Yaw, AimYaw(r), 60f * dt);
                if (Time.time < g.NextShot) continue;
                g.NextShot = Time.time + 1f / GunRate;
                g.Rounds--;
                g.Left = !g.Left;
                var muzzle = g.Left ? g.Rig.MuzzleLeft : g.Rig.MuzzleRight;
                Vector3 gunForward = r.Car.rotation * Quaternion.Euler(0f, g.Yaw, 0f) * Vector3.forward;
                Vector3 origin = muzzle != null ? muzzle.position : g.Rig.Root.position + gunForward * 1.5f;
                // Пули летят по стволам турели; лёгкий разброс очереди, горизонтально (не в асфальт на склоне).
                Vector3 level = Vector3.ProjectOnPlane(gunForward, Vector3.up).normalized;
                Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-0.8f, 0.8f), Vector3.up) * Vector3.Slerp(level, gunForward, 0.5f);
                dir = (dir + Vector3.up * UnityEngine.Random.Range(-0.006f, 0.006f) - Vector3.up * 0.012f).normalized;
                Fire(g.Slot, origin, dir);
                g.Recoil = 0.12f;
                if (g.Rig.Flash != null)
                {
                    g.Rig.Flash.transform.position = origin;
                    g.Rig.Flash.Play(true);
                }
                if (g.Rig.Audio != null) g.Rig.Audio.PlayOneShot(SoundBank.GunShot, 0.55f);
            }
        }

        void Fire(int shooter, Vector3 origin, Vector3 dir)
        {
            if (bulletList.Count == 0) return;
            BulletsFired++;
            var b = bulletList[nextBullet];
            nextBullet = (nextBullet + 1) % bulletList.Count;
            b.Live = true;
            b.Shooter = shooter;
            b.Position = origin;
            b.Direction = dir;
            b.Travel = 0f;
            b.Tracer.gameObject.SetActive(true);
            b.Tracer.SetPosition(0, origin);
            b.Tracer.SetPosition(1, origin + dir * 0.5f);
        }

        void KillBullet(Bullet b)
        {
            b.Live = false;
            if (b.Tracer != null) b.Tracer.gameObject.SetActive(false);
        }

        void UpdateBullets(float dt)
        {
            float speed = settings.BulletSpeed;
            foreach (var b in bulletList)
            {
                if (!b.Live) continue;
                float step = speed * dt;
                var shooter = race.BySlot(b.Shooter);
                int count = Physics.SphereCastNonAlloc(b.Position, 0.25f, b.Direction, hits, step, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                int hitIndex = -1;
                for (int i = 0; i < count; i++)
                {
                    var h = hits[i];
                    if (shooter != null && h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.transform == shooter.Car) continue;
                    if (h.collider.GetComponentInParent<GunMarker>() != null) continue;
                    if (h.distance < nearest)
                    {
                        nearest = h.distance;
                        hitIndex = i;
                    }
                }
                if (hitIndex >= 0)
                {
                    var h = hits[hitIndex];
                    Vector3 point = h.distance > 0f ? h.point : b.Position;
                    LastBulletHit = h.collider.name + " @" + h.distance.ToString("F1");
                    var car = h.collider.attachedRigidbody != null ? h.collider.attachedRigidbody.GetComponent<CarController>() : null;
                    BulletImpact(b, point, car);
                    KillBullet(b);
                    continue;
                }
                b.Position += b.Direction * step;
                b.Travel += step;
                b.Tracer.SetPosition(0, b.Position - b.Direction * Mathf.Min(6f, b.Travel));
                b.Tracer.SetPosition(1, b.Position);
                if (b.Travel > settings.BulletRange) KillBullet(b);
            }
        }

        float sparkAt;

        void BulletImpact(Bullet b, Vector3 point, CarController car)
        {
            var fx = ChaosFx.Instance;
            if (Time.time - sparkAt > 0.05f)
            {
                sparkAt = Time.time;
                fx?.Blast(point, BlastStyle.Spark, car != null ? 0.45f : 0.25f);
            }
            if (car == null) return;
            fx?.Sound(SoundBank.Knock, point, 0.5f, UnityEngine.Random.Range(1.4f, 1.8f));
            // Толкает только владелец машины; очередь из нескольких попаданий сбивает с траектории.
            if (!car.Physics.Simulated) return;
            bool absorbed = car.Invulnerable;
            car.ApplyImpulse(b.Direction * settings.BulletPush + Vector3.up * 0.35f + Vector3.Cross(Vector3.up, b.Direction) * UnityEngine.Random.Range(-0.8f, 0.8f));
            if (!absorbed) BulletHits++;
            var victim = FindRacer(car);
            if (victim != null && !absorbed && Time.time - lastGunMessage > 1.2f)
            {
                lastGunMessage = Time.time;
                CountHit(PowerUpKind.MachineGun, b.Shooter, victim.Slot, false);
            }
        }

        float lastGunMessage;

        Racer FindRacer(CarController car)
        {
            foreach (var r in race.Racers) if (r.Controller == car) return r;
            return null;
        }

        // ---------- мины ----------

        void DropMineAt(PowerUpUse use, Racer user)
        {
            DropMine mine = null;
            foreach (var m in mineList) if (!m.Live) { mine = m; break; }
            if (mine == null)
            {
                // Пул полон — самая старая мина исчезает.
                float oldest = float.MaxValue;
                foreach (var m in mineList) if (m.DieAt < oldest) { oldest = m.DieAt; mine = m; }
            }
            if (mine == null) return;
            Vector3 forward = user != null && user.Car != null ? user.Car.forward : use.Forward;
            Vector3 at = (user != null && user.Car != null ? user.Car.position : use.Position) - forward * 4f;
            mine.Position = GroundAt(at);
            mine.Live = true;
            mine.Serial = use.Serial;
            mine.Owner = use.Slot;
            mine.ArmedAt = Time.time + 0.8f;
            mine.OwnerSafeUntil = Time.time + 2f;
            mine.DieAt = Time.time + settings.MineLifetime;
            mine.T.SetPositionAndRotation(mine.Position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            mine.T.gameObject.SetActive(true);
            ChaosFx.Instance?.Sound(SoundBank.Clank, mine.Position, 0.8f, 0.8f);
        }

        void KillMine(DropMine m)
        {
            m.Live = false;
            if (m.T != null) m.T.gameObject.SetActive(false);
        }

        void UpdateMines()
        {
            foreach (var m in mineList)
            {
                if (!m.Live) continue;
                if (Time.time > m.DieAt)
                {
                    ChaosFx.Instance?.Blast(m.Position, BlastStyle.Fake, 0.5f);
                    KillMine(m);
                    continue;
                }
                bool armed = Time.time >= m.ArmedAt;
                if (m.Lamp != null)
                {
                    m.Lamp.GetPropertyBlock(props);
                    float on = armed ? (Mathf.Repeat(Time.time * 3f, 1f) < 0.5f ? 1f : 0.15f) : 0.4f;
                    props.SetColor(EmissionId, new Color(1f, 0.15f, 0.1f) * (on * 6f));
                    m.Lamp.SetPropertyBlock(props);
                }
                if (!armed) continue;
                foreach (var r in race.Racers)
                {
                    if (!Alive(r) || !Simulates(r) || (r.Slot == m.Owner && Time.time < m.OwnerSafeUntil)) continue;
                    Vector3 d = r.Car.position - m.Position;
                    if (Mathf.Abs(d.y) > 3f || d.x * d.x + d.z * d.z > MineTouchRadius * MineTouchRadius) continue;
                    DetonateMine(m, r.Slot);
                    HitReported?.Invoke(m.Serial, r.Slot, m.Position);
                    break;
                }
            }
        }

        /// Взрыв мины: каждый компьютер подбрасывает свои машины в радиусе; волна поджигает соседние мины.
        void DetonateMine(DropMine m, int victimSlot)
        {
            if (!m.Live) return;
            KillMine(m);
            var fx = ChaosFx.Instance;
            fx?.Blast(m.Position, BlastStyle.Big, 1.4f);
            fx?.Sound(SoundBank.BigBoom, m.Position, 1f, 1.1f);
            fx?.Pop(m.Position + Vector3.up * 2.5f, "МИНА!", new Color(1f, 0.35f, 0.25f), 1.4f);
            foreach (var r in race.Racers)
            {
                if (!Alive(r) || !Simulates(r)) continue;
                Vector3 d = r.Car.position - m.Position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > MineBlastRadius) continue;
                float k = 1f - dist / MineBlastRadius * 0.5f;
                Vector3 away = dist > 0.2f ? d / dist : r.Car.forward;
                bool absorbed = r.Controller.Invulnerable;
                r.Controller.ApplyLaunch(away * 8f * k + Vector3.up * 23f * k, (r.Car.forward * UnityEngine.Random.Range(-8f, 8f) + Vector3.up * UnityEngine.Random.Range(-3f, 3f)) * k, "мина соперника");
                if (r.Slot == victimSlot) CountHit(PowerUpKind.Mine, m.Owner, r.Slot, absorbed);
            }
            fx?.Shockwave(m.Position, 8f, 11f, 1);
        }

        /// Чужой взрыв рядом: оставленная мина детонирует (решает хост — всем как попадание).
        public void OnShock(Vector3 position, float radius, float strength, int generation)
        {
            if (!Authoritative) return;
            foreach (var m in mineList)
            {
                if (!m.Live || Time.time < m.ArmedAt) continue;
                float d = (m.Position - position).magnitude;
                if (d < 0.3f || d > radius) continue;
                ChaosFx.NoteChain(generation);
                DetonateMine(m, -1);
                HitReported?.Invoke(m.Serial, -1, m.Position);
            }
        }

        // ---------- разряд и клаксон ----------

        /// Электроразряд: молнии во всех соседей в радиусе — их трясёт («пьяный» руль), скорость падает, машину подкидывает.
        void Shock(int slot, Vector3 at)
        {
            var fx = ChaosFx.Instance;
            fx?.Sound(SoundBank.Zap, at, 1f, 0.7f);
            fx?.Flash(at + Vector3.up * 2f, new Color(0.45f, 0.9f, 1f), 9f, 0.3f);
            fx?.Pop(at + Vector3.up * 3f, "ЗЗЗТ!", new Color(0.45f, 0.9f, 1f), 1.4f);
            foreach (var r in race.Racers)
            {
                if (r.Slot == slot || !Alive(r) || (r.Car.position - at).sqrMagnitude > settings.ShockRadius * settings.ShockRadius) continue;
                ShowBolt(slot, r.Slot);
                fx?.Blast(r.Car.position + Vector3.up, BlastStyle.Spark, 0.9f);
                if (!Simulates(r)) continue;
                bool absorbed = r.Controller.Invulnerable;
                r.Controller.ApplyLaunch(Vector3.up * 5f, Vector3.up * UnityEngine.Random.Range(-5f, 5f), "электроразряд");
                r.Controller.ApplySlow(0.5f, 1.3f);
                r.Controller.ApplyControlGag(ControlGag.Wobbly, 1.6f);
                CountHit(PowerUpKind.Shock, slot, r.Slot, absorbed);
            }
        }

        void ShowBolt(int from, int to)
        {
            foreach (var b in boltList)
            {
                if (Time.time < b.Until) continue;
                b.From = from;
                b.To = to;
                b.Until = Time.time + 0.45f;
                b.Line.gameObject.SetActive(true);
                return;
            }
        }

        void UpdateBolts()
        {
            foreach (var b in boltList)
            {
                if (!b.Line.gameObject.activeSelf) continue;
                var a = race.BySlot(b.From);
                var c = race.BySlot(b.To);
                if (Time.time > b.Until || a == null || c == null)
                {
                    b.Line.gameObject.SetActive(false);
                    continue;
                }
                // Ломаная молния, каждый кадр новая.
                Vector3 p0 = a.Car.position + Vector3.up * 1.3f, p1 = c.Car.position + Vector3.up * 1.1f;
                int n = b.Line.positionCount;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    Vector3 jitter = i == 0 || i == n - 1 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * 0.9f;
                    b.Line.SetPosition(i, Vector3.Lerp(p0, p1, t) + jitter + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f);
                }
            }
        }

        /// Суперклаксон: «БИ-БИИИП!» — звуковая волна расшвыривает соседей и мячи (без оглушения, в отличие от разряда).
        void Horn(int slot, Vector3 at)
        {
            var fx = ChaosFx.Instance;
            fx?.Sound(SoundBank.Horn, at, 1f, 1f);
            fx?.Blast(at, BlastStyle.ColorPuff, 3f, new Color(1f, 0.55f, 0.8f, 0.5f));
            fx?.Pop(at + Vector3.up * 3f, "БИ-БИИИП!", new Color(1f, 0.4f, 0.7f), 1.6f);
            float radius = settings.HornRadius;
            foreach (var r in race.Racers)
            {
                if (r.Slot == slot || !Alive(r)) continue;
                Vector3 d = r.Car.position - at;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > radius || !Simulates(r)) continue;
                float k = 1f - dist / radius * 0.6f;
                Vector3 away = dist > 0.2f ? d / dist : r.Car.right;
                bool absorbed = r.Controller.Invulnerable;
                r.Controller.ApplyLaunch(away * 22f * k + Vector3.up * 7f * k, Vector3.up * UnityEngine.Random.Range(-6f, 6f) * k, "суперклаксон");
                CountHit(PowerUpKind.Horn, slot, r.Slot, absorbed);
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live) continue;
                Vector3 d = body.transform.position - at;
                if (d.magnitude > radius) continue;
                body.Kick((d.normalized + Vector3.up * 0.4f) * 14f);
            }
        }

        // ---------- пузырь защиты и щита ----------

        readonly Dictionary<int, BubbleRig> bubbleOf = new Dictionary<int, BubbleRig>();

        void UpdateBubbles()
        {
            if (bubbles.Count == 0) return;
            foreach (var r in race.Racers)
            {
                var c = r.Controller;
                bool on = r.Car != null && !r.Retired && (c.Protected || c.Shielded);
                bubbleOf.TryGetValue(r.Slot, out var rig);
                if (!on)
                {
                    if (rig != null)
                    {
                        rig.Root.gameObject.SetActive(false);
                        bubbleOf.Remove(r.Slot);
                    }
                    continue;
                }
                if (rig == null)
                {
                    foreach (var b in bubbles)
                        if (!bubbleOf.ContainsValue(b)) { rig = b; break; }
                    if (rig == null) continue;
                    bubbleOf[r.Slot] = rig;
                    rig.Root.gameObject.SetActive(true);
                }
                Vector3 size = new Vector3(2f, 1.4f, 4.4f);
                Vector3 center = r.Car.position + Vector3.up;
                if (r.Controller != null)
                {
                    var b = r.Controller.BodyBounds;
                    size = Vector3.Scale(b.size, r.Car.lossyScale);
                    center = r.Car.TransformPoint(b.center);
                }
                rig.Root.SetPositionAndRotation(center, r.Car.rotation);
                rig.Root.localScale = new Vector3(size.x * 1.25f, size.y * 1.6f, size.z * 1.15f);
                // Защита после возврата — золотистая и мерцает, щит — бирюзовый; к концу плавно гаснут.
                bool shield = c.Shielded;
                float left = shield ? (c.ShieldLeft > 0f ? c.ShieldLeft : 1f) : (c.ProtectionLeft > 0f ? c.ProtectionLeft : 1f);
                float fade = Mathf.Clamp01(left / 0.4f);
                float flicker = shield ? 1f : 0.65f + 0.35f * Mathf.Sin(Time.time * 26f);
                Color color = shield ? new Color(0.25f, 1f, 0.9f, 0.32f) : new Color(1f, 0.85f, 0.35f, 0.3f);
                color.a *= fade * flicker;
                rig.Shell.GetPropertyBlock(props);
                props.SetColor(BaseColorId, color);
                rig.Shell.SetPropertyBlock(props);
            }
        }

        // ---------- ИИ ----------

        /// ИИ (только на хосте): применяет бонусы по ситуации — ракету, когда есть цель; пулемёт, когда соперник
        /// прямо впереди; турбо на прямой; щит, когда в него летит ракета; мину, когда сзади преследователь…
        void UpdateAi()
        {
            var course = race.Course;
            if (course == null) return;
            foreach (var r in race.Racers)
            {
                // ИИ хоста и своя машина на автопилоте (проверки) — сами решают, когда применить.
                bool ai = r.Kind == ParticipantKind.Ai && Authoritative;
                bool autopilot = r.Kind == ParticipantKind.LocalPlayer && r.Car.TryGetComponent<AiDriver>(out var driver) && driver.enabled;
                if (!(ai || autopilot) || !Alive(r)) continue;
                var h = HolderOf(r.Slot);
                if (h.Item == PowerUpKind.None || Time.time < h.AiUseAt) continue;
                float held = Time.time - h.GotAt;
                if (AiWants(r, h.Item, held, course)) RequestUse(r.Slot);
            }
        }

        bool AiWants(Racer r, PowerUpKind item, float held, TrackPath course)
        {
            float length = course.Length;
            int ahead20 = 0, near = 0, behind = 0;
            Racer front = null;
            float frontGap = float.MaxValue;
            foreach (var o in race.Racers)
            {
                if (o == r || !Alive(o)) continue;
                float gap = TrackPath.Wrap(o.Progress - r.Progress) * length;
                if (gap > length * 0.5f) gap -= length;
                float distance = (o.Car.position - r.Car.position).magnitude;
                if (distance < 15f) near++;
                if (gap > 0f && gap < 20f) ahead20++;
                if (gap < 0f && gap > -40f) behind++;
                if (gap > 2f && gap < frontGap)
                {
                    frontGap = gap;
                    front = o;
                }
            }
            switch (item)
            {
                case PowerUpKind.HomingRocket:
                    return PickRocketTarget(r.Slot, r.Car.position, r.Car.forward) >= 0 || held > 10f;
                case PowerUpKind.LeaderRocket:
                    return true;
                case PowerUpKind.MachineGun:
                    if (front == null || frontGap > 55f) return held > 14f;
                    float lateral = Vector3.Dot(front.Car.position - r.Car.position, r.Car.right);
                    return Mathf.Abs(lateral) < 4f || held > 14f;
                case PowerUpKind.Turbo:
                    return Straight(course, r.Progress, length) && r.Controller.Speed > 12f;
                case PowerUpKind.SuperJump:
                    return HazardAhead(r, course) || held > 9f;
                case PowerUpKind.Shock:
                case PowerUpKind.Horn:
                    return near > 0 || held > 12f;
                case PowerUpKind.Shield:
                    return RocketComing(r.Slot) || HazardAhead(r, course) || held > 12f;
                case PowerUpKind.Mine:
                    return behind > 0 || held > 10f;
            }
            return held > 8f;
        }

        static bool Straight(TrackPath course, float progress, float length)
        {
            for (float d = 0f; d <= 80f; d += 20f)
            {
                int i = Mathf.FloorToInt(TrackPath.Wrap(progress + d / length) * course.Count) % course.Count;
                if (Mathf.Abs(course.CurvatureAt(i, 4)) > 1f / 90f) return false;
            }
            return true;
        }

        bool RocketComing(int slot)
        {
            foreach (var rocket in rocketList) if (rocket.Live && rocket.Target == slot) return true;
            return false;
        }

        static bool HazardAhead(Racer r, TrackPath course)
        {
            var director = EventDirector.Current;
            if (director == null) return false;
            float length = course.Length;
            foreach (var h in director.Hazards)
            {
                if (h.Good) continue;
                float gap = TrackPath.Wrap(h.Progress - r.Progress) * length - h.HalfLength;
                if (gap > 5f && gap < 22f) return true;
            }
            return false;
        }

        /// ИИ без бонуса заезжает в ящик впереди (до 60 м, если до него можно доехать без резкой перекладки).
        public bool BoxLane(CarController car, float progress, float lane, out float lateral)
        {
            lateral = lane;
            var course = race.Course;
            if (course == null || boxList.Count == 0) return false;
            var r = FindRacer(car);
            if (r == null || HeldBy(r.Slot) != PowerUpKind.None) return false;
            float length = course.Length;
            float best = float.MaxValue;
            foreach (var b in boxList)
            {
                if (!b.Active) continue;
                float gap = TrackPath.Wrap(b.Place.Progress - progress) * length;
                if (gap < 6f || gap > 60f) continue;
                float shift = Mathf.Abs(b.Place.Lateral - lane);
                if (shift > 5f) continue;
                float cost = shift + gap * 0.02f;
                if (cost < best)
                {
                    best = cost;
                    lateral = b.Place.Lateral;
                }
            }
            return best < float.MaxValue;
        }
    }
}
