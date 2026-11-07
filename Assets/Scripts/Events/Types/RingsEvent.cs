using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Кольца-рывки: над дорогой змейкой висят крутящиеся золотые кольца. Проехал сквозь — «ДЗЫНЬ!» и рывок.
    /// Часть колец висит высоко: туда долетают только с батута, трамплина или в невесомости (комбинации).
    public sealed class RingsEvent : RaceEvent
    {
        public const int Count = 6;
        const float RingRadius = 2f;

        sealed class Ring
        {
            public Transform Root;
            public Vector3 Position;
            public Vector3 Forward;
            public float Progress, Lateral;
            public float FlashAt = -10f;
        }

        readonly Ring[] rings = new Ring[Count];
        readonly Dictionary<(CarController, int), bool> taken = new Dictionary<(CarController, int), bool>();
        TelegraphMarker marker;

        void Awake()
        {
            for (int i = 0; i < Count; i++)
            {
                rings[i] = new Ring { Root = Child("Ring_" + i) };
                rings[i].Root.gameObject.SetActive(false);
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            taken.Clear();
            float length = Context.Track.Length;
            float lane = RandomRange(-0.6f, 0.6f);
            for (int i = 0; i < Count; i++)
            {
                var ring = rings[i];
                lane = Mathf.Clamp(lane + RandomRange(-0.5f, 0.5f), -0.75f, 0.75f);
                ring.Lateral = lane * (RoadHalf - RingRadius);
                ring.Progress = Progress + (i - (Count - 1) * 0.5f) * (ZoneLength / Count) / length;
                var h = At(ring.Progress, ring.Lateral);
                // Каждое третье кольцо — высоко (только для прыжка).
                bool high = i % 3 == 2 && RandomRange(0f, 1f) < 0.7f;
                ring.Position = h.Position + Vector3.up * (high ? RandomRange(5.5f, 7f) : RingRadius + 0.15f);
                ring.Forward = h.Forward;
                ring.Root.gameObject.SetActive(true);
                ring.Root.SetPositionAndRotation(ring.Position, Quaternion.LookRotation(h.Forward));
                ring.Root.localScale = Vector3.zero;
            }
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = RingRadius;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.75f, true, true, ZoneLength * 0.5f / (RoadHalf * 0.75f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            for (int i = 0; i < Count; i++) rings[i].Root.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01((progress - i * 0.06f) / 0.5f));
            Spin();
        }

        void Spin()
        {
            for (int i = 0; i < Count; i++)
            {
                var ring = rings[i];
                float flash = Mathf.Clamp01(1f - (Time.time - ring.FlashAt) * 2f);
                ring.Root.SetPositionAndRotation(ring.Position + Vector3.up * Mathf.Sin(Time.time * 2f + i) * 0.15f,
                    Quaternion.LookRotation(ring.Forward) * Quaternion.AngleAxis(Time.time * 60f + i * 30f, Vector3.forward));
                if (flash > 0f) ring.Root.localScale = Vector3.one * (1f + flash * 0.4f);
            }
        }

        protected override void OnActivate() => marker.Hide();

        protected override void OnActiveUpdate(float progress, float time)
        {
            Spin();
            foreach (var racer in Racers)
            {
                Vector3 center = racer.Car.position + Vector3.up * 0.8f;
                for (int i = 0; i < Count; i++)
                {
                    var ring = rings[i];
                    Vector3 d = center - ring.Position;
                    float through = Vector3.Dot(d, ring.Forward);
                    if (Mathf.Abs(through) > 1.4f) continue;
                    if ((d - ring.Forward * through).magnitude > RingRadius - 0.2f) continue;
                    if (taken.ContainsKey((racer.Controller, i))) continue;
                    taken[(racer.Controller, i)] = true;
                    ring.FlashAt = Time.time;
                    if (racer.Controller.Physics.Simulated) racer.Controller.ApplyBoost(ring.Position.y - racer.Car.position.y > 4f ? 1.25f : 1.18f);
                    ChaosEffects?.Sound(SoundBank.Ding, ring.Position, 0.8f, 1f + i * 0.05f);
                    ChaosEffects?.Blast(ring.Position, BlastStyle.Spark, 0.9f);
                    ChaosEffects?.Pop(ring.Position + Vector3.up * 2.4f, "ДЗЫНЬ!", new Color(1f, 0.85f, 0.2f), 1f);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < Count; i++) rings[i].Root.localScale = Vector3.one * (1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var r in rings) r.Root.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            foreach (var r in rings)
            {
                if (r.Position.y - Center.y > 4f) continue;
                list.Add(new HazardInfo { Progress = r.Progress, Center = r.Position, Radius = 1f, HalfLength = 1f, Good = true, Since = StartedAt });
            }
        }
    }
}
