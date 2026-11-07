using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Батуты: на прямой вырастают три-четыре пружинящих батута на разных полосах. Наехал на центр —
    /// мультяшный прыжок до ~8 м («БОЙНЬ!»), в воздухе можно довернуть рулём. В невесомости прыжки ещё выше,
    /// с колец над дорогой можно собрать рывки. Хорошее событие: ИИ тоже к ним стремится.
    public sealed class BouncersEvent : RaceEvent
    {
        public const int Count = 4;
        const float PadRadius = 1.6f;

        sealed class Pad
        {
            public Transform Root, Mat;
            public float Progress, Lateral;
            public Vector3 Position;
            public float BounceAt = -10f;
        }

        readonly Pad[] pads = new Pad[Count];
        readonly Dictionary<CarController, float> lastJump = new Dictionary<CarController, float>();
        TelegraphMarker marker;
        int used;

        void Awake()
        {
            for (int i = 0; i < Count; i++)
            {
                var root = Child("Pad_" + i);
                pads[i] = new Pad { Root = root, Mat = root.Find("Mat") };
                root.gameObject.SetActive(false);
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            lastJump.Clear();
            float length = Context.Track.Length;
            used = RoadHalf > 8f ? 4 : 3;
            float lane = RandomRange(-1f, 1f);
            for (int i = 0; i < Count; i++)
            {
                var pad = pads[i];
                if (i >= used)
                {
                    pad.Root.gameObject.SetActive(false);
                    continue;
                }
                // Змейкой по полосам: каждый следующий — на соседней полосе.
                lane = Mathf.Clamp(lane + RandomRange(0.5f, 1.1f) * (i % 2 == 0 ? 1f : -1f), -1f, 1f);
                pad.Lateral = lane * (RoadHalf - PadRadius - 0.8f);
                pad.Progress = Progress + (i - (used - 1) * 0.5f) * (ZoneLength / used) / length;
                var h = At(pad.Progress, pad.Lateral);
                pad.Position = Ground(h.Position);
                pad.Root.gameObject.SetActive(true);
                pad.Root.SetPositionAndRotation(pad.Position - Vector3.up * 1.2f, Quaternion.LookRotation(h.Forward));
            }
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = PadRadius;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.8f, true, true, ZoneLength * 0.5f / (RoadHalf * 0.8f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            for (int i = 0; i < used; i++)
                pads[i].Root.position = pads[i].Position - Vector3.up * 1.2f * (1f - EaseOutBack(Mathf.Clamp01((progress - i * 0.08f) / 0.5f)));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            for (int i = 0; i < used; i++) pads[i].Root.position = pads[i].Position;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            for (int i = 0; i < used; i++)
            {
                var pad = pads[i];
                // Мат пружинит: после прыжка — сильная волна, в покое — лёгкое дыхание.
                float t = Time.time - pad.BounceAt;
                float dip = t < 0.8f ? -Mathf.Sin(t * 22f) * Mathf.Exp(-t * 5f) * 0.35f : Mathf.Sin(Time.time * 3f + i) * 0.04f;
                if (pad.Mat != null) pad.Mat.localPosition = new Vector3(0f, 0.52f + dip, 0f);
                foreach (var racer in RacersNear(pad.Position, PadRadius))
                {
                    if (racer.Car.position.y > pad.Position.y + 1.6f) continue;
                    lastJump.TryGetValue(racer.Controller, out float at);
                    if (Time.time - at < 1f) continue;
                    lastJump[racer.Controller] = Time.time;
                    pad.BounceAt = Time.time;
                    // Батут запускает на огромную высоту; иногда — с сальто назад.
                    if (racer.Controller.Physics.Simulated)
                    {
                        bool flip = Random.value < 0.4f;
                        racer.Controller.ApplyLaunch(racer.Car.forward * 4f + Vector3.up * 28f,
                            flip ? racer.Car.right * -5.5f : Vector3.up * Random.Range(-2f, 2f), flip ? "сальто с батута" : "батут");
                    }
                    ChaosEffects?.Sound(SoundBank.Spring, pad.Position, 0.9f, Random.Range(0.9f, 1.15f));
                    ChaosEffects?.Pop(pad.Position + Vector3.up * 3f, "БОЙНЬ!", new Color(0.3f, 1f, 0.85f), 1.3f);
                    ChaosEffects?.Blast(pad.Position + Vector3.up * 0.6f, BlastStyle.Spark, 0.7f);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < used; i++) pads[i].Root.position = pads[i].Position - Vector3.up * 1.2f * progress;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var pad in pads) pad.Root.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            for (int i = 0; i < used; i++)
                list.Add(new HazardInfo { Progress = pads[i].Progress, Center = pads[i].Position, Radius = PadRadius * 0.6f, HalfLength = PadRadius, Good = true, Since = StartedAt });
        }
    }
}
