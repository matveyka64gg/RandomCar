using System.Collections.Generic;
using RandomCar.Core;
using RandomCar.Race;
using UnityEngine;

namespace RandomCar.Maps
{
    /// Ночное шоу «Ракетный дождь» над «Тель-Авивом» (вымышленная аркадная постановка): волны ракет приходят
    /// с горизонта, ПВО запускает перехватчики, в небе — вспышки, кольца дыма, искры; свет вспышек ложится
    /// на облака, крыши, мокрый асфальт и море. Часть ракет бьёт по городу в стороне от трассы впереди игрока
    /// (видно, но на гонку не влияет). На дорогу ракеты попадают только через честное событие «Ракетный удар»
    /// (BarrageEvent): метки заранее, ракета летит с горизонта к метке (BeginStrike), часть сбивает ПВО.
    /// Волна с сиреной просит директор событий поставить такой удар впереди игрока (временная волна).
    /// Объекты — заранее созданные пулы с лимитами; частицы ограничены, дальние вспышки — облегчённые.
    public sealed class RocketShow : MonoBehaviour
    {
        sealed class Rocket
        {
            public Transform T;
            public TrailRenderer Trail;
            public Renderer Flame;
            public Vector3 P0, P1, P2;
            public float Time, Duration;
            public bool Flying;
            public float InterceptAt = -1f;   // доля пути, на которой встретит перехватчик
            public float FadeAt = -1f;        // уходит в облака
            public bool FarImpact;
            public bool CityImpact;
            public float ImpactScale;
            public float Linger;
            public float StartTime;
            public Strike Strike;
            /// Масштаб модели и ширины следа: ракеты, идущие к земле рядом с трассой, тоньше небесных
            /// (иначе у камеры их след перекрывает полэкрана).
            public float Scale = 1f;
        }

        /// Ракета, летящая к метке удара на дороге. ArriveAt можно уточнять в полёте (событие знает точный
        /// момент удара только к своей активной фазе). Intercepted — её сбила ПВО, удара не будет.
        public sealed class Strike
        {
            public Vector3 Target;
            public float ArriveAt;
            public bool Intercept;
            public bool Intercepted;
            public bool Arrived;
        }

        sealed class Interceptor
        {
            public Transform T;
            public TrailRenderer Trail;
            public Vector3 P0, P1, P2;
            public float Time, Duration;
            public bool Flying;
            public Rocket Target;
            public bool Miss;
            public float Linger;
        }

        sealed class Flash
        {
            public Transform T;
            public Renderer R;
            public float Age, Life, Size;
            public Color Color;
            public bool Active;
        }

        [SerializeField] Transform[] rocketObjects = new Transform[0];
        [SerializeField] Transform[] interceptorObjects = new Transform[0];
        [SerializeField] Renderer[] flashObjects = new Renderer[0];
        [SerializeField] Light[] lights = new Light[0];
        [SerializeField] ParticleSystem sparks;
        [SerializeField] ParticleSystem smoke;
        [SerializeField] AudioSource siren;
        [SerializeField] AudioSource booms;
        [SerializeField] AudioClip boomClip;
        [SerializeField] Vector3 cityCenter;
        [SerializeField] Vector3 threatDirection = Vector3.right;
        [SerializeField] Vector3[] batteries = new Vector3[0];

        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        readonly List<Rocket> rockets = new List<Rocket>();
        readonly List<Interceptor> interceptors = new List<Interceptor>();
        readonly List<Flash> flashes = new List<Flash>();
        readonly float[] lightAge = new float[8];
        readonly float[] lightPeak = new float[8];
        readonly List<(float at, float volume)> pendingBooms = new List<(float, float)>();
        MaterialPropertyBlock block;
        System.Random random = new System.Random(77);
        float nextWave;
        float lullUntil;
        float nextLull;
        float sirenCooldown;
        RaceManager race;
        float nextRoadStrike;

        /// Шоу текущей карты (событие «Ракетный удар» ведёт свои ракеты через него). Сбрасывается в OnDisable.
        public static RocketShow Instance { get; private set; }

        /// Сколько объектов сейчас в небе (проверки и отладка).
        public int ActiveRockets { get; private set; }
        public int ActiveInterceptors { get; private set; }
        public int ActiveFlashes { get; private set; }
        public int Capacity => rocketObjects.Length + interceptorObjects.Length + flashObjects.Length;

        public void Configure(Transform[] rocketPool, Transform[] interceptorPool, Renderer[] flashPool, Light[] lightPool,
                              ParticleSystem sparkSystem, ParticleSystem smokeSystem, AudioSource sirenSource, AudioSource boomSource, AudioClip boom,
                              Vector3 centre, Vector3 threat, Vector3[] launchers)
        {
            rocketObjects = rocketPool;
            interceptorObjects = interceptorPool;
            flashObjects = flashPool;
            lights = lightPool;
            sparks = sparkSystem;
            smoke = smokeSystem;
            siren = sirenSource;
            booms = boomSource;
            boomClip = boom;
            cityCenter = centre;
            threatDirection = threat.normalized;
            batteries = launchers;
        }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            // Хлопки — процедурный взрыв из общего банка звуков (файлов нет).
            if (boomClip == null) boomClip = RandomCar.Audio.SoundBank.Explosion;
            foreach (var t in rocketObjects)
            {
                rockets.Add(new Rocket { T = t, Trail = t.GetComponentInChildren<TrailRenderer>(true), Flame = FindRenderer(t, "Flame") });
                t.gameObject.SetActive(false);
            }
            foreach (var t in interceptorObjects)
            {
                interceptors.Add(new Interceptor { T = t, Trail = t.GetComponentInChildren<TrailRenderer>(true) });
                t.gameObject.SetActive(false);
            }
            foreach (var r in flashObjects)
            {
                flashes.Add(new Flash { T = r.transform, R = r });
                r.gameObject.SetActive(false);
            }
            foreach (var l in lights) l.enabled = false;
            nextWave = Time.time + 1.5f;
            nextLull = Time.time + Range(45f, 70f);
        }

        static Renderer FindRenderer(Transform root, string part)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains(part)) return r;
            return null;
        }

        void OnEnable()
        {
            Instance = this;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += FaceCamera;
            race = FindAnyObjectByType<RaceManager>();
            if (race != null) race.RaceStarted += OnRaceStarted;
        }

        /// Вспышки повёрнуты к той камере, что рисует кадр (игровая, камера меню, камера скриншота).
        void FaceCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            foreach (var f in flashes)
                if (f.Active) f.T.rotation = Quaternion.LookRotation(f.T.position - cam.transform.position);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= FaceCamera;
            if (race != null) race.RaceStarted -= OnRaceStarted;
            if (siren != null) siren.Stop();
        }

        /// Старт гонки: сирена воздушной тревоги и плотная волна через несколько секунд.
        void OnRaceStarted()
        {
            if (GameSettings.RocketShow <= 0) return;
            PlaySiren();
            nextWave = Time.time + 2.5f;
            nextRoadStrike = Time.time + 14f;
            lullUntil = 0f;
            nextLull = Time.time + Range(45f, 70f);
        }

        void PlaySiren()
        {
            if (siren == null || siren.clip == null || !GameSettings.AirSiren || Time.time < sirenCooldown) return;
            siren.volume = 0.5f * GameSettings.SfxVolume;
            siren.Play();
            sirenCooldown = Time.time + 75f;
        }

        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // ---------- волны ----------

        static int WaveSize(int intensity) => intensity switch { 1 => 4, 2 => 8, 3 => 14, 4 => 22, _ => 0 };

        Vector2 WaveGap(int intensity) => intensity switch
        {
            1 => new Vector2(9f, 14f),
            2 => new Vector2(6f, 10f),
            3 => new Vector2(4f, 7f),
            _ => new Vector2(2.5f, 5f),
        };

        void Update()
        {
            float dt = Time.deltaTime;
            int intensity = GameSettings.RocketShow;
            bool low = GameSettings.EffectsQuality == 0;
            if (intensity > 0 && Time.time >= nextWave)
            {
                if (Time.time >= nextLull)
                {
                    // Короткая передышка: небо затихает, потом обстрел возобновляется (и снова сирена).
                    lullUntil = Time.time + (intensity >= 4 ? Range(5f, 8f) : Range(8f, 14f));
                    nextLull = lullUntil + Range(45f, 70f);
                }
                if (Time.time < lullUntil) nextWave = lullUntil;
                else
                {
                    if (lullUntil > 0f && Time.time - lullUntil < 1f) PlaySiren();
                    int size = Mathf.RoundToInt(WaveSize(intensity) * (low ? 0.6f : 1f) * Range(0.7f, 1.3f));
                    for (int i = 0; i < size; i++) LaunchRocket(Range(0f, 2.8f));
                    // Временная волна: часть ракет этой волны придёт на дорогу впереди игрока (событие с метками).
                    if (Time.time >= nextRoadStrike && race != null && race.Phase == RacePhase.Running)
                    {
                        var director = RandomCar.Events.EventDirector.Current;
                        if (director != null && director.LaunchWave("rocketbarrage"))
                            nextRoadStrike = Time.time + Range(26f, 40f) * (intensity >= 4 ? 0.7f : intensity == 3 ? 0.85f : 1.15f);
                        else nextRoadStrike = Time.time + 4f;
                    }
                    var gap = WaveGap(intensity);
                    nextWave = Time.time + Range(gap.x, gap.y);
                }
            }
            UpdateRockets(dt);
            UpdateInterceptors(dt);
            UpdateFlashes(dt);
            UpdateLights(dt);
            UpdateBooms();
        }

        Rocket FreeRocket() => rockets.Find(r => !r.Flying && r.Linger <= 0f);
        Interceptor FreeInterceptor() => interceptors.Find(i => !i.Flying && i.Linger <= 0f);

        /// Сколько ракет пул держит в запасе для ударов по дороге (фоновые волны их не берут).
        const int StrikeReserve = 8;

        void LaunchRocket(float delay)
        {
            if (rockets.FindAll(x => !x.Flying && x.Linger <= 0f).Count <= StrikeReserve) return;
            var r = FreeRocket();
            if (r == null) return;
            Vector3 side = Vector3.Cross(Vector3.up, threatDirection).normalized;
            r.P0 = cityCenter + threatDirection * Range(2600f, 3600f) + side * Range(-1600f, 1600f) + Vector3.up * Range(140f, 420f);
            float roll = (float)random.NextDouble();
            r.InterceptAt = -1f;
            r.FadeAt = -1f;
            r.FarImpact = false;
            r.CityImpact = false;
            r.Strike = null;
            r.Scale = 1f;
            if (roll < 0.14f && CityTarget(out var city))
            {
                // Удар по городу у трассы, впереди по ходу игрока: видно с дороги, свет на домах; дорогу не задевает.
                r.P2 = city;
                r.CityImpact = true;
                r.ImpactScale = Range(1.1f, 2.4f);
                r.Scale = 0.45f;
            }
            else if (roll < 0.62f)
            {
                // Идёт к городу — её перехватят высоко в небе.
                r.P2 = cityCenter + side * Range(-900f, 900f) + threatDirection * Range(-500f, 700f) + Vector3.up * Range(160f, 380f);
                r.InterceptAt = Range(0.55f, 0.85f);
            }
            else if (roll < 0.82f)
            {
                // Скрывается за облаками.
                r.P2 = cityCenter + side * Range(-1200f, 1200f) + threatDirection * Range(300f, 1200f) + Vector3.up * Range(650f, 900f);
                r.FadeAt = Range(0.5f, 0.75f);
            }
            else
            {
                // Далёкая вспышка за горизонтом — далеко от трассы.
                r.P2 = cityCenter + threatDirection * Range(1300f, 2000f) + side * Range(-1400f, 1400f);
                r.FarImpact = true;
            }
            r.P1 = r.CityImpact
                ? r.P2 + (r.P0 - r.P2) * 0.22f + Vector3.up * Range(900f, 1200f)
                : (r.P0 + r.P2) * 0.5f + Vector3.up * Range(500f, 900f);
            r.Duration = Range(9f, 14f);
            r.Time = -delay;
            r.StartTime = Time.time + delay;
            r.Flying = true;
            r.T.gameObject.SetActive(false);
            if (r.InterceptAt > 0f) ScheduleInterceptor(r);
        }

        /// Точка удара по городу: 300–900 м впереди игрока по трассе, 70–260 м в сторону — среди домов.
        bool CityTarget(out Vector3 target)
        {
            target = default;
            if (race == null || race.Player == null || race.Course == null || race.Phase != RacePhase.Running) return false;
            var course = race.Course;
            float p = RandomCar.Track.TrackPath.Wrap(race.Player.Progress + Range(300f, 900f) / course.Length);
            float side = random.NextDouble() < 0.5 ? -1f : 1f;
            target = course.PointAt(p) + course.SideAt(p) * side * Range(70f, 260f);
            target.y = 0f;
            return true;
        }

        /// Ракета к метке удара на дороге: с горизонта по дуге, прибывает в ArriveAt. intercept — её собьёт ПВО.
        public Strike BeginStrike(Vector3 target, float arriveAt, bool intercept)
        {
            var r = FreeRocket();
            if (r == null) return null;
            var strike = new Strike { Target = target, ArriveAt = arriveAt, Intercept = intercept };
            Vector3 side = Vector3.Cross(Vector3.up, threatDirection).normalized;
            r.P0 = target + threatDirection * Range(2000f, 2800f) + side * Range(-700f, 700f) + Vector3.up * Range(220f, 420f);
            r.P2 = target;
            // Заход сверху: контрольная точка над целью — последний участок почти вертикальный, ракета не
            // проносится низко над дорогой у камеры.
            r.P1 = target + (r.P0 - target) * 0.22f + Vector3.up * Range(950f, 1200f);
            r.Scale = 0.4f;
            r.StartTime = Time.time;
            r.Time = 0f;
            r.Duration = Mathf.Max(3f, arriveAt - Time.time);
            r.InterceptAt = intercept ? Range(0.58f, 0.74f) : -1f;
            r.FadeAt = -1f;
            r.FarImpact = false;
            r.CityImpact = false;
            r.Strike = strike;
            r.Flying = true;
            r.T.gameObject.SetActive(false);
            if (intercept) ScheduleInterceptor(r, miss: false);
            return strike;
        }

        /// Взрыв на земле (удар по дороге или городу): огненный шар, свет на домах и асфальте, искры, столб дыма,
        /// звук с задержкой по расстоянию. scale 0.8 — небольшой, 2+ — крупный.
        public void GroundBlast(Vector3 pos, float scale)
        {
            var cam = Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, pos) : 500f;
            bool full = GameSettings.EffectsQuality > 0 && distance < 1600f;
            var color = Color.Lerp(new Color(1f, 0.62f, 0.25f), new Color(1f, 0.82f, 0.45f), (float)random.NextDouble());
            SpawnFlash(pos + Vector3.up * (4f + 3f * scale), 26f * scale, color, 0.7f + 0.2f * scale, light: false, far: false);
            if (GameSettings.EffectsQuality > 0) LightUp(pos + Vector3.up * 8f, color, 10f + 9f * scale);
            if (full)
            {
                Emit(sparks, pos + Vector3.up * 2f, Mathf.RoundToInt(16 * scale), 16f * scale, Range(1.2f, 2f), new Color(1f, 0.7f, 0.35f), Range(0.8f, 1.6f));
                // Столб дыма уходит вверх и не висит над дорогой стеной.
                int puffs = Mathf.RoundToInt(4 + 3 * scale);
                for (int k = 0; k < puffs; k++)
                    EmitOne(smoke, pos + Random.insideUnitSphere * 2f * scale + Vector3.up * 3f, new Vector3(Random.Range(-1f, 1f), Range(5f, 9f) * scale, Random.Range(-1f, 1f)),
                            Range(2.5f, 4f), Range(4f, 7f) * scale, new Color(0.17f, 0.16f, 0.18f, 0.55f));
            }
            float volume = Mathf.Clamp01(1.4f * scale / (1f + distance / 450f));
            pendingBooms.Add((Time.time + distance / 343f, volume));
        }

        void ScheduleInterceptor(Rocket r) => ScheduleInterceptor(r, miss: random.NextDouble() < 0.12);

        void ScheduleInterceptor(Rocket r, bool miss)
        {
            var i = FreeInterceptor();
            if (i == null || batteries.Length == 0)
            {
                r.InterceptAt = -1f;
                r.FadeAt = 0.7f;
                return;
            }
            Vector3 meet = Bezier(r.P0, r.P1, r.P2, r.InterceptAt);
            Vector3 from = batteries[random.Next(batteries.Length)] + new Vector3(Range(-30f, 30f), 0f, Range(-30f, 30f));
            i.Duration = Range(3.2f, 4.8f);
            i.P0 = from;
            i.P2 = meet;
            i.P1 = from + Vector3.up * (meet.y * 0.9f) + (meet - from) * 0.25f;
            i.Target = r;
            i.Miss = miss;
            // Старт так, чтобы встретиться с ракетой в точке перехвата.
            i.Time = (r.Time) - (r.InterceptAt * r.Duration - i.Duration);
            i.Flying = true;
            i.T.gameObject.SetActive(false);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t) => 2f * (1f - t) * (b - a) + 2f * t * (c - b);

        void UpdateRockets(float dt)
        {
            int count = 0;
            foreach (var r in rockets)
            {
                if (r.Linger > 0f)
                {
                    r.Linger -= dt;
                    if (r.Linger <= 0f) r.T.gameObject.SetActive(false);
                }
                if (!r.Flying) continue;
                r.Time += dt;
                if (r.Time < 0f) continue;
                if (!r.T.gameObject.activeSelf)
                {
                    r.T.position = r.P0;
                    r.T.localScale = Vector3.one * 3f * r.Scale;
                    r.T.gameObject.SetActive(true);
                    if (r.Trail != null)
                    {
                        r.Trail.widthMultiplier = r.Scale;
                        r.Trail.Clear();
                        r.Trail.emitting = true;
                    }
                    if (r.Flame != null) r.Flame.enabled = true;
                }
                count++;
                // Ракета к метке на дороге подстраивает скорость под уточнённый момент удара.
                if (r.Strike != null)
                {
                    r.Duration = Mathf.Max(0.5f, r.Strike.ArriveAt - r.StartTime);
                    r.Time = Time.time - r.StartTime;
                }
                float t = r.Time / r.Duration;
                if (r.InterceptAt > 0f && t >= r.InterceptAt)
                {
                    // Встреча с перехватчиком произойдёт в его обновлении; здесь — запасной случай (перехватчик пропал).
                    t = r.InterceptAt;
                }
                Vector3 pos = Bezier(r.P0, r.P1, r.P2, Mathf.Clamp01(t));
                Vector3 dir = BezierTangent(r.P0, r.P1, r.P2, Mathf.Clamp01(t));
                r.T.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward));
                if (r.FadeAt > 0f && t >= r.FadeAt)
                {
                    EndRocket(r, 1.5f);
                    continue;
                }
                if (t >= 1f)
                {
                    if (r.FarImpact) SpawnFlash(pos, Range(60f, 110f), new Color(1f, 0.55f, 0.25f), 1.3f, light: false, far: true);
                    if (r.CityImpact) GroundBlast(pos, r.ImpactScale);
                    if (r.Strike != null) r.Strike.Arrived = true;
                    EndRocket(r, 0.5f);
                }
            }
            ActiveRockets = count;
        }

        void EndRocket(Rocket r, float linger)
        {
            r.Flying = false;
            if (r.Trail != null) r.Trail.emitting = false;
            if (r.Flame != null) r.Flame.enabled = false;
            r.Linger = (r.Trail != null ? r.Trail.time : 1f) + linger;
        }

        void UpdateInterceptors(float dt)
        {
            int count = 0;
            foreach (var i in interceptors)
            {
                if (i.Linger > 0f)
                {
                    i.Linger -= dt;
                    if (i.Linger <= 0f) i.T.gameObject.SetActive(false);
                }
                if (!i.Flying) continue;
                i.Time += dt;
                if (i.Time < 0f) continue;
                if (!i.T.gameObject.activeSelf)
                {
                    i.T.position = i.P0;
                    i.T.gameObject.SetActive(true);
                    if (i.Trail != null)
                    {
                        i.Trail.Clear();
                        i.Trail.emitting = true;
                    }
                }
                count++;
                float t = Mathf.Clamp01(i.Time / i.Duration);
                Vector3 p2 = i.Miss ? i.P2 + Vector3.up * 350f : i.P2;
                Vector3 pos = Bezier(i.P0, i.P1, p2, t);
                Vector3 dir = BezierTangent(i.P0, i.P1, p2, t);
                i.T.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.up));
                if (t < 1f) continue;
                i.Flying = false;
                if (i.Trail != null) i.Trail.emitting = false;
                i.Linger = (i.Trail != null ? i.Trail.time : 1f) + 0.5f;
                if (i.Miss)
                {
                    // Промах: перехватчик самоликвидируется выше, ракета уходит в облака.
                    SpawnFlash(pos, Range(25f, 45f), new Color(0.85f, 0.9f, 1f), 0.6f, light: false, far: false);
                    if (i.Target != null && i.Target.Flying)
                    {
                        i.Target.InterceptAt = -1f;
                        i.Target.FadeAt = Mathf.Min(0.95f, i.Target.Time / i.Target.Duration + 0.12f);
                    }
                    continue;
                }
                if (i.Target != null && i.Target.Flying)
                {
                    if (i.Target.Strike != null) i.Target.Strike.Intercepted = true;
                    EndRocket(i.Target, 0.2f);
                    i.Target.T.gameObject.SetActive(false);
                }
                Intercept(pos);
            }
            ActiveInterceptors = count;
        }

        /// Перехват: яркая вспышка, свет на облаках и крышах, кольцо дыма, искры, глухой хлопок с задержкой звука.
        void Intercept(Vector3 pos)
        {
            float size = Range(220f, 420f);
            var cam = Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, pos) : 1000f;
            bool near = distance < 1800f && GameSettings.EffectsQuality > 0;
            var color = Color.Lerp(new Color(1f, 0.82f, 0.55f), new Color(1f, 0.6f, 0.35f), (float)random.NextDouble());
            SpawnFlash(pos, size, color, Range(0.9f, 1.4f), light: true, far: !near);
            if (near)
            {
                Emit(sparks, pos, 36, 40f, Range(2.5f, 4.5f), new Color(1f, 0.75f, 0.4f), Range(1.5f, 3f));
                // Кольцо дыма и облако в центре.
                int ring = 20;
                for (int k = 0; k < ring; k++)
                {
                    float a = k * Mathf.PI * 2f / ring;
                    var v = new Vector3(Mathf.Cos(a), Range(-0.15f, 0.15f), Mathf.Sin(a)) * Range(18f, 26f);
                    EmitOne(smoke, pos, v, Range(7f, 10f), Range(55f, 85f), new Color(0.55f, 0.52f, 0.5f, 0.5f));
                }
                for (int k = 0; k < 5; k++)
                    EmitOne(smoke, pos + Random.insideUnitSphere * 10f, Random.insideUnitSphere * 4f, Range(8f, 12f), Range(70f, 110f), new Color(0.45f, 0.43f, 0.42f, 0.6f));
            }
            float volume = Mathf.Clamp01(1.2f / (1f + distance / 700f));
            pendingBooms.Add((Time.time + distance / 343f, volume));
        }

        void SpawnFlash(Vector3 pos, float size, Color color, float life, bool light, bool far)
        {
            var f = flashes.Find(x => !x.Active);
            if (f == null) return;
            f.Active = true;
            f.Age = 0f;
            f.Life = life;
            f.Size = far ? size * 0.6f : size;
            f.Color = color;
            f.T.position = pos;
            f.R.gameObject.SetActive(true);
            if (light && GameSettings.EffectsQuality > 0) LightUp(pos, color, far ? 6f : Range(14f, 24f));
        }

        void LightUp(Vector3 pos, Color color, float peak)
        {
            int best = -1;
            float oldest = -1f;
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i].enabled) { best = i; break; }
                if (lightAge[i] > oldest) { oldest = lightAge[i]; best = i; }
            }
            if (best < 0) return;
            var l = lights[best];
            l.transform.position = pos;
            l.color = color;
            l.enabled = true;
            lightAge[best] = 0f;
            lightPeak[best] = peak;
        }

        void UpdateLights(float dt)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i].enabled) continue;
                lightAge[i] += dt;
                float k = Mathf.Exp(-lightAge[i] * 3.2f) * (1f - Mathf.Exp(-lightAge[i] * 30f));
                lights[i].intensity = lightPeak[i] * k;
                if (lightAge[i] > 1.6f) lights[i].enabled = false;
            }
        }

        void UpdateFlashes(float dt)
        {
            var cam = Camera.main;
            int count = 0;
            foreach (var f in flashes)
            {
                if (!f.Active) continue;
                f.Age += dt;
                float t = f.Age / f.Life;
                if (t >= 1f)
                {
                    f.Active = false;
                    f.R.gameObject.SetActive(false);
                    continue;
                }
                count++;
                // Резкая вспышка, затем мягкое затухание и разрастание.
                float intensity = Mathf.Pow(1f - t, 2.2f) * (t < 0.06f ? t / 0.06f : 1f);
                float scale = f.Size * (0.55f + 0.6f * Mathf.Sqrt(t));
                if (cam != null) f.T.rotation = Quaternion.LookRotation(f.T.position - cam.transform.position);
                f.T.localScale = Vector3.one * scale;
                var c = f.Color * (2.5f * intensity);
                c.a = intensity;
                f.R.GetPropertyBlock(block);
                block.SetColor(ColorId, c);
                f.R.SetPropertyBlock(block);
            }
            ActiveFlashes = count;
        }

        void UpdateBooms()
        {
            for (int i = pendingBooms.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingBooms[i].at) continue;
                if (booms != null && boomClip != null) booms.PlayOneShot(boomClip, pendingBooms[i].volume * 0.6f * GameSettings.SfxVolume);
                pendingBooms.RemoveAt(i);
            }
        }

        static void Emit(ParticleSystem ps, Vector3 pos, int count, float speed, float life, Color color, float size)
        {
            if (ps == null) return;
            for (int k = 0; k < count; k++)
                EmitOne(ps, pos, Random.insideUnitSphere.normalized * speed * Random.Range(0.4f, 1f), life * Random.Range(0.6f, 1f), size, color);
        }

        static void EmitOne(ParticleSystem ps, Vector3 pos, Vector3 velocity, float life, float size, Color color)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = velocity,
                startLifetime = life,
                startSize = size,
                startColor = color,
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }
    }
}
