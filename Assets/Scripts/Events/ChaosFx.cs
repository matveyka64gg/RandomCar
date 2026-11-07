using System;
using System.Collections;
using System.Collections.Generic;
using RandomCar.Core;
using UnityEngine;

namespace RandomCar.Events
{
    /// Вид мультяшного взрыва: разные размеры, цвета, слои частиц и звуки — не одна анимация на всё.
    public enum BlastStyle
    {
        /// Мина: вспышка, огненный шар, кольцо по земле, дым, искры.
        Cartoon,
        /// Бочка, большой шар: всё сразу, обломки и звёздочки.
        Big,
        /// Звено цепочки: маленький хлопок.
        Mini,
        /// Ложная мина: конфетти и цветной «пшик».
        Fake,
        /// Цветной дым.
        ColorPuff,
        /// Искры и звёздочки от удара (молот, перчатка, шар о машину).
        Spark,
        /// Ледяные осколки.
        Ice,
    }

    /// Предмет, который реагирует на ударную волну: мина детонирует, взрывоопасная бочка взрывается.
    public interface IChaosReactive
    {
        /// generation — звено цепной реакции, которым станет реакция (взрыв от волны первого взрыва — 1, …).
        void OnShock(Vector3 position, float radius, float strength, int generation);
    }

    /// Общие эффекты хаоса: пул многослойных взрывов, фейерверки, вспышки света, звуки в мире, мультяшные надписи
    /// («БУМ!», «БОЙНЬ!») и ударная волна, которая отбрасывает лёгкие объекты и запускает цепные реакции.
    /// Объекты пула заранее лежат в сцене (собирает GameSceneBuilder); здесь они только запускаются.
    /// Дальние эффекты упрощаются: вдали — без искр, обломков и звёздочек, совсем далеко — не играются.
    public sealed class ChaosFx : MonoBehaviour
    {
        [Serializable]
        public sealed class BlastRig
        {
            public Transform Root;
            public ParticleSystem Flash, Fireball, Smoke, Debris, Sparks, Ring, Confetti, Stars;
            [NonSerialized] public float StartedAt = -100f;
        }

        [Serializable]
        public sealed class FireworkRig
        {
            public Transform Root;
            public ParticleSystem Trail, Burst, Glitter;
            [NonSerialized] public bool Busy;
        }

        const float SimpleDistance = 150f;
        const float CullDistance = 330f;
        const int Voices = 20;

        /// Дальше этого звена цепная реакция только видна: волна не поджигает и не отбрасывает (нет бесконечных циклов).
        public const int MaxChain = 8;

        /// Не больше стольких ударных волн в секунду (защита от лавины взрывов).
        const int MaxShocksPerSecond = 40;

        [SerializeField] List<BlastRig> blasts = new List<BlastRig>();
        [SerializeField] List<FireworkRig> fireworks = new List<FireworkRig>();
        [SerializeField] List<Light> lights = new List<Light>();
        /// Следы на дороге (v5): копоть от взрывов и трещины от ударов, тают через несколько секунд.
        [SerializeField] List<Renderer> scorches = new List<Renderer>();
        [SerializeField] Material crackMaterial;
        [SerializeField] Material scorchMaterial;
        float[] scorchAt;
        int nextScorch;
        MaterialPropertyBlock scorchBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const float ScorchSeconds = 14f;

        readonly List<IChaosReactive> reactive = new List<IChaosReactive>();
        AudioSource[] voices;
        float[] lightUntil;
        float[] lightPower;
        int nextBlast, nextLight;

        public static ChaosFx Instance { get; private set; }

        /// Мультяшная надпись в мире: позиция, текст, цвет, размер (интерфейс рисует её над местом события).
        public static event Action<Vector3, string, Color, float> Popped;

        /// Взрыв сыгран (для проверки динамики и ленты событий): позиция и масштаб.
        public static event Action<Vector3, BlastStyle, float> Blasted;

        /// Сколько взрывов, фейерверков и ударных волн было (проверка).
        public static int BlastCount { get; private set; }
        public static int FireworkCount { get; private set; }

        /// Цепные реакции (проверка): сколько раз волна задела машину или подожгла предмет, самое длинное звено.
        public static int ChainHits { get; private set; }
        public static int MaxGeneration { get; private set; }
        public static int ShockCount { get; private set; }

        /// Ударная волна отбросила машину (машина, звено цепочки).
        public static event Action<Car.CarController, int> ShockHitCar;

        /// Волна подожгла мину или бочку (звено generation).
        public static void NoteChain(int generation)
        {
            ChainHits++;
            MaxGeneration = Mathf.Max(MaxGeneration, generation);
        }

        public static void ResetStats()
        {
            ChainHits = 0;
            MaxGeneration = 0;
            ShockCount = 0;
        }

        float shockWindowAt;
        int shocksInWindow;

        public void Configure(List<BlastRig> blastRigs, List<FireworkRig> fireworkRigs, List<Light> flashLights,
                              List<Renderer> scorchDecals = null, Material scorch = null, Material cracks = null)
        {
            blasts = blastRigs;
            fireworks = fireworkRigs;
            lights = flashLights;
            scorches = scorchDecals ?? new List<Renderer>();
            scorchMaterial = scorch;
            crackMaterial = cracks;
        }

        /// След на дороге под точкой: копоть (взрыв) или трещины (удар кувалды, наковальни). Пул, самый старый — заново.
        public void Scorch(Vector3 position, float size, bool cracks = false)
        {
            if (scorches.Count == 0 || CameraDistance(position) > CullDistance) return;
            if (!Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore) || hit.rigidbody != null) return;
            var r = scorches[nextScorch];
            scorchAt[nextScorch] = Time.time;
            nextScorch = (nextScorch + 1) % scorches.Count;
            var material = cracks ? crackMaterial : scorchMaterial;
            if (material != null) r.sharedMaterial = material;
            r.transform.SetPositionAndRotation(hit.point + hit.normal * 0.03f, Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            r.transform.localScale = Vector3.one * Mathf.Clamp(size, 1f, 8f);
            r.gameObject.SetActive(true);
        }

        void FadeScorches()
        {
            if (scorchAt == null) return;
            for (int i = 0; i < scorches.Count; i++)
            {
                var r = scorches[i];
                if (r == null || !r.gameObject.activeSelf) continue;
                float age = Time.time - scorchAt[i];
                if (age > ScorchSeconds)
                {
                    r.gameObject.SetActive(false);
                    continue;
                }
                // Цвет материала (копоть — белая текстура, трещины — тёмные), прозрачность тает к концу.
                var color = r.sharedMaterial != null ? r.sharedMaterial.GetColor(BaseColorId) : Color.white;
                color.a *= Mathf.Clamp01((ScorchSeconds - age) / 4f);
                r.GetPropertyBlock(scorchBlock);
                scorchBlock.SetColor(BaseColorId, color);
                r.SetPropertyBlock(scorchBlock);
            }
        }

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice_" + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0.8f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 10f;
                source.maxDistance = 280f;
                source.dopplerLevel = 0f;
                voices[i] = source;
            }
            lightUntil = new float[lights.Count];
            lightPower = new float[lights.Count];
            scorchAt = new float[scorches.Count];
            scorchBlock = new MaterialPropertyBlock();
            foreach (var s in scorches) if (s != null) s.gameObject.SetActive(false);
            foreach (var l in lights) if (l != null) l.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Register(IChaosReactive item)
        {
            if (Instance != null && !Instance.reactive.Contains(item)) Instance.reactive.Add(item);
        }

        public static void Unregister(IChaosReactive item)
        {
            if (Instance != null) Instance.reactive.Remove(item);
        }

        static float CameraDistance(Vector3 position)
        {
            var cam = Camera.main;
            return cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
        }

        // ---------- взрывы ----------

        /// Взрыв в точке. scale — размер (1 — мина), tint — цвет дыма и конфетти (для цветного дыма и фейков).
        public void Blast(Vector3 position, BlastStyle style, float scale = 1f, Color? tint = null)
        {
            BlastCount++;
            Blasted?.Invoke(position, style, scale);
            float distance = CameraDistance(position);
            if (distance > CullDistance || blasts.Count == 0) return;
            bool simple = distance > SimpleDistance;
            var rig = blasts[nextBlast];
            nextBlast = (nextBlast + 1) % blasts.Count;
            rig.StartedAt = Time.time;
            rig.Root.position = position;
            rig.Root.localScale = Vector3.one * scale;
            Color smoke = tint ?? new Color(0.45f, 0.44f, 0.46f, 0.8f);

            switch (style)
            {
                case BlastStyle.Cartoon:
                    Fire(rig.Flash); Fire(rig.Fireball); Fire(rig.Ring); Tinted(rig.Smoke, smoke);
                    if (!simple) { Fire(rig.Sparks); Fire(rig.Stars); }
                    Flash(position + Vector3.up * 2f, new Color(1f, 0.7f, 0.35f), 7f * scale, 0.35f);
                    Scorch(position, 2.6f * scale);
                    break;
                case BlastStyle.Big:
                    Fire(rig.Flash); Fire(rig.Fireball); Fire(rig.Ring); Tinted(rig.Smoke, smoke);
                    if (!simple) { Fire(rig.Sparks); Fire(rig.Debris); Fire(rig.Stars); }
                    Flash(position + Vector3.up * 3f, new Color(1f, 0.62f, 0.3f), 12f * scale, 0.5f);
                    if (tint == null) Scorch(position, 3.2f * scale);
                    break;
                case BlastStyle.Mini:
                    Fire(rig.Flash); Fire(rig.Fireball);
                    if (!simple) Fire(rig.Sparks);
                    break;
                case BlastStyle.Fake:
                    Tinted(rig.Smoke, smoke); Fire(rig.Confetti);
                    if (!simple) Fire(rig.Stars);
                    break;
                case BlastStyle.ColorPuff:
                    Tinted(rig.Smoke, smoke); Fire(rig.Ring);
                    break;
                case BlastStyle.Spark:
                    if (!simple) { Fire(rig.Sparks); Fire(rig.Stars); }
                    Fire(rig.Flash);
                    break;
                case BlastStyle.Ice:
                    Tinted(rig.Smoke, new Color(0.85f, 0.95f, 1f, 0.7f));
                    if (!simple) { Tinted(rig.Debris, new Color(0.75f, 0.92f, 1f, 1f)); Fire(rig.Sparks); }
                    break;
            }
        }

        static void Fire(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Clear(true);
            ps.Play(true);
        }

        static void Tinted(ParticleSystem ps, Color color)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.25f));
            Fire(ps);
        }

        /// Вспышка света от взрыва: ложится на дорогу, машины и дома, гаснет за duration.
        public void Flash(Vector3 position, Color color, float power, float duration)
        {
            if (lights.Count == 0) return;
            int i = nextLight;
            nextLight = (nextLight + 1) % lights.Count;
            var l = lights[i];
            if (l == null) return;
            l.transform.position = position;
            l.color = color;
            l.range = 14f + power * 2.4f;
            l.intensity = power;
            l.enabled = true;
            lightPower[i] = power;
            lightUntil[i] = Time.time + duration;
        }

        void Update()
        {
            FadeScorches();
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (l == null || !l.enabled) continue;
                float left = lightUntil[i] - Time.time;
                if (left <= 0f)
                {
                    l.enabled = false;
                    continue;
                }
                l.intensity = lightPower[i] * Mathf.Clamp01(left / 0.3f);
            }
        }

        // ---------- фейерверки ----------

        static readonly Color[] FireworkColors =
        {
            new Color(1f, 0.3f, 0.25f), new Color(1f, 0.82f, 0.2f), new Color(0.25f, 0.95f, 0.85f),
            new Color(0.6f, 1f, 0.3f), new Color(0.95f, 0.4f, 1f), new Color(0.35f, 0.6f, 1f),
        };

        public static Color RandomFireworkColor(System.Random random) => FireworkColors[random.Next(FireworkColors.Length)];

        /// Безопасный декоративный фейерверк: взлёт со свистом и раскрытие цветным шаром с треском.
        public bool Firework(Vector3 ground, float height, Color color, float delay = 0f)
        {
            if (CameraDistance(ground) > CullDistance * 1.6f) return false;
            foreach (var rig in fireworks)
            {
                if (rig.Busy) continue;
                rig.Busy = true;
                FireworkCount++;
                StartCoroutine(Launch(rig, ground, height, color, delay));
                return true;
            }
            return false;
        }

        IEnumerator Launch(FireworkRig rig, Vector3 ground, float height, Color color, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            rig.Root.position = ground;
            Fire(rig.Trail);
            Sound(Audio.SoundBank.Whistle, ground, 0.45f, UnityEngine.Random.Range(0.9f, 1.15f));
            const float rise = 0.85f;
            for (float t = 0f; t < rise; t += Time.deltaTime)
            {
                float k = t / rise;
                rig.Root.position = ground + Vector3.up * height * (1f - (1f - k) * (1f - k));
                yield return null;
            }
            if (rig.Trail != null) rig.Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Vector3 top = ground + Vector3.up * height;
            // Яркость выше 1 — свечение (bloom): видно и на дневном небе.
            Tinted(rig.Burst, color * 2.2f);
            Tinted(rig.Glitter, Color.Lerp(color, Color.white, 0.5f) * 1.8f);
            Flash(top, color, 6f, 0.45f);
            Sound(Audio.SoundBank.Crackle, top, 0.5f, UnityEngine.Random.Range(0.9f, 1.1f));
            yield return new WaitForSeconds(2.2f);
            rig.Busy = false;
        }

        /// Залп салюта вдоль дороги: count ракет по обеим обочинам на участке длиной spread.
        public void Volley(Vector3 center, Vector3 forward, Vector3 right, float roadHalf, int count, float spread, System.Random random)
        {
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float along = ((float)random.NextDouble() - 0.5f) * spread;
                Vector3 p = center + forward * along + right * side * (roadHalf + 4f + (float)random.NextDouble() * 6f);
                Firework(p, 16f + (float)random.NextDouble() * 12f, RandomFireworkColor(random), i * 0.18f);
            }
        }

        // ---------- звук и надписи ----------

        /// Звук в мире: громче рядом с камерой, тише вдали.
        public void Sound(AudioClip clip, Vector3 position, float volume, float pitch = 1f)
        {
            if (clip == null || voices == null) return;
            // Свободный голос, иначе — тот, что звучит дольше всех.
            AudioSource best = null;
            float bestTime = -1f;
            foreach (var v in voices)
            {
                if (!v.isPlaying)
                {
                    best = v;
                    break;
                }
                if (v.time <= bestTime) continue;
                bestTime = v.time;
                best = v;
            }
            if (best == null) best = voices[0];
            best.transform.position = position;
            best.pitch = pitch;
            best.volume = Mathf.Clamp01(volume) * GameSettings.SfxVolume;
            best.clip = clip;
            best.Play();
        }

        /// Мультяшная надпись над местом события («БУМ!», «ПШИК!», «БОЙНЬ!»).
        public void Pop(Vector3 position, string text, Color color, float size = 1f)
        {
            if (CameraDistance(position) > SimpleDistance * 1.2f) return;
            Popped?.Invoke(position, text, color, size);
        }

        // ---------- ударная волна ----------

        /// Ударная волна: лёгкие объекты отлетают (чем ближе и легче — тем сильнее), реагирующие предметы
        /// (мины, бочки) получают сигнал — так получаются цепные реакции. Машины волна не трогает: их толкает
        /// само событие через CarController (с пределами честности).
        public void Shockwave(Vector3 position, float radius, float strength) => Shockwave(position, radius, strength, 1, null);

        /// Ударная волна звена generation: отбрасывает тела, поджигает мины и бочки (они станут звеном generation),
        /// в «много» и выше — отбрасывает и машины (кроме source), которые считает этот компьютер.
        public void Shockwave(Vector3 position, float radius, float strength, int generation, Car.CarController source = null)
        {
            if (Time.time - shockWindowAt > 1f)
            {
                shockWindowAt = Time.time;
                shocksInWindow = 0;
            }
            if (++shocksInWindow > MaxShocksPerSecond) return;
            ShockCount++;
            MaxGeneration = Mathf.Max(MaxGeneration, generation);
            bool spread = generation <= MaxChain;
            var power = ChaosPower.Current;
            var race = EventDirector.Current != null ? EventDirector.Current.Race : null;
            if (spread && power.CarBilliards && race != null)
            {
                foreach (var r in race.Racers)
                {
                    var car = r.Controller;
                    if (car == source || r.Finished || !car.Physics.Simulated) continue;
                    Vector3 d = r.Car.position - position;
                    float dist = d.magnitude;
                    if (dist > radius || dist < 0.05f) continue;
                    // Машину, которую уже подбросило это же звено, волна не добивает.
                    if (Time.time - car.LaunchedAt < 0.25f) continue;
                    float k = 1f - dist / radius;
                    Vector3 away = Vector3.ProjectOnPlane(d, Vector3.up).normalized;
                    car.ApplyLaunch(away * strength * 1.1f * k + Vector3.up * strength * 0.9f * k, UnityEngine.Random.onUnitSphere * 6f * k, "ударная волна", generation);
                    ChainHits++;
                    ShockHitCar?.Invoke(car, generation);
                }
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live) continue;
                Vector3 d = body.transform.position - position;
                float dist = d.magnitude;
                if (dist > radius) continue;
                float k = 1f - dist / radius;
                Vector3 dir = dist > 0.1f ? d / dist : Vector3.up;
                body.Kick((dir + Vector3.up * 0.8f).normalized * strength * k);
            }
            if (!spread) return;
            for (int i = reactive.Count - 1; i >= 0; i--)
                if (i < reactive.Count) reactive[i].OnShock(position, radius, strength, generation);
        }
    }
}
