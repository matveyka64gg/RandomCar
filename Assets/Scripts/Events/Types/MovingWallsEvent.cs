using System.Collections.Generic;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Двигающиеся стены: три ряда мягких стен поперёк дороги вырастают из асфальта. В каждом ряду открыт проезд
    /// не уже max(5.4 м, трети дороги); стены раздвигаются и сдвигаются, и проезд плывёт от края к краю
    /// (у рядов разные фазы — получается слалом, иногда несколько машин ломятся в одну щель).
    /// Стены — настоящие препятствия (кинематические тела), застрявших выталкивает в проезд.
    public sealed class MovingWallsEvent : RaceEvent
    {
        public const int Rows = 3;
        const float RowSpacing = 18f;
        const float ModelLength = 4f;
        const float Sink = 1.6f;

        sealed class Row
        {
            public Transform Root, Left, Right;
            public Rigidbody LeftBody, RightBody;
            public BoxCollider LeftBox, RightBox;
            public float Progress;
            public HazardPoint Point;
            public float Phase;
            public float GapCenter;
        }

        readonly Row[] rows = new Row[Rows];
        readonly Dictionary<CarController, float> nextPush = new Dictionary<CarController, float>();
        TelegraphMarker marker;
        float gap;
        float period;

        void Awake()
        {
            for (int r = 0; r < Rows; r++)
            {
                var row = new Row { Root = Child("Row_" + r) };
                row.Left = row.Root.Find("WallL");
                row.Right = row.Root.Find("WallR");
                row.LeftBody = row.Left.GetComponent<Rigidbody>();
                row.RightBody = row.Right.GetComponent<Rigidbody>();
                row.LeftBox = row.Left.GetComponent<BoxCollider>();
                row.RightBox = row.Right.GetComponent<BoxCollider>();
                row.Root.gameObject.SetActive(false);
                rows[r] = row;
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        public override bool IsObstacle => true;

        protected override void OnTelegraph()
        {
            nextPush.Clear();
            float length = Context.Track.Length;
            gap = Mathf.Max(5.4f, RoadHalf * 2f * 0.34f);
            period = RandomRange(6f, 8f);
            float phase = RandomRange(0f, Mathf.PI * 2f);
            for (int r = 0; r < Rows; r++)
            {
                var row = rows[r];
                row.Progress = Progress + (r - (Rows - 1) * 0.5f) * RowSpacing / length;
                row.Point = At(row.Progress, 0f);
                row.Phase = phase + r * RandomRange(1.6f, 2.6f);
                row.Root.gameObject.SetActive(true);
                row.Root.SetPositionAndRotation(row.Point.Position - Vector3.up * Sink, Quaternion.LookRotation(row.Point.Forward));
                SetColliders(row, false);
                Layout(row);
            }
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = MaxRadius;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.9f, false, true, (RowSpacing * Rows) * 0.5f / (RoadHalf * 0.9f));
        }

        static void SetColliders(Row row, bool on)
        {
            row.LeftBox.enabled = on;
            row.RightBox.enabled = on;
        }

        /// Проезд сейчас: центр щели плавает по синусу от края до края.
        void Layout(Row row)
        {
            float amplitude = RoadHalf - gap * 0.5f - 0.3f;
            row.GapCenter = amplitude * Mathf.Sin(EventTime * Mathf.PI * 2f / period + row.Phase);
            float leftEnd = row.GapCenter - gap * 0.5f;
            float rightStart = row.GapCenter + gap * 0.5f;
            Stretch(row.Left, row.LeftBody, -RoadHalf, leftEnd);
            Stretch(row.Right, row.RightBody, rightStart, RoadHalf);
        }

        static void Stretch(Transform wall, Rigidbody body, float from, float to)
        {
            float span = Mathf.Max(0.05f, to - from);
            wall.localScale = new Vector3(span / ModelLength, 1f, 1f);
            Vector3 local = new Vector3((from + to) * 0.5f, 0f, 0f);
            if (body != null && body.isKinematic && wall.gameObject.activeInHierarchy) body.MovePosition(wall.parent.TransformPoint(local));
            else wall.localPosition = local;
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01((progress - 0.25f) / 0.55f));
            foreach (var row in rows)
            {
                row.Root.position = row.Point.Position - Vector3.up * Sink * (1f - rise);
                Layout(row);
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            foreach (var row in rows)
            {
                row.Root.position = row.Point.Position;
                SetColliders(row, true);
            }
            ShakeCameraNear(Center, 0.2f, 30f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var row in rows)
            {
                Layout(row);
                // Застрявших у стены — в проезд.
                foreach (var racer in RacersNear(row.Point.Position, RoadHalf + 2f))
                {
                    if (Mathf.Abs(Vector3.Dot(racer.Car.position - row.Point.Position, row.Point.Forward)) > 2.5f) continue;
                    if (Mathf.Abs(racer.Controller.Speed) > 3f || !racer.Controller.Physics.Simulated) continue;
                    nextPush.TryGetValue(racer.Controller, out float at);
                    if (Time.time < at) continue;
                    nextPush[racer.Controller] = Time.time + 0.6f;
                    float lat = Vector3.Dot(racer.Car.position - row.Point.Position, row.Point.Right);
                    racer.Controller.ApplyImpulse(row.Point.Right * Mathf.Sign(row.GapCenter - lat) * 5f + row.Point.Forward * 3f + Vector3.up);
                }
            }
        }

        protected override void OnEnd()
        {
            foreach (var row in rows) SetColliders(row, false);
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var row in rows) row.Root.position = row.Point.Position - Vector3.up * Sink * progress;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var row in rows)
            {
                SetColliders(row, false);
                row.Root.gameObject.SetActive(false);
            }
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            float speed = (RoadHalf - gap * 0.5f) * Mathf.PI * 2f / period;
            foreach (var row in rows)
            {
                float leftEnd = row.GapCenter - gap * 0.5f, rightStart = row.GapCenter + gap * 0.5f;
                float w = Mathf.Cos(EventTime * Mathf.PI * 2f / period + row.Phase) * speed;
                AddWall(list, row, -RoadHalf, leftEnd, w);
                AddWall(list, row, rightStart, RoadHalf, w);
            }
        }

        void AddWall(List<HazardInfo> list, Row row, float from, float to, float lateralSpeed)
        {
            if (to - from < 0.3f) return;
            list.Add(new HazardInfo
            {
                Progress = row.Progress,
                Center = row.Point.Position + row.Point.Right * (from + to) * 0.5f,
                Radius = (to - from) * 0.5f,
                HalfLength = 1f,
                Good = false,
                Obstacle = true,
                Since = StartedAt,
                Velocity = row.Point.Right * lateralSpeed,
            });
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            foreach (var row in rows)
            {
                list.Add(new RoadBlock { Progress = row.Progress, HalfLength = 0.5f, LateralMin = -RoadHalf, LateralMax = row.GapCenter - gap * 0.5f });
                list.Add(new RoadBlock { Progress = row.Progress, HalfLength = 0.5f, LateralMin = row.GapCenter + gap * 0.5f, LateralMax = RoadHalf });
            }
        }
    }
}
