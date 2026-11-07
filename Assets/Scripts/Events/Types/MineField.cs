using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Race;
using UnityEngine;

namespace RandomCar.Events
{
    /// Вид мины: настоящая, ложная («пшик» с конфетти), прыгающая (отскакивает в сторону при подъезде),
    /// мигающая (то исчезает, то появляется), с задержкой (долгий фитиль).
    public enum MineType { Real, Fake, Hopper, Ghost, Delayed }

    /// Мины события (минное поле, развилка): появление из асфальта, мигание и писк, срабатывание от машины
    /// или физического объекта, фитиль, взрыв с подбросом и заносом, цепная реакция от соседних взрывов.
    /// Срабатывание решает компьютер, который считает физику задевшей машины (сигнал уходит остальным).
    public sealed class MineField : IChaosReactive
    {
        public const int SignalTrigger = 1;
        /// Мины крупные — их видно издалека и можно объехать.
        const float Scale = 1.5f;
        const float TriggerRadius = 1.75f;
        const float BlastRadius = 3.6f;
        const float ChainRadius = 6.8f;

        /// Радиус подброса взрывом: в «безумии» мина задевает и соседние машины.
        static float Blast => ChaosPower.Current.Wild ? 5.6f : BlastRadius;

        sealed class Mine
        {
            public Transform Root;
            public Renderer Lamp;
            public MineType Type;
            public Vector3 Home;
            public Vector3 Right;
            public float Progress;
            public float Lateral;
            public bool Armed;
            public bool Lit;
            public bool Done;
            public float FuseAt = float.MaxValue;
            public bool Hopped;
            public float HopAt = -10f;
            public Vector3 HopFrom;
            public float HopSide;
            public float GhostPhase;
            /// Звено цепной реакции: 0 — задела машина, 1+ — подожгла ударная волна.
            public int Generation;
        }

        readonly List<Mine> mines = new List<Mine>();
        readonly RaceEvent owner;
        readonly System.Func<int, int, Vector3, bool> signal;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        float beepAt;

        /// Сколько мин взорвалось (проверка).
        public static int Explosions { get; private set; }

        /// Мина взорвалась: позиция и была ли она настоящей (для проверки и ленты).
        public static event System.Action<Vector3, bool> Exploded;

        public int Count => mines.Count;

        /// owner — событие (сигналы и машины), signal — отправка сигнала (code, index, value).
        public MineField(RaceEvent ownerEvent, System.Func<int, int, Vector3, bool> sendSignal)
        {
            owner = ownerEvent;
            signal = sendSignal;
        }

        public void Bind(Transform root)
        {
            var lamp = root.Find("Lamp");
            mines.Add(new Mine { Root = root, Lamp = lamp != null ? lamp.GetComponent<Renderer>() : null });
            root.gameObject.SetActive(false);
        }

        public void Clear()
        {
            foreach (var m in mines) m.Root.gameObject.SetActive(false);
            ChaosFx.Unregister(this);
        }

        /// Поставить мину i: место, направление «вбок», прогресс трассы, вид. Вылезает из асфальта на телеграфе.
        public void Place(int i, Vector3 position, Vector3 right, Vector3 forward, float progress, float lateral, MineType type, float ghostPhase)
        {
            var m = mines[i];
            m.Home = position;
            m.Right = right;
            m.Progress = progress;
            m.Lateral = lateral;
            m.Type = type;
            m.Armed = false;
            m.Lit = true;
            m.Done = false;
            m.Hopped = false;
            m.FuseAt = float.MaxValue;
            m.GhostPhase = ghostPhase;
            m.HopSide = ghostPhase > 0.5f ? 1f : -1f;
            m.Root.gameObject.SetActive(true);
            m.Root.SetPositionAndRotation(position - Vector3.up * 0.8f, Quaternion.LookRotation(forward));
            m.Root.localScale = Vector3.one * Scale;
            ChaosFx.Register(this);
        }

        public void Hide(int i) => mines[i].Root.gameObject.SetActive(false);

        /// Появление: мины выпрыгивают из асфальта с перелётом (progress 0..1 телеграфа).
        public void Rise(float progress, int used)
        {
            for (int i = 0; i < used; i++)
            {
                var m = mines[i];
                float k = Mathf.Clamp01((progress - 0.1f * (i % 4)) / 0.5f);
                m.Root.position = m.Home - Vector3.up * 0.8f * (1f - EaseOutBack(k));
                SetLamp(m, Mathf.Repeat(Time.time * 2f + i * 0.3f, 1f) < 0.5f ? 0.6f : 0.15f);
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public void Arm(int used)
        {
            for (int i = 0; i < used; i++)
            {
                var m = mines[i];
                m.Root.position = m.Home;
                // «С задержкой» взводится через 1.5 с — до этого по ней можно проехать.
                m.Armed = m.Type != MineType.Delayed;
                if (m.Type == MineType.Delayed) m.FuseAt = float.MaxValue;
            }
        }

        /// Кадр: мигание и писк (чаще, когда машина рядом), прыжки, «призраки», срабатывание и фитили.
        public void Update(float activeTime, int used, IReadOnlyList<Racer> racers, bool authority)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < used; i++)
            {
                var m = mines[i];
                if (m.Done) continue;
                if (m.Type == MineType.Delayed && !m.Armed && activeTime > 1.5f) m.Armed = true;

                // «Призрак»: полсекунды из каждых двух его нет — проскочить можно, если угадать.
                if (m.Type == MineType.Ghost)
                {
                    bool visible = Mathf.Repeat(activeTime * 0.5f + m.GhostPhase, 1f) > 0.28f;
                    m.Root.localScale = Vector3.one * (visible ? Scale : Mathf.Max(0.01f, m.Root.localScale.x - Time.deltaTime * 6f * Scale));
                    m.Lit = visible;
                }

                // Прыгающая: при подъезде машины отпрыгивает на 2 м в сторону — мина «уворачивается» прямо в путь.
                if (m.Type == MineType.Hopper && m.HopAt > 0f)
                {
                    float t = (Time.time - m.HopAt) / 0.6f;
                    if (t < 1f)
                    {
                        Vector3 target = m.HopFrom + m.Right * m.HopSide * 2f;
                        m.Root.position = Vector3.Lerp(m.HopFrom, target, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.8f;
                        m.Root.rotation = Quaternion.AngleAxis(t * 360f, m.Right) * Quaternion.LookRotation(Vector3.Cross(m.Right, Vector3.up));
                    }
                    else m.Home = m.Root.position = m.HopFrom + m.Right * m.HopSide * 2f;
                }

                foreach (var r in racers)
                {
                    Vector3 d = r.Car.position - m.Root.position;
                    d.y = 0f;
                    float dist = d.magnitude;
                    nearest = Mathf.Min(nearest, dist);
                    if (m.Type == MineType.Hopper && !m.Hopped && dist < 42f && m.Armed)
                    {
                        m.Hopped = true;
                        m.HopAt = Time.time;
                        m.HopFrom = m.Root.position;
                        ChaosFx.Instance?.Sound(SoundBank.Boing, m.Root.position, 0.5f, 1.4f);
                    }
                    // Срабатывание решает тот, кто считает физику машины.
                    if (m.Armed && m.Lit && m.FuseAt == float.MaxValue && dist < TriggerRadius && r.Controller.Physics.Simulated)
                        signal(SignalTrigger, i, m.Root.position);
                }
                // Шар или бочка, прокатившиеся по мине, тоже её взрывают (физику объектов считает хост).
                if (authority && m.Armed && m.Lit && m.FuseAt == float.MaxValue)
                {
                    foreach (var body in ChaosBody.Active)
                    {
                        if (body == null || !body.Live) continue;
                        Vector3 d = body.transform.position - m.Root.position;
                        d.y = 0f;
                        if (d.magnitude < body.Radius + 0.6f)
                        {
                            signal(SignalTrigger, i, m.Root.position);
                            break;
                        }
                    }
                }

                // Мигание: обычное спокойное, на фитиле — часто.
                bool fusing = m.FuseAt != float.MaxValue;
                float rate = fusing ? 14f : m.Armed ? 2.5f : 1f;
                SetLamp(m, !m.Lit ? 0f : Mathf.Repeat(Time.time * rate + i * 0.37f, 1f) < 0.5f ? 1f : 0.12f);
                if (fusing && Time.time >= m.FuseAt) Explode(i, racers);
            }
            // Писк ближе — чаще: слышно, что впереди мины.
            if (nearest < 60f && Time.time >= beepAt && ChaosFx.Instance != null)
            {
                beepAt = Time.time + Mathf.Lerp(0.12f, 0.6f, nearest / 60f);
                ChaosFx.Instance.Sound(SoundBank.MineBeep, owner.transform.position + Vector3.up, 0.25f, 1f);
            }
        }

        /// Сигнал «мина i задета»: запускает фитиль (повторный сигнал безвреден).
        public void Trigger(int i)
        {
            if (i < 0 || i >= mines.Count) return;
            var m = mines[i];
            if (m.Done || m.FuseAt != float.MaxValue) return;
            m.FuseAt = Time.time + (m.Type == MineType.Delayed ? 0.85f : m.Type == MineType.Fake ? 0.12f : 0.22f);
            m.Generation = 0;
            ChaosFx.Instance?.Sound(SoundBank.MineBeep, m.Root.position, 0.8f, 1.5f);
        }

        void Explode(int i, IReadOnlyList<Racer> racers)
        {
            var m = mines[i];
            m.Done = true;
            m.FuseAt = float.MaxValue;
            Vector3 p = m.Root.position;
            m.Root.gameObject.SetActive(false);
            Explosions++;
            var fx = ChaosFx.Instance;
            bool real = m.Type != MineType.Fake;
            Exploded?.Invoke(p, real);
            if (!real)
            {
                // Ложная: цветной «пшик», конфетти и обиженное «пфф». Чуть притормаживает, и только.
                var tint = Color.HSVToRGB(Mathf.Repeat(m.GhostPhase * 3.7f, 1f), 0.65f, 1f);
                tint.a = 0.85f;
                fx?.Blast(p, BlastStyle.Fake, 0.55f, tint);
                fx?.Sound(SoundBank.Pshik, p, 0.8f, Random.Range(0.9f, 1.15f));
                fx?.Pop(p + Vector3.up * 2f, "ПШИК!", new Color(0.6f, 1f, 0.5f), 1f);
                foreach (var r in racers)
                    if (r.Controller.Physics.Simulated && Flat(r.Car.position - p) < BlastRadius) r.Controller.ApplySlow(0.85f, 0.6f);
                return;
            }
            bool wild = ChaosPower.Current.Wild;
            fx?.Blast(p, wild ? BlastStyle.Big : BlastStyle.Cartoon, wild ? 1.6f : 1.1f);
            fx?.Flash(p + Vector3.up * 1.5f, new Color(1f, 0.6f, 0.25f), wild ? 9f : 5f, 0.25f);
            fx?.Sound(SoundBank.BigBoom, p, 0.95f, Random.Range(0.9f, 1.1f));
            fx?.Pop(p + Vector3.up * 2.5f, m.Generation > 0 ? "ЦЕПОЧКА!" : wild ? "КА-БУМ!" : "БУМ!", new Color(1f, 0.55f, 0.15f), wild ? 1.6f : 1.2f);
            // Подброс высоко вверх с кувырком (мультяшная мина): от центра взрыва, ближе — сильнее.
            float radius = Blast;
            foreach (var r in racers)
            {
                Vector3 d = r.Car.position - p;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > radius || !r.Controller.Physics.Simulated) continue;
                Vector3 away = dist > 0.2f ? d / dist : m.Right;
                float k = 1f - dist / radius * 0.45f;
                var car = r.Car;
                // Кувырок: «бочка» вокруг продольной оси от бокового удара плюс сальто и немного волчка.
                float side = Vector3.Dot(away, car.right) >= 0f ? 1f : -1f;
                Vector3 spin = (car.forward * -side * Random.Range(5f, 8f) + car.right * Random.Range(-5f, 5f) + Vector3.up * Random.Range(-3f, 3f)) * k;
                r.Controller.ApplyLaunch(away * 9f * k + car.forward * 3f + Vector3.up * 24f * k, spin, "подброс миной", m.Generation);
                r.Controller.ApplySlip(0.8f);
            }
            // Ударная волна: соседние мины детонируют, шары и бочки отлетают, машины рядом отбрасывает.
            fx?.Shockwave(p, wild ? ChainRadius * 1.4f : ChainRadius, wild ? 14f : 9f, m.Generation + 1);
            owner.ShakeAt(p, wild ? 0.6f : 0.35f, wild ? 45f : 30f);
        }

        static float Flat(Vector3 d)
        {
            d.y = 0f;
            return d.magnitude;
        }

        /// Ударная волна от соседнего взрыва: мины в радиусе цепочки поджигаются с короткой задержкой.
        public void OnShock(Vector3 position, float radius, float strength, int generation)
        {
            for (int i = 0; i < mines.Count; i++)
            {
                var m = mines[i];
                if (m.Done || !m.Root.gameObject.activeInHierarchy || m.FuseAt != float.MaxValue) continue;
                if (Flat(m.Root.position - position) > Mathf.Min(radius, ChainRadius) || Flat(m.Root.position - position) < 0.1f) continue;
                m.FuseAt = Time.time + 0.28f;
                m.Generation = generation;
                ChaosFx.NoteChain(generation);
            }
        }

        public void SetLamp(int i, float k) => SetLamp(mines[i], k);

        void SetLamp(Mine m, float k)
        {
            if (m.Lamp == null) return;
            m.Lamp.GetPropertyBlock(block);
            block.SetColor(BaseColor, Color.Lerp(new Color(0.35f, 0.05f, 0.05f), new Color(1f, 0.2f, 0.15f), k));
            block.SetColor(EmissionColor, new Color(1f, 0.15f, 0.1f) * (k * 6f));
            m.Lamp.SetPropertyBlock(block);
        }

        /// Опасности для ИИ: каждая живая мина.
        public void CollectHazards(List<HazardInfo> list, int used, float since)
        {
            for (int i = 0; i < used; i++)
            {
                var m = mines[i];
                if (m.Done || !m.Root.gameObject.activeInHierarchy) continue;
                // Радиус срабатывания уже включает полуширину машины: ИИ объезжает с запасом ~1 м сверх него.
                list.Add(new HazardInfo { Progress = m.Progress, Center = m.Root.position, Radius = 0.9f, HalfLength = TriggerRadius, Good = false, Since = since });
            }
        }

        public void CollectBlocks(List<RoadBlock> list, int used)
        {
            for (int i = 0; i < used; i++)
            {
                var m = mines[i];
                if (m.Done || !m.Root.gameObject.activeInHierarchy) continue;
                list.Add(new RoadBlock { Progress = m.Progress, HalfLength = TriggerRadius, LateralMin = m.Lateral - TriggerRadius, LateralMax = m.Lateral + TriggerRadius });
            }
        }

        public int Remaining(int used)
        {
            int n = 0;
            for (int i = 0; i < used; i++) if (!mines[i].Done) n++;
            return n;
        }

        /// Ряды мин с гарантированным проездом: в каждом ряду открыт промежуток не уже gap, мины не ближе 3.6 м.
        /// Возвращает (смещение вдоль, поперёк) для каждой мины.
        public static List<Vector2> Layout(float roadHalf, int rows, float rowStep, int pattern, System.Func<float, float, float> range, int maxMines)
        {
            var result = new List<Vector2>();
            float gap = 5.4f;
            float usable = roadHalf * 2f - 1.2f;
            int perRow = Mathf.Clamp(Mathf.FloorToInt((usable - gap) / 3.6f), 1, 4);
            float gapCenter = range(-roadHalf + gap * 0.5f + 0.6f, roadHalf - gap * 0.5f - 0.6f);
            for (int row = 0; row < rows && result.Count < maxMines; row++)
            {
                float along = (row - (rows - 1) * 0.5f) * rowStep;
                // Узор: 0 — проезд прыгает с ряда на ряд (слалом), 1 — плавно смещается (диагональ),
                // 2 — «клин»: одна половина дороги усеяна, другая открыта.
                if (pattern == 0) gapCenter = range(-roadHalf + gap * 0.5f + 0.6f, roadHalf - gap * 0.5f - 0.6f);
                else if (pattern == 1) gapCenter = Mathf.Clamp(gapCenter + (row % 2 == 0 ? 1f : -1f) * roadHalf * 0.35f, -roadHalf + gap * 0.5f + 0.6f, roadHalf - gap * 0.5f - 0.6f);
                float lo = gapCenter - gap * 0.5f, hi = gapCenter + gap * 0.5f;
                // Свободная ширина слева и справа от проезда, мины — равномерно.
                float leftSpan = lo - (-roadHalf + 0.6f), rightSpan = (roadHalf - 0.6f) - hi;
                int left = leftSpan >= 1.2f ? Mathf.Clamp(Mathf.RoundToInt(perRow * leftSpan / (leftSpan + rightSpan)), 0, perRow) : 0;
                int right = rightSpan >= 1.2f ? perRow - left : 0;
                if (pattern == 2 && row % 2 == 1) right = 0;
                for (int k = 0; k < left && result.Count < maxMines; k++)
                    result.Add(new Vector2(along + range(-1.2f, 1.2f), lo - 1.8f - k * Mathf.Max(3.6f, (leftSpan - 1.8f) / Mathf.Max(1, left))));
                for (int k = 0; k < right && result.Count < maxMines; k++)
                    result.Add(new Vector2(along + range(-1.2f, 1.2f), hi + 1.8f + k * Mathf.Max(3.6f, (rightSpan - 1.8f) / Mathf.Max(1, right))));
            }
            // Мины не выходят за дорогу.
            for (int i = 0; i < result.Count; i++)
                result[i] = new Vector2(result[i].x, Mathf.Clamp(result[i].y, -roadHalf + 0.6f, roadHalf - 0.6f));
            return result;
        }
    }
}
