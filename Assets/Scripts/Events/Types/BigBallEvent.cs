using System.Collections.Generic;
using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Шар-переросток (только «Безумие»): огромный полосатый шар высотой с дом выкатывается навстречу гонке
    /// и катится по трассе, переваливаясь между полосами. Масса 9 т: мячи, бочки и мины разлетаются,
    /// машины отскакивают (с пределом удара). Шар занимает не больше трети ширины дороги — объехать можно всегда.
    /// В конце сдувается с обиженным «пшиком».
    public sealed class BigBallEvent : RaceEvent
    {
        ChaosBody ball;
        TelegraphMarker marker;
        float radius;
        float rumbleAt;
        float weave;

        void Awake()
        {
            ball = Child("Giant_0").GetComponent<ChaosBody>();
            ball.gameObject.SetActive(false);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            radius = Mathf.Clamp(RoadHalf * 0.33f, 2f, 3.8f);
            weave = RandomRange(0f, 10f);
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = radius;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.9f, false, true, ZoneLength * 0.5f / (RoadHalf * 0.9f));
            ChaosEffects?.Sound(SoundBank.Gong, Center, 0.6f, 0.5f);
        }

        protected override void OnTelegraphUpdate(float progress, float time) => marker.Pulse(progress);

        protected override void OnActivate()
        {
            marker.Hide();
            var h = At(Progress + ZoneLength * 0.4f / Context.Track.Length, RandomRange(-0.3f, 0.3f) * RoadHalf);
            ball.transform.localScale = Vector3.one * radius;
            ball.NetId = BodyId(0);
            ball.Cruise = 8.5f;
            ball.TargetLateral = Lateral(h.Position, h.Progress);
            Vector3 start = h.Position + Vector3.up * (radius + 0.2f);
            ball.Spawn(Context.Track, RoadHalf, start, Quaternion.identity, -h.Forward * 8.5f, h.Right * (8.5f / radius), !Authority);
            ChaosEffects?.Blast(start, BlastStyle.ColorPuff, 2.2f, new Color(0.75f, 0.55f, 1f, 0.7f));
            ChaosEffects?.Pop(start + Vector3.up * radius * 1.5f, "ОГРОМНЫЙ!", new Color(0.8f, 0.5f, 1f), 1.6f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (!ball.Live) return;
            // Переваливается между полосами, но не дальше, чем оставляет проезд с другой стороны.
            ball.TargetLateral = Mathf.Sin(EventTime * 0.45f + weave) * (RoadHalf - radius - 1f) * 0.6f;
            Center = ball.transform.position;
            if (Time.time >= rumbleAt)
            {
                rumbleAt = Time.time + 1.1f;
                ChaosEffects?.Sound(SoundBank.Gong, Center, 0.35f, 0.45f);
                ShakeCameraNear(Center, 0.25f, 35f);
            }
            if (ball.Lost)
            {
                ChaosEffects?.Blast(ball.transform.position, BlastStyle.ColorPuff, 2f);
                ball.Despawn();
            }
        }

        protected override void OnEnd()
        {
            if (!ball.Live) return;
            ChaosEffects?.Sound(SoundBank.Pshik, ball.transform.position, 1f, 0.6f);
            ChaosEffects?.Pop(ball.transform.position + Vector3.up * radius, "ПФФФ!", new Color(0.8f, 0.6f, 1f), 1.4f);
        }

        protected override void OnEndUpdate(float progress)
        {
            if (ball.Live) ball.transform.localScale = Vector3.one * radius * Mathf.Max(0.05f, 1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            ball.Despawn();
            ball.transform.localScale = Vector3.one;
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage == EventStage.Telegraph)
            {
                base.CollectHazards(list);
                return;
            }
            if (Stage != EventStage.Active || !ball.Live) return;
            Vector3 p = ball.transform.position;
            list.Add(new HazardInfo
            {
                Progress = Context.Track.Nearest(p),
                Center = p,
                Radius = radius + 0.6f,
                HalfLength = radius,
                Good = false,
                Obstacle = true,
                Since = StartedAt,
                Velocity = ball.Velocity,
            });
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active || !ball.Live) return;
            Vector3 p = ball.transform.position;
            float progress = Context.Track.Nearest(p);
            float lat = Lateral(p, progress);
            list.Add(new RoadBlock { Progress = progress, HalfLength = radius, LateralMin = lat - radius, LateralMax = lat + radius });
        }
    }
}
