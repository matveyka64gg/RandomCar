using System.Collections.Generic;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Развилка: по оси дороги вырастает разделитель из бетонных блоков, перед ним щиты со стрелками в обе стороны.
    /// Одна половина — «риск»: слалом мин, а за ними два ускорителя. Другая — «спокойно»: без мин, но со льдом.
    /// Выбор за водителем (и за каждым ИИ). Обе половины проезжаемы.
    public sealed class ForkEvent : RaceEvent
    {
        public const int Barriers = 10, Mines = 3, Pads = 2;
        const float BarrierLength = 3f;
        const float Sink = 1.4f;

        readonly Transform[] barriers = new Transform[Barriers];
        readonly Collider[] barrierColliders = new Collider[Barriers];
        readonly Transform[] boards = new Transform[2];
        Collider boardBlock;
        readonly Transform[] pads = new Transform[Pads];
        readonly Vector3[] padPositions = new Vector3[Pads];
        readonly float[] padProgress = new float[Pads];
        readonly HashSet<CarController> boosted = new HashSet<CarController>();
        readonly HashSet<CarController> slipped = new HashSet<CarController>();
        MineField field;
        Transform ice;
        TelegraphMarker marker;
        float riskSide;
        float dividerProgress;
        readonly Vector3[] barrierHomes = new Vector3[Barriers];
        readonly Vector3[] boardHomes = new Vector3[2];

        void Awake()
        {
            for (int i = 0; i < Barriers; i++)
            {
                barriers[i] = Child("Barrier_" + i);
                barrierColliders[i] = barriers[i].GetComponent<Collider>();
            }
            for (int i = 0; i < 2; i++) boards[i] = Child("Board_" + i);
            boardBlock = Child("BoardBlock").GetComponent<Collider>();
            for (int i = 0; i < Pads; i++) pads[i] = Child("Pad_" + i);
            field = new MineField(this, (code, index, value) =>
            {
                Signal(code, index, value);
                return true;
            });
            for (int i = 0; i < Mines; i++) field.Bind(Child("Mine_" + i));
            ice = Child("Ice_0");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            Show(false);
        }

        void Show(bool on)
        {
            foreach (var b in barriers) b.gameObject.SetActive(on);
            foreach (var b in boards) b.gameObject.SetActive(on);
            foreach (var p in pads) p.gameObject.SetActive(on);
            ice.gameObject.SetActive(on);
        }

        public override bool IsObstacle => true;

        protected override void OnTelegraph()
        {
            boosted.Clear();
            slipped.Clear();
            float length = Context.Track.Length;
            riskSide = Placement.Lateral >= 0f ? 1f : -1f;
            Show(true);
            dividerProgress = Progress;
            for (int i = 0; i < Barriers; i++)
            {
                float along = (i - (Barriers - 1) * 0.5f) * BarrierLength;
                var h = At(Progress + along / length, 0f);
                barrierHomes[i] = h.Position;
                barriers[i].SetPositionAndRotation(h.Position - Vector3.up * Sink, Quaternion.LookRotation(h.Forward));
                if (barrierColliders[i] != null) barrierColliders[i].enabled = false;
            }
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                // Щиты — в голове разделителя, вплотную друг к другу: «налево» и «направо».
                var h = At(Progress - (Barriers * BarrierLength * 0.5f + 1f) / length, s * 0.88f);
                boardHomes[i] = h.Position;
                boards[i].SetPositionAndRotation(h.Position - Vector3.up * 3f, Quaternion.LookRotation(h.Forward));
                // Стрелка модели — вправо (+x); левый щит зеркальный.
                boards[i].localScale = new Vector3(s * 0.8f, 0.8f, 0.8f);
            }
            // Щиты зеркальные (отрицательный масштаб), поэтому столкновение — отдельной коробкой без масштаба.
            var head = At(Progress - (Barriers * BarrierLength * 0.5f + 1f) / length, 0f);
            boardBlock.transform.SetPositionAndRotation(head.Position + Vector3.up * 0.9f, Quaternion.LookRotation(head.Forward));
            boardBlock.enabled = false;
            // «Риск»: мины у краёв полосы по очереди (слалом), за ними — два ускорителя.
            float laneInner = 0.9f, laneOuter = RoadHalf - 0.9f;
            for (int i = 0; i < Mines; i++)
            {
                float along = -10f + i * 9f;
                float lat = riskSide * (i % 2 == 0 ? laneInner + 0.9f : laneOuter - 0.9f);
                var h = At(Progress + along / length, lat);
                var type = RandomRange(0f, 1f) < 0.2f ? MineType.Fake : MineType.Real;
                field.Place(i, Ground(h.Position) + Vector3.up * 0.02f, h.Right, h.Forward, Progress + along / length, lat, type, RandomRange(0f, 1f));
            }
            for (int i = 0; i < Pads; i++)
            {
                float along = 14f + i * 6f;
                padProgress[i] = Progress + along / length;
                var h = At(padProgress[i], riskSide * (laneInner + laneOuter) * 0.5f);
                padPositions[i] = h.Position;
                pads[i].SetPositionAndRotation(h.Position + Vector3.up * 0.02f, Quaternion.LookRotation(h.Forward));
                pads[i].localScale = Vector3.zero;
            }
            // «Спокойно»: лёд по всей спокойной полосе вдоль разделителя.
            var iceAt = At(Progress, -riskSide * (laneInner + laneOuter) * 0.5f);
            ice.SetPositionAndRotation(iceAt.Position + Vector3.up * 0.02f, Quaternion.LookRotation(iceAt.Forward));
            ice.localScale = Vector3.zero;
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = 1f;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.9f, false, true, ZoneLength * 0.5f / (RoadHalf * 0.9f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01(progress / 0.6f));
            for (int i = 0; i < Barriers; i++) barriers[i].position = barrierHomes[i] - Vector3.up * Sink * (1f - EaseOutBack(Mathf.Clamp01(progress * 1.4f - i * 0.04f)));
            for (int i = 0; i < 2; i++) boards[i].position = boardHomes[i] - Vector3.up * 3f * (1f - rise);
            field.Rise(progress, Mines);
            float k = Mathf.Clamp01((progress - 0.3f) / 0.5f);
            foreach (var p in pads) p.localScale = Vector3.one * k;
            ice.localScale = new Vector3((RoadHalf - 1.8f) * k, 1f, Barriers * BarrierLength * k);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            for (int i = 0; i < Barriers; i++)
            {
                barriers[i].position = barrierHomes[i];
                if (barrierColliders[i] != null) barrierColliders[i].enabled = true;
            }
            boardBlock.enabled = true;
            field.Arm(Mines);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            field.Update(ActiveElapsed, Mines, Racers, Authority);
            float length = Context.Track.Length;
            foreach (var racer in Racers)
            {
                Locate(racer, out float along, out float lat);
                // Ускорители на «рисковой» половине.
                for (int i = 0; i < Pads; i++)
                {
                    float a = (padProgress[i] - Progress) * length;
                    if (Mathf.Abs(along - a) > 2.5f || Mathf.Sign(lat) != riskSide || boosted.Contains(racer.Controller)) continue;
                    boosted.Add(racer.Controller);
                    if (racer.Controller.Physics.Simulated) racer.Controller.ApplyBoost(FairnessLimits.MaxBoostFactor);
                    ChaosEffects?.Pop(racer.Car.position + Vector3.up * 2.5f, "РИСК ОКУПИЛСЯ!", new Color(0.3f, 1f, 0.8f), 1f);
                }
                // Лёд на «спокойной» половине.
                if (Mathf.Abs(along) < Barriers * BarrierLength * 0.5f && Mathf.Sign(lat) == -riskSide && Mathf.Abs(lat) > 1.2f)
                {
                    if (racer.Controller.Physics.Simulated && !racer.Controller.IsSlipping) racer.Controller.ApplySlip(0.6f);
                    if (slipped.Add(racer.Controller)) ChaosEffects?.Sound(Audio.SoundBank.IceCrack, racer.Car.position, 0.6f, 1f);
                }
            }
        }

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if (code == MineField.SignalTrigger) field.Trigger(index);
        }

        protected override void OnEnd()
        {
            foreach (var c in barrierColliders) if (c != null) c.enabled = false;
            boardBlock.enabled = false;
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < Barriers; i++) barriers[i].position = barrierHomes[i] - Vector3.up * Sink * progress;
            for (int i = 0; i < 2; i++) boards[i].position = boardHomes[i] - Vector3.up * 3f * progress;
            foreach (var p in pads) p.localScale = Vector3.one * (1f - progress);
            ice.localScale = new Vector3((RoadHalf - 1.8f) * (1f - progress), 1f, Barriers * BarrierLength * (1f - progress));
            field.Rise(1f - progress, Mines);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var c in barrierColliders) if (c != null) c.enabled = false;
            boardBlock.enabled = false;
            field.Clear();
            Show(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            // Разделитель — стена по оси; мины, лёд и ускорители — по своим местам: ИИ выбирает половину сам.
            list.Add(new HazardInfo { Progress = dividerProgress, Center = Center, Radius = 1.7f, HalfLength = Barriers * BarrierLength * 0.5f + 2f, Good = false, Obstacle = true, Since = StartedAt });
            field.CollectHazards(list, Mines, StartedAt);
            for (int i = 0; i < Pads; i++)
                list.Add(new HazardInfo { Progress = padProgress[i], Center = padPositions[i], Radius = 1.5f, HalfLength = 2f, Good = true, Since = StartedAt });
            list.Add(new HazardInfo { Progress = Progress, Center = ice.position, Radius = (RoadHalf - 1.8f) * 0.5f, HalfLength = Barriers * BarrierLength * 0.5f, Good = false, Soft = true, Since = StartedAt });
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            list.Add(new RoadBlock { Progress = dividerProgress, HalfLength = Barriers * BarrierLength * 0.5f + 2f, LateralMin = -1.7f, LateralMax = 1.7f });
            field.CollectBlocks(list, Mines);
        }
    }
}
