using System.Collections.Generic;
using RandomCar.Car;
using RandomCar.Maps;
using UnityEngine;

namespace RandomCar.Events
{
    /// Обстрел: на участке ~70 м заранее загораются метки ударов, когда игрок подъезжает — сверху один за другим
    /// падают снаряды (метеориты, на «Тель-Авиве» — ракеты). Метки стоят змейкой по разным сторонам дороги:
    /// проход есть всегда, но ехать придётся слаломом, подстраивая скорость. Каждый удар толкает и замедляет
    /// тех, кто в его круге (в пределах FairnessLimits), и оставляет воронку — повреждённый участок, который
    /// ещё немного притормаживает. Держится, пока игрок не проедет.
    /// Вариант «Тель-Авива» (skyRockets): ракеты ведёт RocketShow — видимый полёт с горизонта к метке, часть сбивает ПВО
    /// (метка гаснет, удара нет); взрывы разного масштаба освещают дома и асфальт. Решения — из сида события.
    public sealed class BarrageEvent : RaceEvent
    {
        public const int Strikes = 6;
        public const int Blasts = 3;
        const float FallSeconds = 0.9f;
        const float StartHeight = 120f;
        const float Spacing = 11f;

        [SerializeField] bool skyRockets;

        public void UseSkyRockets(bool value) => skyRockets = value;

        sealed class Strike
        {
            public RocketShow.Strike Sky;
            public bool WillIntercept;
            public float Scale = 1f;
            public readonly HashSet<CarController> Slowed = new HashSet<CarController>();
            public Transform Projectile;
            public ParticleSystem Trail;
            public Transform Crater;
            public TelegraphMarker Marker;
            public Vector3 Point;
            public float Progress;
            public float LandAt;
            public bool Landed;
        }

        readonly Strike[] strikes = new Strike[Strikes];
        readonly (ParticleSystem explosion, ParticleSystem smoke, ParticleSystem sparks)[] blasts = new (ParticleSystem, ParticleSystem, ParticleSystem)[Blasts];
        int nextBlast;
        float strikeRadius;

        void Awake()
        {
            for (int i = 0; i < Strikes; i++)
            {
                var projectile = Child("Projectile_" + i);
                strikes[i] = new Strike
                {
                    Projectile = projectile,
                    Trail = projectile.GetComponentInChildren<ParticleSystem>(true),
                    Crater = Child("Crater_" + i),
                    Marker = Child(i == 0 ? "Marker" : "Marker_" + i).GetComponent<TelegraphMarker>(),
                };
            }
            for (int i = 0; i < Blasts; i++) blasts[i] = (Fx("FxExplosion_" + i), Fx("FxSmoke_" + i), Fx("FxSparks_" + i));
        }

        protected override void OnTelegraph()
        {
            strikeRadius = Mathf.Min(MaxRadius, 3.4f);
            Radius = strikeRadius;
            float length = Context.Track.Length;
            float limit = Mathf.Max(0f, RoadHalf - strikeRadius - 0.3f);
            // Змейка: стороны чередуются, отступ от оси — случайный, но не дальше, чем позволяет проезд.
            float side = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            for (int i = 0; i < Strikes; i++)
            {
                var s = strikes[i];
                float along = (i - (Strikes - 1) * 0.5f) * Spacing + RandomRange(-2f, 2f);
                float lateral = side * limit * RandomRange(0.35f, 1f);
                side = -side;
                s.Progress = Progress + along / length;
                var h = At(s.Progress, lateral);
                s.Point = Ground(h.Position);
                s.Landed = false;
                s.LandAt = float.MaxValue;
                s.Projectile.gameObject.SetActive(false);
                s.Crater.gameObject.SetActive(false);
                s.Marker.Show(s.Point, h.Forward, strikeRadius, false, false);
                s.Slowed.Clear();
                s.Sky = null;
                // Масштаб взрыва: от вспышки до крупного огненного шара (из сида — одинаково у всех игроков).
                float roll = RandomRange(0f, 1f);
                s.Scale = roll < 0.4f ? 0.8f : roll < 0.8f ? 1.3f : 2.1f;
            }
            Center = At(Progress, 0f).Position;
            nextBlast = 0;
            sky = skyRockets && RocketShow.Instance != null;
            if (sky)
            {
                // Перехват: до двух ракет из шести (остальные — удары по меткам).
                int intercepts = 0;
                foreach (var s in strikes)
                {
                    s.WillIntercept = intercepts < 2 && RandomRange(0f, 1f) < 0.3f;
                    if (s.WillIntercept) intercepts++;
                }
                for (int i = 0; i < Strikes; i++)
                    strikes[i].Sky = RocketShow.Instance.BeginStrike(strikes[i].Point, Time.time + EstimatedLand(i, 0f), strikes[i].WillIntercept);
            }
        }

        bool sky;

        /// Оценка момента удара i от текущего момента (до активной фазы — по подъезду игрока).
        float EstimatedLand(int i, float telegraphElapsed)
        {
            var focus = FocusRacer();
            float speed = focus != null ? Mathf.Max(8f, focus.Controller.Speed) : 20f;
            float untilActive = Mathf.Max(0f, WarningDuration - telegraphElapsed);
            float eta = focus != null ? Mathf.Max(0f, SignedAheadOf(focus, strikes[i].Progress)) / speed : untilActive + i * 0.45f;
            return Mathf.Max(untilActive + 0.35f + i * 0.2f, eta - 0.5f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            for (int i = 0; i < Strikes; i++)
            {
                var s = strikes[i];
                if (s.Landed) continue;
                s.Marker.Pulse(progress);
                if (s.Sky == null) continue;
                if (s.Sky.Intercepted) Cancel(s);
                else s.Sky.ArriveAt = Time.time + EstimatedLand(i, time);
            }
        }

        /// ПВО сбила ракету: метка гаснет, удара не будет.
        void Cancel(Strike s)
        {
            s.Landed = true;
            s.Marker.Hide();
        }

        protected override void OnActivate()
        {
            // Удары — к подъезду игрока: каждый падает чуть раньше, чем он доедет до метки (не позже — проезд уже начат).
            var focus = FocusRacer();
            float speed = focus != null ? Mathf.Max(8f, focus.Controller.Speed) : 20f;
            for (int i = 0; i < Strikes; i++)
            {
                var s = strikes[i];
                float eta = focus != null ? Mathf.Max(0f, SignedAheadOf(focus, s.Progress)) / speed : i * 0.45f;
                s.LandAt = Mathf.Max(0.35f + i * 0.2f, eta - 0.5f);
                if (s.Sky != null && !s.Landed) s.Sky.ArriveAt = Time.time + s.LandAt;
            }
        }

        float SignedAheadOf(RandomCar.Race.Racer racer, float progress)
        {
            float length = Context.Track.Length;
            float d = RandomCar.Track.TrackPath.Wrap(progress - racer.Progress) * length;
            return d > length * 0.5f ? d - length : d;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var s in strikes)
            {
                if (s.Landed)
                {
                    // Воронка — повреждённый участок: проехавший по ней чуть теряет скорость (один раз).
                    if (s.Crater.gameObject.activeSelf)
                        foreach (var racer in RacersNear(s.Point, strikeRadius * 0.55f))
                            if (s.Slowed.Add(racer.Controller)) racer.Controller.ApplySlow(0.85f, 0.5f);
                    continue;
                }
                if (s.Sky != null && s.Sky.Intercepted)
                {
                    Cancel(s);
                    continue;
                }
                s.Marker.Pulse(Mathf.Clamp01(time / Mathf.Max(0.1f, s.LandAt)));
                float fallStart = s.LandAt - FallSeconds;
                if ((!sky || s.Sky == null) && time >= fallStart)
                {
                    if (!s.Projectile.gameObject.activeSelf)
                    {
                        s.Projectile.gameObject.SetActive(true);
                        if (s.Trail != null) s.Trail.Play(true);
                    }
                    float f = Mathf.Clamp01((time - fallStart) / FallSeconds);
                    s.Projectile.SetPositionAndRotation(s.Point + Vector3.up * Mathf.Lerp(StartHeight, 0f, f * f), Quaternion.Euler(0f, time * 260f, 0f));
                }
                if (time >= s.LandAt) Land(s);
            }
        }

        void Land(Strike s)
        {
            s.Landed = true;
            s.Projectile.gameObject.SetActive(false);
            s.Marker.Hide();
            s.Crater.gameObject.SetActive(true);
            s.Crater.SetPositionAndRotation(s.Point + Vector3.up * 0.02f, Quaternion.Euler(0f, RandomRange(0f, 360f), 0f));
            s.Crater.localScale = Vector3.one * strikeRadius * 0.5f;
            var blast = blasts[nextBlast];
            nextBlast = (nextBlast + 1) % Blasts;
            Play(blast.explosion, s.Point + Vector3.up * 0.6f);
            Play(blast.smoke, s.Point);
            Play(blast.sparks, s.Point + Vector3.up * 0.3f);
            if (RocketShow.Instance != null) RocketShow.Instance.GroundBlast(s.Point, s.Scale);
            ShakeCameraNear(s.Point, 0.35f + 0.2f * s.Scale, 45f);
            float reach = strikeRadius + 1.2f;
            foreach (var racer in RacersNear(s.Point, reach))
            {
                Vector3 away = racer.Car.position - s.Point;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.right;
                float falloff = 1f - Mathf.Clamp01(away.magnitude / reach);
                racer.Controller.ApplyImpulse(away.normalized * (4f + 4f * falloff) + Vector3.up * (2.5f + 2.5f * falloff));
                racer.Controller.ApplySlow(0.72f, 0.9f);
            }
        }

        protected override void OnEnd()
        {
            foreach (var s in strikes)
            {
                s.Marker.Hide();
                s.Projectile.gameObject.SetActive(false);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var s in strikes)
                if (s.Crater.gameObject.activeSelf) s.Crater.localScale = Vector3.one * strikeRadius * 0.5f * (1f - progress);
        }

        protected override void Cleanup()
        {
            foreach (var s in strikes)
            {
                s.Marker.Hide();
                s.Projectile.gameObject.SetActive(false);
                s.Crater.gameObject.SetActive(false);
                s.Sky = null;
            }
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            foreach (var s in strikes)
            {
                if (s.Landed) continue;
                list.Add(new HazardInfo
                {
                    Progress = s.Progress,
                    Center = s.Point,
                    Radius = strikeRadius,
                    HalfLength = strikeRadius,
                    Good = false,
                    Obstacle = false,
                    Since = StartedAt,
                });
            }
        }
    }
}
