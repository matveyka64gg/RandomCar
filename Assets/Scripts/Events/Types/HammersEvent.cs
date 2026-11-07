using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Молоты: две арки поперёк дороги вырастают из асфальта, с каждой свисают огромные молоты-маятники
    /// и качаются поперёк полос. Опасен только низ размаха (у дороги); на краях размаха молот высоко.
    /// Проезд — по краям дороги или угадать момент. Удар — мультяшный «БАМ»: машину отбрасывает вбок
    /// по ходу молота с закручиванием (с пределами честности). Фаза — по часам события: у всех одинаково.
    public sealed class HammersEvent : RaceEvent
    {
        public const int Rows = 2;
        public const int HammersPerRow = 2;
        const float PivotHeight = 9.6f;
        const float ArmLength = 7.6f;
        const float HeadRadius = 1.25f;
        const float Amplitude = 56f;
        const float RowSpacing = 20f;
        const float Sink = 11f;

        sealed class Row
        {
            public Transform Root, PillarL, PillarR, Beam;
            public readonly Transform[] Hammers = new Transform[HammersPerRow];
            public readonly float[] Lateral = new float[HammersPerRow];
            public readonly float[] Phase = new float[HammersPerRow];
            public int Count;
            public float Progress;
            public HazardPoint Point;
        }

        readonly Row[] rows = new Row[Rows];
        readonly Dictionary<CarController, float> lastHit = new Dictionary<CarController, float>();
        TelegraphMarker marker;
        float period;
        float swingGrow;

        void Awake()
        {
            for (int r = 0; r < Rows; r++)
            {
                var row = new Row { Root = Child("Row_" + r) };
                row.PillarL = row.Root.Find("PillarL");
                row.PillarR = row.Root.Find("PillarR");
                row.Beam = row.Root.Find("Beam");
                for (int k = 0; k < HammersPerRow; k++) row.Hammers[k] = row.Root.Find("Hammer_" + k);
                row.Root.gameObject.SetActive(false);
                rows[r] = row;
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            lastHit.Clear();
            float length = Context.Track.Length;
            period = RandomRange(2.3f, 3.1f);
            var map = Context.Race.Map;
            float pillarOut = Mathf.Min(RoadHalf + 1.6f, map != null ? Mathf.Max(RoadHalf + 0.8f, map.Roadside) : RoadHalf + 1.6f);
            for (int r = 0; r < Rows; r++)
            {
                var row = rows[r];
                row.Progress = Progress + (r - (Rows - 1) * 0.5f) * RowSpacing / length;
                row.Point = At(row.Progress, 0f);
                row.Root.gameObject.SetActive(true);
                row.Root.SetPositionAndRotation(row.Point.Position - Vector3.up * Sink, Quaternion.LookRotation(row.Point.Forward));
                row.PillarL.localPosition = new Vector3(-pillarOut, 0f, 0f);
                row.PillarR.localPosition = new Vector3(pillarOut, 0f, 0f);
                row.Beam.localPosition = new Vector3(0f, 10.2f, 0f);
                row.Beam.localScale = new Vector3(pillarOut * 2f, 1f, 1f);
                // Широкая дорога — два молота в противофазе, узкая — один по центру.
                row.Count = RoadHalf > 8f ? 2 : 1;
                for (int k = 0; k < HammersPerRow; k++)
                {
                    bool on = k < row.Count;
                    row.Hammers[k].gameObject.SetActive(on);
                    if (!on) continue;
                    // Узкая дорога: молот смещён к краю (у рядов — к разным), с другой стороны всегда ≥ 4 м проезда.
                    row.Lateral[k] = row.Count == 1 ? (r % 2 == 0 ? -1f : 1f) * RoadHalf * 0.35f : (k == 0 ? -1f : 1f) * RoadHalf * 0.45f;
                    // Сдвиг на четверть периода: когда один молот внизу, второй — на краю размаха (высоко),
                    // проезд под ним открыт. В противофазе оба проходили бы низ одновременно.
                    row.Phase[k] = k * Mathf.PI * 0.5f + r * RandomRange(0.6f, 1.6f);
                    row.Hammers[k].localPosition = new Vector3(row.Lateral[k], PivotHeight, 0f);
                }
            }
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = 2.6f;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.8f, false, false, (RowSpacing + 8f) * 0.5f / (RoadHalf * 0.8f));
            swingGrow = 0f;
        }

        float Angle(Row row, int k) =>
            Amplitude * swingGrow * Mathf.Sin(EventTime * Mathf.PI * 2f / period + row.Phase[k]);

        void Swing()
        {
            foreach (var row in rows)
                for (int k = 0; k < row.Count; k++)
                    row.Hammers[k].localRotation = Quaternion.AngleAxis(Angle(row, k), Vector3.forward);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01(progress / 0.55f));
            foreach (var row in rows) row.Root.position = row.Point.Position - Vector3.up * Sink * (1f - rise);
            swingGrow = Mathf.Clamp01((progress - 0.4f) / 0.6f);
            Swing();
        }

        protected override void OnActivate()
        {
            marker.Hide();
            swingGrow = 1f;
            foreach (var row in rows) row.Root.position = row.Point.Position;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            Swing();
            foreach (var row in rows)
            {
                for (int k = 0; k < row.Count; k++)
                {
                    var hammer = row.Hammers[k];
                    Vector3 head = hammer.TransformPoint(new Vector3(0f, -ArmLength, 0f));
                    float height = head.y - row.Point.Position.y;
                    if (height > 2.9f) continue;
                    // Направление удара — по ходу молота (знак скорости угла).
                    float w = Mathf.Cos(EventTime * Mathf.PI * 2f / period + row.Phase[k]);
                    Vector3 hitDir = row.Root.right * (w >= 0f ? 1f : -1f);
                    foreach (var racer in RacersNear(head, HeadRadius + 1.3f))
                    {
                        if (Mathf.Abs(racer.Car.position.y + 0.8f - head.y) > HeadRadius + 1.2f) continue;
                        lastHit.TryGetValue(racer.Controller, out float at);
                        if (Time.time - at < 1f) continue;
                        lastHit[racer.Controller] = Time.time;
                        // Молот отправляет машину в полёт вбок и вверх, с «бочкой» вокруг продольной оси.
                        float sign = w >= 0f ? 1f : -1f;
                        if (racer.Controller.Physics.Simulated)
                            racer.Controller.ApplyLaunch(hitDir * 27f + row.Root.forward * 4f + Vector3.up * 14f,
                                racer.Car.forward * -sign * 9f + Vector3.up * sign * 3f, "удар молотом");
                        ChaosEffects?.Blast(head, BlastStyle.Spark, 1.6f);
                        ChaosEffects?.Blast(head, BlastStyle.Mini, 1f);
                        ChaosEffects?.Flash(head, new Color(1f, 0.9f, 0.6f), 6f, 0.15f);
                        ChaosEffects?.Sound(SoundBank.Whack, head, 1f, Random.Range(0.75f, 0.95f));
                        ChaosEffects?.Pop(head + Vector3.up * 1.5f, "БА-БАМ!", new Color(1f, 0.35f, 0.25f), 1.6f);
                        ShakeCameraNear(head, 0.6f, 30f);
                    }
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            swingGrow = 1f - progress;
            Swing();
            foreach (var row in rows) row.Root.position = row.Point.Position - Vector3.up * Sink * progress * progress;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var row in rows) row.Root.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            foreach (var row in rows)
                for (int k = 0; k < row.Count; k++)
                    list.Add(new HazardInfo
                    {
                        Progress = row.Progress,
                        Center = row.Point.Position + row.Point.Right * row.Lateral[k],
                        Radius = 1.6f,
                        HalfLength = 2.5f,
                        Good = false,
                        Obstacle = true,
                        Since = StartedAt,
                    });
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            foreach (var row in rows)
                for (int k = 0; k < row.Count; k++)
                {
                    // Перекрывает дорогу только боёк внизу дуги (у машин), там, где он сейчас.
                    var hammer = row.Hammers[k];
                    Vector3 head = hammer.TransformPoint(new Vector3(0f, -ArmLength, 0f));
                    if (head.y - row.Point.Position.y > 2.9f) continue;
                    float lat = Vector3.Dot(head - row.Point.Position, row.Point.Right);
                    list.Add(new RoadBlock { Progress = row.Progress, HalfLength = 2f, LateralMin = lat - HeadRadius - 1f, LateralMax = lat + HeadRadius + 1f });
                }
        }
    }
}
