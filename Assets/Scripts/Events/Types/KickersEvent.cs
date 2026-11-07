using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Боксёрские перчатки: вдоль обочин по очереди слева и справа стоят ящики с пружинными перчатками.
    /// Ящик трясётся и мигает перед ударом, перчатка выстреливает поперёк крайней полосы и отбрасывает машину
    /// к середине дороги с лёгким закручиванием («БАЦ!») — неожиданно, но управляемо. Середина дороги
    /// перчатками не достаётся. Ритм — по часам события, у всех одинаково.
    public sealed class KickersEvent : RaceEvent
    {
        public const int Count = 6;
        const float Period = 2.4f;
        const float Windup = 0.45f;
        const float Punch = 0.12f;
        const float Hold = 0.28f;
        const float Retract = 0.6f;

        sealed class Kicker
        {
            public Transform Box, Spring, Glove;
            public float Side;
            public float Progress;
            public HazardPoint Point;
            public float Phase;
            public float Extension;
            public Vector3 Foot;
        }

        readonly Kicker[] kickers = new Kicker[Count];
        readonly Dictionary<CarController, float> lastHit = new Dictionary<CarController, float>();
        TelegraphMarker marker;
        float reach;

        void Awake()
        {
            for (int i = 0; i < Count; i++)
            {
                var k = new Kicker { Box = Child("Box_" + i), Spring = Child("Spring_" + i), Glove = Child("Glove_" + i) };
                k.Box.gameObject.SetActive(false);
                k.Spring.gameObject.SetActive(false);
                k.Glove.gameObject.SetActive(false);
                kickers[i] = k;
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            lastHit.Clear();
            float length = Context.Track.Length;
            reach = Mathf.Min(4.6f, RoadHalf * 0.42f);
            float first = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            for (int i = 0; i < Count; i++)
            {
                var k = kickers[i];
                k.Side = i % 2 == 0 ? first : -first;
                k.Progress = Progress + (i - (Count - 1) * 0.5f) * (ZoneLength / Count) / length;
                k.Point = At(k.Progress, k.Side * (RoadHalf + 0.7f));
                k.Phase = i * 0.47f + RandomRange(0f, 0.3f);
                k.Box.gameObject.SetActive(true);
                k.Spring.gameObject.SetActive(true);
                k.Glove.gameObject.SetActive(true);
                // Ящик смотрит «дыркой» (+x модели) на дорогу.
                Quaternion face = Quaternion.LookRotation(k.Point.Forward) * Quaternion.Euler(0f, k.Side > 0f ? 180f : 0f, 0f);
                k.Foot = Ground(k.Point.Position);
                k.Box.SetPositionAndRotation(k.Foot - Vector3.up * 2.4f, face);
                SetGlove(k, 0f);
            }
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = reach * 0.5f + 0.8f;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.85f, false, false, ZoneLength * 0.5f / (RoadHalf * 0.85f));
        }

        void SetGlove(Kicker k, float extension)
        {
            k.Extension = extension;
            Vector3 mouth = k.Box.TransformPoint(new Vector3(0.68f, 1.45f, 0f));
            Vector3 dir = k.Box.right;
            float len = 0.15f + extension * reach;
            k.Spring.SetPositionAndRotation(mouth, k.Box.rotation);
            k.Spring.localScale = new Vector3(len, 1f, 1f);
            k.Glove.SetPositionAndRotation(mouth + dir * len, k.Box.rotation);
        }

        /// Фаза цикла перчатки: 0 — в ящике, 1 — вытянута. shake — дрожание ящика перед ударом.
        float Extension(Kicker k, out float shake)
        {
            float t = Mathf.Repeat(EventTime + k.Phase, Period);
            shake = 0f;
            if (t < Windup)
            {
                shake = t / Windup;
                return 0f;
            }
            t -= Windup;
            if (t < Punch) return t / Punch;
            t -= Punch;
            if (t < Hold) return 1f;
            t -= Hold;
            if (t < Retract) return 1f - t / Retract;
            return 0f;
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01(progress / 0.6f));
            foreach (var k in kickers)
            {
                k.Box.position = k.Foot - Vector3.up * 2.4f * (1f - rise);
                SetGlove(k, 0f);
            }
        }

        protected override void OnActivate() => marker.Hide();

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var k in kickers)
            {
                float before = k.Extension;
                float e = Extension(k, out float shake);
                k.Box.position = k.Foot + (shake > 0f ? k.Box.forward * Mathf.Sin(Time.time * 60f) * 0.06f * shake : Vector3.zero);
                SetGlove(k, e);
                if (e > 0.5f && before <= 0.5f) ChaosEffects?.Sound(SoundBank.Whack, k.Glove.position, 0.5f, 1.3f);
                if (e < 0.6f) continue;
                foreach (var racer in RacersNear(k.Glove.position, 1.9f))
                {
                    lastHit.TryGetValue(racer.Controller, out float at);
                    if (Time.time - at < 1f) continue;
                    lastHit[racer.Controller] = Time.time;
                    // Отбрасывает к середине дороги, чуть вперёд и с закручиванием — не в стену.
                    if (racer.Controller.Physics.Simulated)
                        racer.Controller.ApplyLaunch(-k.Point.Right * k.Side * 25f + k.Point.Forward * 3f + Vector3.up * 9f,
                            racer.Car.forward * k.Side * 8f - Vector3.up * k.Side * 4f, "удар перчаткой");
                    ChaosEffects?.Blast(k.Glove.position, BlastStyle.Spark, 1.4f);
                    ChaosEffects?.Sound(SoundBank.Whack, k.Glove.position, 1f, 0.9f);
                    ChaosEffects?.Pop(k.Glove.position + Vector3.up * 1.6f, "БАЦ!", new Color(1f, 0.25f, 0.35f), 1.3f);
                    ShakeCameraNear(k.Glove.position, 0.5f, 25f);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var k in kickers)
            {
                k.Box.position = k.Foot - Vector3.up * 2.4f * progress;
                SetGlove(k, 0f);
            }
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var k in kickers)
            {
                k.Box.gameObject.SetActive(false);
                k.Spring.gameObject.SetActive(false);
                k.Glove.gameObject.SetActive(false);
            }
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            foreach (var k in kickers)
            {
                var h = At(k.Progress, k.Side * (RoadHalf - reach * 0.5f));
                list.Add(new HazardInfo { Progress = k.Progress, Center = h.Position, Radius = reach * 0.5f + 0.6f, HalfLength = 1.5f, Good = false, Obstacle = true, Since = StartedAt });
            }
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            foreach (var k in kickers)
            {
                if (k.Extension < 0.5f) continue;
                float edge = k.Side * RoadHalf;
                list.Add(new RoadBlock { Progress = k.Progress, HalfLength = 0.8f, LateralMin = Mathf.Min(edge, edge - k.Side * reach), LateralMax = Mathf.Max(edge, edge - k.Side * reach) });
            }
        }
    }
}
