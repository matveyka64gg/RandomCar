using System.Collections.Generic;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Перекрытие: «дорожные работы» вырастают из асфальта шиканой — сначала закрыта одна половина дороги,
    /// через несколько метров другая. Объезд змейкой: сбросить скорость или пройти заносом с ручником.
    /// Каждая секция — настоящее препятствие (коллайдер), вторая половина дороги всегда свободна.
    /// Метки и стрелки объезда видны заранее; держится, пока игрок не проедет.
    public sealed class RoadblockEvent : RaceEvent
    {
        public const int Sections = 2;
        public const int BarriersPerSection = 4;
        public const int ConesPerSection = 5;
        const float Sink = 1.3f;
        const float SectionLength = 12f;
        /// Между секциями — место для перекладки: на 70–80 км/ч проходится, на полном ходу — только с тормозом.
        const float SectionGap = 20f;

        sealed class Section
        {
            public Transform Root;
            public Transform Board;
            public readonly Transform[] Barriers = new Transform[BarriersPerSection];
            public readonly Transform[] Cones = new Transform[ConesPerSection];
            public BoxCollider Blocker;
            public TelegraphMarker Marker;
            public float Side;
            public float Progress;
            public Vector3 Center;
        }

        readonly Section[] sections = new Section[Sections];
        readonly Dictionary<CarController, float> nextPush = new Dictionary<CarController, float>();
        ParticleSystem dust;

        public override bool IsObstacle => true;

        void Awake()
        {
            for (int s = 0; s < Sections; s++)
            {
                var section = new Section { Root = Child("Section_" + s) };
                section.Board = section.Root.Find("Board");
                for (int i = 0; i < BarriersPerSection; i++) section.Barriers[i] = section.Root.Find("Barrier_" + i);
                for (int i = 0; i < ConesPerSection; i++) section.Cones[i] = section.Root.Find("Cone_" + i);
                section.Blocker = section.Root.GetComponent<BoxCollider>();
                section.Marker = Child(s == 0 ? "Marker" : "Marker_" + s).GetComponent<TelegraphMarker>();
                sections[s] = section;
            }
            dust = Fx("FxDust");
        }

        protected override void OnTelegraph()
        {
            nextPush.Clear();
            float length = Context.Track.Length;
            float first = Placement.Lateral >= 0f ? 1f : -1f;
            // Закрыта бо́льшая часть одной половины; рядом с блоками остаётся проезд шире машины.
            float closed = RoadHalf * 0.8f;
            Radius = closed * 0.5f;
            for (int s = 0; s < Sections; s++)
            {
                var section = sections[s];
                section.Side = s == 0 ? first : -first;
                section.Progress = Progress + (s == 0 ? -1f : 1f) * (SectionLength + SectionGap) * 0.5f / length;
                var h = At(section.Progress, section.Side * (RoadHalf - closed * 0.5f));
                section.Center = h.Position;
                var rotation = Quaternion.LookRotation(h.Forward);
                section.Root.gameObject.SetActive(true);
                section.Root.SetPositionAndRotation(h.Position - Vector3.up * Sink, rotation);
                // Блоки — по линии закрытия (со стороны открытой половины), конусы — веером по закрытой части,
                // щит со стрелкой — в начале секции, стрелка показывает объезд в открытую сторону.
                float lineX = -section.Side * closed * 0.5f;
                for (int i = 0; i < BarriersPerSection; i++)
                {
                    float z = -SectionLength * 0.5f + 1.5f + i * 3f;
                    section.Barriers[i].localPosition = new Vector3(lineX, 0f, z);
                    section.Barriers[i].localRotation = Quaternion.identity;
                }
                // Конусы — «клин» перед секцией: от кромки дороги к линии закрытия, машину плавно уводит в сторону.
                for (int i = 0; i < ConesPerSection; i++)
                {
                    float t = (i + 0.5f) / ConesPerSection;
                    float x = Mathf.Lerp(section.Side * closed * 0.5f, lineX, t);
                    float z = Mathf.Lerp(-SectionLength * 0.5f - 7f, -SectionLength * 0.5f, t);
                    section.Cones[i].localPosition = new Vector3(x, 0f, z);
                }
                // Щит смотрит навстречу машинам (−z), стрелка модели — вправо; в другую сторону — зеркально.
                section.Board.localPosition = new Vector3(section.Side * closed * 0.15f, 0f, -SectionLength * 0.5f + 0.6f);
                section.Board.localRotation = Quaternion.identity;
                section.Board.localScale = new Vector3(-section.Side, 1f, 1f);
                section.Blocker.center = new Vector3(0f, 0.6f, 0f);
                section.Blocker.size = new Vector3(closed, 1.2f, SectionLength);
                section.Blocker.enabled = false;
                section.Marker.Show(section.Center, h.Forward, Radius, false, true, SectionLength * 0.5f / Radius);
            }
            Center = At(Progress, 0f).Position;
            Play(dust, Center);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            float rise = EaseOutBack(Mathf.Clamp01((progress - 0.35f) / 0.5f));
            foreach (var section in sections)
            {
                section.Marker.Pulse(progress);
                section.Root.position = section.Center - Vector3.up * Sink * (1f - rise);
            }
        }

        protected override void OnActivate()
        {
            foreach (var section in sections)
            {
                section.Root.position = section.Center;
                section.Blocker.enabled = true;
                section.Marker.Hide();
            }
            ShakeCameraNear(Center, 0.2f, 30f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            // Кто упёрся в блоки или застрял в очереди перед секцией, того мягко выталкиваем в открытую половину
            // и вперёд: никто не стоит дольше секунды, затор из нескольких машин рассасывается.
            foreach (var section in sections)
            {
                foreach (var racer in RacersNear(section.Center, SectionLength * 0.5f + 9f))
                {
                    if (Mathf.Abs(racer.Controller.Speed) > 3f || !racer.Controller.Physics.Simulated) continue;
                    nextPush.TryGetValue(racer.Controller, out float at);
                    if (Time.time < at) continue;
                    nextPush[racer.Controller] = Time.time + 0.6f;
                    racer.Controller.ApplyImpulse(-section.Side * section.Root.right * 6f + section.Root.forward * 3f + Vector3.up * 1f);
                }
            }
        }

        protected override void OnEnd()
        {
            foreach (var section in sections) section.Blocker.enabled = false;
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var section in sections) section.Root.position = section.Center - Vector3.up * Sink * progress;
        }

        protected override void Cleanup()
        {
            foreach (var section in sections)
            {
                section.Marker.Hide();
                section.Blocker.enabled = false;
                section.Root.gameObject.SetActive(false);
            }
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            foreach (var section in sections)
                list.Add(new HazardInfo
                {
                    Progress = section.Progress,
                    Center = section.Center,
                    Radius = Radius,
                    HalfLength = SectionLength * 0.5f,
                    Good = false,
                    Obstacle = true,
                    Since = StartedAt,
                });
        }
    }
}
