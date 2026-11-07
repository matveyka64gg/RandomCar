using System.Collections.Generic;
using System.Linq;
using System.Text;
using RandomCar.Race;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    /// Проверка динамики гонки глазами игрока (а не «событие технически запустилось»): для каждого события —
    /// где оно возникло относительно игрока, видел ли он его, доехал ли, пока оно шло, пришлось ли объезжать;
    /// по гонке — сколько секунд подряд игрок ехал без событий и как растёт насыщенность от круга к кругу.
    /// Отладочный компонент: вешается на объект гонки на время проверки (RaceTelemetry — соседний инструмент).
    public sealed class EventDynamicsProbe : MonoBehaviour
    {
        /// Событие «рядом», если до его зоны впереди не больше стольких метров (или игрок внутри).
        public const float NearAhead = 300f;
        /// Дальше видимость не засчитывается: событие — точка на горизонте.
        public const float ViewDistance = 350f;

        public sealed class Record
        {
            public string Id;
            public int Lap;
            public float SpawnTime;
            /// Расстояние по трассе от игрока до центра зоны в момент запуска, м (минус — позади).
            public float SpawnAhead;
            public float SpawnSpeedKmh;
            public bool Seen;
            public bool Occluded;
            public bool Reached;
            public bool ReachedActive;
            public bool Expired;
            public bool CloseCall;
            public bool Inside;
            public float Progress;
            public float MinLateralGap = float.MaxValue;
            public RaceEvent Event;
            /// Событие поставлено впереди ИИ (каждое третье): доехал ли до него тот, ради кого оно.
            public bool ForAi;
            public string FocusName;
            public bool FocusReached;
            /// Хаос v5: трюк (мгновенное происшествие рядом с машиной), расстояние от «своей» машины при запуске, м.
            public bool Gag;
            public float TargetAhead;
            /// Расстояние по прямой от машины игрока до центра события при запуске, м.
            public float PlayerDistance;
        }

        RaceManager race;
        EventDirector director;
        Camera view;
        readonly List<Record> records = new List<Record>();
        readonly List<float> quietSpans = new List<float>();
        float quietFor;
        float watchedTime;
        float interestingTime;

        // Хаос v4: одновременность, комбинации, физика, мины, проезд, производительность.
        int maxSimultaneous;
        int maxBodies;
        int combos, scenes;
        readonly List<string> comboTitles = new List<string>();
        int ballCarHits, playerBodyHits, bodyBodyHits;
        int mineReal, mineFake;
        int blasts;
        float minPassage = float.MaxValue;
        float minPassageAt;
        string minPassageEvents = "";
        int frames;
        float frameTimeSum;
        float worstFrame;
        readonly List<float> fpsSamples = new List<float>();
        float fpsWindow;
        int fpsFrames;
        int launches;
        int playerLaunches;
        readonly Dictionary<int, HashSet<string>> typesPerLap = new Dictionary<int, HashSet<string>>();
        readonly Dictionary<string, int> recoveries = new Dictionary<string, int>();
        readonly Dictionary<string, float> standing = new Dictionary<string, float>();
        readonly Dictionary<string, float> standingAt = new Dictionary<string, float>();
        readonly Dictionary<string, int> effectsBy = new Dictionary<string, int>();
        readonly Dictionary<Racer, int> lastEffectCount = new Dictionary<Racer, int>();

        // Хаос v5: сила ударов, полёты, цепные реакции, память.
        int strongLaunches, hugeLaunches, billiards, hardLandings, shockCars;
        float maxLaunch;
        readonly Dictionary<string, float> maxHeight = new Dictionary<string, float>();
        readonly Dictionary<string, float> airTime = new Dictionary<string, float>();
        // Темп гонки: метры вдоль трассы на земле и в воздухе, время на земле медленнее 15 м/с.
        readonly Dictionary<Racer, float> lastProgress = new Dictionary<Racer, float>();
        readonly Dictionary<string, float> groundMeters = new Dictionary<string, float>();
        readonly Dictionary<string, float> airMeters = new Dictionary<string, float>();
        readonly Dictionary<string, float> groundTime = new Dictionary<string, float>();
        readonly Dictionary<string, float> slowTime = new Dictionary<string, float>();
        readonly Dictionary<string, float> slowCause = new Dictionary<string, float>();
        long memoryStart, memoryPeak, memoryEnd;
        int chainHitsStart, shocksStart;
        int maxBodiesLive;

        public IReadOnlyList<Record> Records => records;

        void Awake()
        {
            race = GetComponent<RaceManager>() ?? FindAnyObjectByType<RaceManager>();
            director = FindAnyObjectByType<EventDirector>();
            director.EventStarted += OnStarted;
            director.ComboStarted += OnCombo;
            race.RaceRestarted += Clear;
            race.Recovered += OnRecovered;
            ChaosBody.HitCar += OnBodyHitCar;
            ChaosBody.HitBody += OnBodyHitBody;
            MineField.Exploded += OnMine;
            ChaosFx.Blasted += OnBlast;
            Car.CarController.Launched += OnLaunched;
            Car.CarController.Billiard += OnBilliard;
            Car.CarController.HardLanding += OnHardLanding;
            ChaosFx.ShockHitCar += OnShockCar;
        }

        void OnLaunched(Car.CarController car, float strength, int generation)
        {
            if (race.Phase != RacePhase.Running) return;
            maxLaunch = Mathf.Max(maxLaunch, strength);
            if (strength >= 20f) strongLaunches++;
            if (strength >= 35f) hugeLaunches++;
        }

        void OnBilliard(Car.CarController from, Car.CarController to, float speed) => billiards++;

        void OnHardLanding(Car.CarController car, float strength, int generation) => hardLandings++;

        void OnShockCar(Car.CarController car, int generation) => shockCars++;

        void OnCombo(string title, int count)
        {
            if (race.Phase != RacePhase.Running) return;
            combos++;
            if (count >= 3) scenes++;
            comboTitles.Add($"{race.RaceTime:F0}с {title}");
        }

        void OnBodyHitCar(ChaosBody body, Car.CarController car, float strength)
        {
            ballCarHits++;
            if (Focus != null && car == Focus.Controller) playerBodyHits++;
        }

        void OnBodyHitBody(ChaosBody a, ChaosBody b, float strength) => bodyBodyHits++;

        void OnRecovered(Racer r)
        {
            recoveries.TryGetValue(r.Name, out int n);
            recoveries[r.Name] = n + 1;
        }

        void OnMine(Vector3 position, bool real)
        {
            if (real) mineReal++;
            else mineFake++;
        }

        void OnBlast(Vector3 position, BlastStyle style, float scale) => blasts++;

        void OnDestroy()
        {
            if (director != null)
            {
                director.EventStarted -= OnStarted;
                director.ComboStarted -= OnCombo;
            }
            if (race != null)
            {
                race.RaceRestarted -= Clear;
                race.Recovered -= OnRecovered;
            }
            ChaosBody.HitCar -= OnBodyHitCar;
            ChaosBody.HitBody -= OnBodyHitBody;
            MineField.Exploded -= OnMine;
            ChaosFx.Blasted -= OnBlast;
            Car.CarController.Launched -= OnLaunched;
            Car.CarController.Billiard -= OnBilliard;
            Car.CarController.HardLanding -= OnHardLanding;
            ChaosFx.ShockHitCar -= OnShockCar;
        }

        public void Clear()
        {
            records.Clear();
            quietSpans.Clear();
            quietFor = watchedTime = interestingTime = 0f;
            maxSimultaneous = maxBodies = combos = scenes = ballCarHits = playerBodyHits = bodyBodyHits = mineReal = mineFake = blasts = 0;
            comboTitles.Clear();
            minPassage = float.MaxValue;
            minPassageEvents = "";
            frames = 0;
            frameTimeSum = worstFrame = 0f;
            fpsSamples.Clear();
            fpsWindow = 0f;
            fpsFrames = 0;
            launches = playerLaunches = 0;
            launchesAtStart = race != null && race.Player != null ? race.Player.Controller.Launches : 0;
            typesPerLap.Clear();
            recoveries.Clear();
            standing.Clear();
            standingAt.Clear();
            effectsBy.Clear();
            lastEffectCount.Clear();
            strongLaunches = hugeLaunches = billiards = hardLandings = shockCars = maxBodiesLive = 0;
            maxLaunch = 0f;
            maxHeight.Clear();
            airTime.Clear();
            lastProgress.Clear();
            groundMeters.Clear();
            airMeters.Clear();
            groundTime.Clear();
            slowTime.Clear();
            slowCause.Clear();
            memoryStart = memoryPeak = memoryEnd = System.GC.GetTotalMemory(false);
            chainHitsStart = ChaosFx.ChainHits;
            shocksStart = ChaosFx.ShockCount;
            ChaosFx.ResetStats();
            chainHitsStart = shocksStart = 0;
        }

        Racer Focus => race.Player;

        float Ahead(float eventProgress)
        {
            float length = race.Course.Length;
            float d = TrackPath.Wrap(eventProgress - Focus.Progress) * length;
            return d > length * 0.5f ? d - length : d;
        }

        void OnStarted(RaceEvent ev)
        {
            if (Focus == null || race.Course == null) return;
            int lapNow = Mathf.Max(0, Focus.Laps.Laps) + 1;
            if (!typesPerLap.TryGetValue(lapNow, out var types)) typesPerLap[lapNow] = types = new HashSet<string>();
            types.Add(ev.Id);
            records.Add(new Record
            {
                Id = ev.Id,
                Lap = lapNow,
                SpawnTime = race.RaceTime,
                SpawnAhead = Ahead(ev.Progress),
                Progress = ev.Progress,
                SpawnSpeedKmh = Focus.Controller.SpeedKmh,
                Event = ev,
                ForAi = ev.Placement.FocusSlot != Focus.Slot,
                FocusName = race.BySlot(ev.Placement.FocusSlot)?.Name ?? "-",
                Gag = ev.Definition != null && ev.Definition.Gag,
                TargetAhead = TargetAhead(ev),
                PlayerDistance = Vector3.Distance(Focus.Car.position, race.Course.PointAt(ev.Progress)),
            });
        }

        /// Расстояние по трассе от машины, ради которой событие, до его центра, м.
        float TargetAhead(RaceEvent ev)
        {
            var target = race.BySlot(ev.Placement.FocusSlot) ?? Focus;
            float length = race.Course.Length;
            float d = TrackPath.Wrap(ev.Progress - target.Progress) * length;
            return d > length * 0.5f ? d - length : d;
        }

        void Update()
        {
            if (race.Phase != RacePhase.Running || Focus == null || Focus.Finished) return;
            if (view == null) view = Camera.main;
            float dt = Time.deltaTime;
            watchedTime += dt;
            // Производительность: кадр в среднем, худший кадр, FPS по секундам.
            float unscaled = Time.unscaledDeltaTime;
            frames++;
            frameTimeSum += unscaled;
            if (frames > 30) worstFrame = Mathf.Max(worstFrame, unscaled);
            fpsWindow += unscaled;
            fpsFrames++;
            if (fpsWindow >= 1f)
            {
                fpsSamples.Add(fpsFrames / fpsWindow);
                fpsWindow = 0f;
                fpsFrames = 0;
            }
            int simultaneous = 0;
            foreach (var e in director.ActiveEvents) if (e.Stage != EventStage.Ending) simultaneous++;
            maxSimultaneous = Mathf.Max(maxSimultaneous, simultaneous);
            maxBodies = Mathf.Max(maxBodies, ChaosBody.Active.Count);
            if ((frames & 63) == 0) memoryPeak = System.Math.Max(memoryPeak, System.GC.GetTotalMemory(false));
            // Полёты: высота над дорогой и время в воздухе у каждого участника.
            foreach (var r in race.Racers)
            {
                if (r.Finished) continue;
                float h = r.Car.position.y - race.Course.PointAt(r.Progress).y;
                maxHeight.TryGetValue(r.Name, out float best);
                if (h > best) maxHeight[r.Name] = h;
                bool air = !r.Controller.IsGrounded && h > 1.5f;
                if (air)
                {
                    airTime.TryGetValue(r.Name, out float a);
                    airTime[r.Name] = a + dt;
                }
                if (lastProgress.TryGetValue(r, out float before))
                {
                    float step = TrackPath.Wrap(r.Progress - before) * race.Course.Length;
                    if (step > race.Course.Length * 0.5f) step -= race.Course.Length;
                    var meters = air ? airMeters : groundMeters;
                    meters.TryGetValue(r.Name, out float m);
                    meters[r.Name] = m + step;
                    if (!air)
                    {
                        groundTime.TryGetValue(r.Name, out float g);
                        groundTime[r.Name] = g + dt;
                        if (r.Controller.Speed < 15f)
                        {
                            slowTime.TryGetValue(r.Name, out float s);
                            slowTime[r.Name] = s + dt;
                            // Почему медленно: первая подходящая причина.
                            var c = r.Controller;
                            float lat = Mathf.Abs(Vector3.Dot(r.Car.position - race.Course.PointAt(r.Progress), race.Course.SideAt(r.Progress)));
                            string cause = c.Physics.IsUpsideDown ? "на боку/крыше"
                                : c.IsSlowed ? "замедление"
                                : c.Physics.Effects.ControlActive ? "нелепый руль"
                                : c.GroundedFor < 2f ? "только приземлился"
                                : lat > race.Course.HalfWidth + 1f ? "вне дороги"
                                : c.IsSlipping ? "скользко"
                                : c.Physics.SpeedAbs > 15f ? "боком/юзом"
                                : "разгон/прочее";
                            slowCause.TryGetValue(cause, out float sc);
                            slowCause[cause] = sc + dt;
                        }
                    }
                }
                lastProgress[r] = r.Progress;
            }
            if (Focus.Controller.LastEffect != lastEffect || Focus.Controller.EffectsReceived != lastEffects)
            {
                playerLaunches = Focus.Controller.Launches - launchesAtStart;
                lastEffect = Focus.Controller.LastEffect;
                lastEffects = Focus.Controller.EffectsReceived;
            }
            MeasurePassage();
            // Сколько секунд каждый участник простоял (затор, застрял у препятствия).
            foreach (var r in race.Racers)
            {
                if (r.Finished || r.Retired || r.Controller.Locked || r.Controller.SpeedKmh > 3f) continue;
                standing.TryGetValue(r.Name, out float s);
                standing[r.Name] = s + dt;
                // Где стоял: у какого события (по трассе ±40 м), иначе «без события».
                string where = NearestEventId(r);
                standingAt.TryGetValue(where, out float w);
                standingAt[where] = w + dt;
            }
            // Чьи воздействия: событие рядом с машиной в момент эффекта.
            foreach (var r in race.Racers)
            {
                lastEffectCount.TryGetValue(r, out int before);
                int now = r.Controller.EffectsReceived;
                if (now == before) continue;
                lastEffectCount[r] = now;
                if (before == 0 && !effectsBy.ContainsKey("_start")) { effectsBy["_start"] = 0; }
                string key = NearestEventId(r) + ":" + r.Controller.LastEffect;
                effectsBy.TryGetValue(key, out int c);
                effectsBy[key] = c + (now - before);
            }
            bool interesting = false;
            Plane[] planes = view != null ? GeometryUtility.CalculateFrustumPlanes(view) : null;

            foreach (var r in records)
            {
                var ev = r.Event;
                if (ev == null || r.Expired) continue;
                // Экземпляры событий берутся из пула: тот же объект позже может играть другое событие.
                bool live = ev.IsRunning && Mathf.Approximately(ev.Progress, r.Progress) && ev.Stage != EventStage.Ending;
                float half = ev.ZoneLength * 0.5f;
                float ahead = Ahead(ev.Progress);
                bool inside = Mathf.Abs(ahead) <= half;

                if (live && r.ForAi && !r.FocusReached)
                {
                    var target = race.BySlot(ev.Placement.FocusSlot);
                    if (target != null)
                    {
                        float d = TrackPath.Wrap(ev.Progress - target.Progress) * race.Course.Length;
                        if (d > race.Course.Length * 0.5f) d -= race.Course.Length;
                        if (Mathf.Abs(d) <= half && ev.Stage == EventStage.Active) r.FocusReached = true;
                    }
                }
                if (live)
                {
                    if (ahead > -half && ahead < NearAhead) interesting = true;
                    if (!r.Seen && planes != null && ahead > -half && ahead < ViewDistance)
                    {
                        var bounds = new Bounds(ev.Center + Vector3.up * 1.5f, new Vector3(6f, 3f, 6f));
                        if (GeometryUtility.TestPlanesAABB(planes, bounds))
                        {
                            bool blocked = Physics.Linecast(view.transform.position, ev.Center + Vector3.up * 1.5f, out var hit, ~0, QueryTriggerInteraction.Ignore)
                                           && hit.collider.attachedRigidbody == null && hit.collider.GetComponentInParent<RaceEvent>() == null
                                           && (hit.point - ev.Center).sqrMagnitude > 25f;
                            if (blocked) r.Occluded = true;
                            else r.Seen = true;
                        }
                    }
                    if (inside && !r.Inside)
                    {
                        r.Reached = true;
                        if (ev.Stage == EventStage.Active) r.ReachedActive = true;
                    }
                    if (inside)
                    {
                        if (ev.Stage == EventStage.Active) r.ReachedActive = true;
                        Vector3 axis = race.Course.PointAt(ev.Progress);
                        Vector3 side = race.Course.SideAt(ev.Progress);
                        float carLateral = Vector3.Dot(Focus.Car.position - axis, side);
                        float eventLateral = Vector3.Dot(ev.Center - axis, side);
                        float gap = Mathf.Abs(carLateral - eventLateral) - ev.Radius;
                        r.MinLateralGap = Mathf.Min(r.MinLateralGap, gap);
                        if (!ev.Definition.IsGood && gap < 1.5f) r.CloseCall = true;
                    }
                    r.Inside = inside;
                }
                else
                {
                    // Событие закончилось: если игрок так и не доехал, а зона была впереди — оно прошло впустую.
                    if (!r.Reached && ahead > 0f) r.Expired = true;
                    r.Event = null;
                }
            }

            if (interesting)
            {
                interestingTime += dt;
                if (quietFor > 0f) quietSpans.Add(quietFor);
                quietFor = 0f;
            }
            else quietFor += dt;
        }

        string NearestEventId(Racer r)
        {
            float length = race.Course.Length;
            string best = "без события";
            float bestD = 40f;
            foreach (var e in director.ActiveEvents)
            {
                float d = TrackPath.Wrap(e.Progress - r.Progress) * length;
                if (d > length * 0.5f) d -= length;
                d = Mathf.Max(0f, Mathf.Abs(d) - e.ZoneLength * 0.5f);
                if (d < bestD)
                {
                    bestD = d;
                    best = e.Id;
                }
            }
            return best;
        }

        string lastEffect = "";
        int lastEffects;
        int launchesAtStart;

        /// Самый узкий проезд на трассе сейчас: по перекрытым участкам всех событий (RoadBlock) — в каждом сечении
        /// через 2 м находится самый широкий свободный промежуток между препятствиями и краями дороги.
        void MeasurePassage()
        {
            var list = director.Blocks;
            if (list.Count == 0 || race.Course == null) return;
            float length = race.Course.Length;
            float half = race.Course.HalfWidth;
            var cuts = new List<(float, float)>();
            foreach (var b in list)
            {
                for (float a = -b.HalfLength; a <= b.HalfLength + 0.01f; a += 2f)
                {
                    float p = b.Progress + a / length;
                    cuts.Clear();
                    foreach (var o in list)
                    {
                        float d = TrackPath.Wrap(o.Progress - p) * length;
                        if (d > length * 0.5f) d -= length;
                        if (Mathf.Abs(d) <= o.HalfLength) cuts.Add((Mathf.Max(-half, o.LateralMin), Mathf.Min(half, o.LateralMax)));
                    }
                    cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                    float edge = -half, best = 0f;
                    foreach (var (lo, hi) in cuts)
                    {
                        if (lo > edge) best = Mathf.Max(best, lo - edge);
                        edge = Mathf.Max(edge, hi);
                    }
                    best = Mathf.Max(best, half - edge);
                    if (best < minPassage)
                    {
                        minPassage = best;
                        minPassageAt = race.RaceTime;
                        var ids = new HashSet<string>();
                        foreach (var e in director.ActiveEvents) if (e.Stage == EventStage.Active) ids.Add(e.Id);
                        minPassageEvents = string.Join("+", ids);
                    }
                }
            }
        }

        /// Сводка хаоса v5: частота всех запусков (события + трюки), расстояние до машин, полёты, цепочки, память.
        void AppendChaosV5(StringBuilder sb)
        {
            var starts = records.Select(r => r.SpawnTime).OrderBy(t => t).ToList();
            var gaps = new List<float>();
            for (int i = 1; i < starts.Count; i++) gaps.Add(starts[i] - starts[i - 1]);
            gaps.Sort();
            float median = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0f;
            int gags = records.Count(r => r.Gag);
            sb.Append($"ХАОС v5: запусков {records.Count} (событий {records.Count - gags}, трюков {gags}), " +
                      $"между запусками средн {(gaps.Count > 0 ? gaps.Average() : 0f):F2} с, медиана {median:F2} с, макс {(gaps.Count > 0 ? gaps.Max() : 0f):F1} с\n");
            // Плотность по окнам 10 с: хаос не стихает от старта до финиша.
            if (starts.Count > 0)
            {
                float first = starts[0], last = starts[starts.Count - 1];
                var windows = new List<int>();
                for (float t = first; t + 10f <= last + 0.01f; t += 10f) windows.Add(starts.Count(s => s >= t && s < t + 10f));
                if (windows.Count > 0)
                    sb.Append($"  запусков за 10 с: мин {windows.Min()}, средн {windows.Average():F0}, макс {windows.Max()}; первый запуск на {first:F1} с гонки\n");
            }
            var near = records.Select(r => Mathf.Abs(r.TargetAhead)).OrderBy(d => d).ToList();
            if (near.Count > 0)
                sb.Append($"  расстояние от «своей» машины при запуске: медиана {near[near.Count / 2]:F0} м, ≤ 40 м {near.Count(d => d <= 40f) * 100 / near.Count}%, ≤ 80 м {near.Count(d => d <= 80f) * 100 / near.Count}%, позади {records.Count(r => r.TargetAhead < 0f)}\n");
            sb.Append($"  рядом с игроком (≤ 60 м по прямой при запуске): {records.Count(r => r.PlayerDistance <= 60f)} из {records.Count}\n");
            sb.Append($"  подбросов: сильных (≥ 20 м/с) {strongLaunches}, огромных (≥ 35 м/с) {hugeLaunches}, самый сильный {maxLaunch:F0} м/с; жёстких приземлений {hardLandings}\n");
            sb.Append($"  макс. высота полёта над точкой подброса: {string.Join(", ", race.Racers.OrderByDescending(r => r.Controller.MaxAirHeight).Select(r => $"{r.Name} {r.Controller.MaxAirHeight:F0} м"))}\n");
            sb.Append($"  время в воздухе: {string.Join(", ", airTime.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value:F0} с"))}\n");
            sb.Append($"  медленно на земле — причины: {string.Join(", ", slowCause.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value:F0} с"))}\n");
            sb.Append($"  темп (м вдоль трассы): {string.Join("; ", groundMeters.Keys.Select(k => $"{k}: земля {groundMeters[k]:F0} м за {(groundTime.TryGetValue(k, out var gt) ? gt : 0f):F0} с (медленнее 15 м/с {(slowTime.TryGetValue(k, out var st) ? st : 0f):F0} с), воздух {(airMeters.TryGetValue(k, out var am) ? am : 0f):F0} м"))}\n");
            sb.Append($"  цепные реакции: волна задела машину/подожгла предмет {ChaosFx.ChainHits} раз (машин {shockCars}), самое длинное звено {ChaosFx.MaxGeneration}, " +
                      $"«бильярд» машина-машина {billiards}, событий от приземления {director.ChainEventCount}, ударных волн {ChaosFx.ShockCount}\n");
            var pu = PowerUps.PowerUpDirector.Current;
            if (pu != null)
                sb.Append($"  БОНУСЫ: ящиков {pu.BoxCount}, подборов {pu.Pickups}, применений {pu.Uses} ({string.Join(", ", pu.UsesByKind.Select(p => $"{p.Key} {p.Value}"))}); " +
                          $"попаданий: {string.Join(", ", pu.HitsByKind.Select(p => $"{p.Key} {p.Value}"))}; пуль выпущено {pu.BulletsFired}; " +
                          $"защита поглотила: {string.Join(", ", race.Racers.Select(r => $"{r.Name} {r.Controller.Absorbed}"))}\n");
            sb.Append($"  память (управляемая): старт {memoryStart / 1048576f:F0} МБ, пик {memoryPeak / 1048576f:F0} МБ, конец {memoryEnd / 1048576f:F0} МБ; тел на трассе сейчас {ChaosBody.Active.Count}, событий идёт {director.ActiveEvents.Count}\n");
        }

        public string Report()
        {
            var sb = new StringBuilder();
            var spans = new List<float>(quietSpans);
            if (quietFor > 0f) spans.Add(quietFor);
            int n = records.Count;
            sb.Append($"время наблюдения {watchedTime:F0} с, рядом с событиями {interestingTime:F0} с ({(watchedTime > 0 ? interestingTime / watchedTime : 0f):P0})\n");
            sb.Append($"пустая езда: макс {(spans.Count > 0 ? spans.Max() : 0f):F1} с, средн {(spans.Count > 0 ? spans.Average() : 0f):F1} с, отрезков >8 с: {spans.Count(s => s > 8f)}\n");
            memoryEnd = System.GC.GetTotalMemory(false);
            AppendChaosV5(sb);
            // Доехал ли и не кончилось ли раньше — только для обычных событий (трюки мгновенные).
            var mine = records.Where(r => !r.ForAi && !r.Gag).ToList();
            var forAi = records.Where(r => r.ForAi && !r.Gag).ToList();
            sb.Append($"для игрока {mine.Count}: доехал во время действия {mine.Count(r => r.ReachedActive)}, кончилось до приезда {mine.Count(r => r.Expired)}; " +
                      $"для ИИ {forAi.Count}: ИИ доехал во время действия {forAi.Count(r => r.FocusReached)} ({string.Join(", ", forAi.GroupBy(r => r.FocusName).Select(g => g.Key + " " + g.Count()))}); игрок проехал и их: {forAi.Count(r => r.ReachedActive)}\n");
            var effects = race.Racers.Select(r => $"{r.Name} {r.Controller.EffectsReceived}");
            sb.Append($"воздействий событий на машины: {string.Join(", ", effects)}\n");
            sb.Append($"возвратов на трассу: {(recoveries.Count == 0 ? "0" : string.Join(", ", recoveries.Select(p => $"{p.Key} {p.Value}")))}; " +
                      $"простой (< 3 км/ч): {(standing.Count == 0 ? "0" : string.Join(", ", standing.Select(p => $"{p.Key} {p.Value:F1} с")))}\n");
            sb.Append($"простой у событий: {string.Join(", ", standingAt.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value:F1} с"))}\n");
            sb.Append($"воздействия по событиям: {string.Join(", ", effectsBy.Where(p => p.Key != "_start").OrderByDescending(p => p.Value).Take(14).Select(p => $"{p.Key} {p.Value}"))}\n");
            sb.Append($"событий {n}: позади при запуске {records.Count(r => r.SpawnAhead < 0f)}, дальше 400 м {records.Count(r => r.SpawnAhead > 400f)}, " +
                      $"увидел {records.Count(r => r.Seen)}, заслонено {records.Count(r => r.Occluded && !r.Seen)}, доехал {records.Count(r => r.Reached)}, " +
                      $"доехал во время действия {records.Count(r => r.ReachedActive)}, кончилось до приезда {records.Count(r => r.Expired)}, пришлось объезжать {records.Count(r => r.CloseCall)}\n");
            foreach (var lap in records.GroupBy(r => r.Lap).OrderBy(g => g.Key))
            {
                var starts = lap.Select(r => r.SpawnTime).OrderBy(t => t).ToList();
                var gaps = new List<float>();
                for (int i = 1; i < starts.Count; i++) gaps.Add(starts[i] - starts[i - 1]);
                typesPerLap.TryGetValue(lap.Key, out var types);
                sb.Append($"  круг {lap.Key}: событий {lap.Count()}, видов {(types != null ? types.Count : 0)}, между началами средн {(gaps.Count > 0 ? gaps.Average() : 0f):F1} с / макс {(gaps.Count > 0 ? gaps.Max() : 0f):F1} с, " +
                          $"доехал {lap.Count(r => r.Reached)}, увидел {lap.Count(r => r.Seen)}, объезд {lap.Count(r => r.CloseCall)}\n");
            }
            sb.Append($"одновременно событий: макс {maxSimultaneous}; физических тел: макс {maxBodies}; комбинаций {combos} (сцен {scenes})\n");
            foreach (var t in comboTitles) sb.Append($"    комбо {t}\n");
            sb.Append($"удары тел о машины {ballCarHits} (о машину игрока {playerBodyHits}), тел друг о друга {bodyBodyHits}; мины: взрывов {mineReal}, ложных {mineFake}; взрывов всего {blasts}; подбросов игрока {playerLaunches}\n");
            sb.Append($"самый узкий проезд: {(minPassage == float.MaxValue ? "перекрытий не было" : $"{minPassage:F1} м на {minPassageAt:F0} с ({minPassageEvents})")}\n");
            if (frames > 0)
                sb.Append($"кадр: средний FPS {frames / Mathf.Max(0.001f, frameTimeSum):F0}, худший кадр {worstFrame * 1000f:F0} мс, худшая секунда {(fpsSamples.Count > 0 ? fpsSamples.Min() : 0f):F0} FPS\n");
            // Подробно — первые 80 событий (в «безумии» их сотни).
            foreach (var r in records.Take(80))
                sb.Append($"  {r.SpawnTime,6:F1} с  {(r.ForAi ? "ИИ:" + r.FocusName : "игрок"),-10} {r.Id,-9} впереди {r.SpawnAhead,6:F0} м  {r.SpawnSpeedKmh,4:F0} км/ч  " +
                          $"{(r.Seen ? "видел" : r.Occluded ? "заслон" : "не видел")} {(r.ReachedActive ? "проехал-в-действии" : r.Reached ? "доехал" : r.Expired ? "впустую" : "—")}{(r.CloseCall ? " объезд" : "")}\n");
            return sb.ToString();
        }
    }
}
