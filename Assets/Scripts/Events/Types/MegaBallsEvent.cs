using System.Collections.Generic;
using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Шары-гиганты: настоящие физические шары (масса, вращение, отскоки, столкновения с машинами и друг с другом)
    /// выкатываются на дорогу, когда игрок подъезжает. Варианты по сиду:
    /// пляжные (лёгкие, виляют между полосами), стальной (тяжёлый, быстрый, по прямой, отскакивает от бортиков),
    /// резиновые (падают с неба и скачут), рой разноцветных мячиков, «горка» (шары высыпаются с вышки у обочины).
    /// Шары катятся навстречу, поперёк или попутно; держатся, пока игрок не проедет участок.
    public sealed class MegaBallsEvent : RaceEvent
    {
        public const int Beach = 2, Metal = 1, Rubber = 2, Mini = 5;

        enum Variant { Beach, Metal, Rubber, Swarm, Tower }

        readonly List<ChaosBody> beach = new List<ChaosBody>();
        readonly List<ChaosBody> rubber = new List<ChaosBody>();
        readonly List<ChaosBody> minis = new List<ChaosBody>();
        readonly List<ChaosBody> live = new List<ChaosBody>();
        readonly List<(ChaosBody body, float at, Vector3 position, Vector3 velocity)> queue = new List<(ChaosBody, float, Vector3, Vector3)>();
        ChaosBody metal;
        TelegraphMarker marker;
        Transform tower;
        Transform bin;
        Variant variant;
        HazardPoint spot;
        float side;
        float activeStart;

        void Awake()
        {
            for (int i = 0; i < Beach; i++) beach.Add(Body("Beach_" + i));
            metal = Body("Metal_0");
            for (int i = 0; i < Rubber; i++) rubber.Add(Body("Rubber_" + i));
            for (int i = 0; i < Mini; i++) minis.Add(Body("Mini_" + i));
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            tower = Child("Tower");
            bin = tower != null ? tower.Find("Bin") : null;
        }

        ChaosBody Body(string name)
        {
            var b = Child(name).GetComponent<ChaosBody>();
            b.gameObject.SetActive(false);
            return b;
        }

        protected override void OnTelegraph()
        {
            live.Clear();
            queue.Clear();
            float roll = RandomRange(0f, 1f);
            variant = roll < 0.24f ? Variant.Beach : roll < 0.42f ? Variant.Metal : roll < 0.62f ? Variant.Rubber : roll < 0.84f ? Variant.Swarm : Variant.Tower;
            side = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            spot = At(Progress, 0f);
            Center = spot.Position;
            Radius = MaxRadius;
            marker.Show(spot.Position, spot.Forward, RoadHalf * 0.85f, false, true, ZoneLength * 0.5f / (RoadHalf * 0.85f));
            if (variant == Variant.Tower)
            {
                // Вышка у обочины: шары высыпаются из ковша и скатываются на дорогу.
                var h = At(Progress + 12f / Context.Track.Length, 0f);
                tower.gameObject.SetActive(true);
                Vector3 foot = Roadside(h, side, RoadHalf + 2.5f);
                tower.SetPositionAndRotation(foot - Vector3.up * 8f, Quaternion.LookRotation(h.Forward) * Quaternion.Euler(0f, side > 0f ? 180f : 0f, 0f));
                if (bin != null) bin.localRotation = Quaternion.identity;
            }
            ChaosEffects?.Sound(SoundBank.Boing, Center, 0.5f, 0.8f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            if (variant == Variant.Tower)
            {
                var h = At(Progress + 12f / Context.Track.Length, 0f);
                Vector3 foot = Roadside(h, side, RoadHalf + 2.5f);
                tower.position = foot - Vector3.up * 8f * (1f - EaseOutBack(Mathf.Clamp01(progress / 0.6f)));
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            activeStart = Time.time;
            float length = Context.Track.Length;
            Vector3 fwd = spot.Forward, right = spot.Right;
            switch (variant)
            {
                case Variant.Beach:
                    // Два пляжных мяча навстречу, виляют между полосами.
                    for (int i = 0; i < beach.Count; i++)
                    {
                        var h = At(Progress + (14f + i * 9f) / length, RandomRange(-RoadHalf * 0.6f, RoadHalf * 0.6f));
                        Queue(beach[i], i * 0.5f, h.Position + Vector3.up * 1.6f, -fwd * RandomRange(5f, 8f) + right * RandomRange(-2f, 2f), 7f);
                    }
                    break;
                case Variant.Metal:
                    {
                        // Стальной шар — быстро и прямо, наискось через дорогу; к нему пляжный для смеха.
                        var h = At(Progress + 22f / length, side * RoadHalf * 0.7f);
                        Queue(metal, 0f, h.Position + Vector3.up * 1.3f, -fwd * RandomRange(9f, 12f) - right * side * RandomRange(2.5f, 4f), 0f);
                        var b = At(Progress + 5f / length, -side * RoadHalf * 0.4f);
                        Queue(beach[0], 0.6f, b.Position + Vector3.up * 1.6f, -fwd * 4f, 6f);
                        break;
                    }
                case Variant.Rubber:
                    // Резиновые падают с неба и скачут по дороге навстречу.
                    for (int i = 0; i < rubber.Count; i++)
                    {
                        var h = At(Progress + (10f + i * 12f) / length, RandomRange(-RoadHalf * 0.6f, RoadHalf * 0.6f));
                        Queue(rubber[i], i * 0.45f, h.Position + Vector3.up * RandomRange(9f, 13f), -fwd * RandomRange(4f, 7f) + right * RandomRange(-1.5f, 1.5f), 0f);
                    }
                    break;
                case Variant.Swarm:
                    // Рой разноцветных мячиков с обочины поперёк дороги.
                    for (int i = 0; i < minis.Count; i++)
                    {
                        // Медленно поперёк и навстречу: к подъезду игрока мячики посреди дороги.
                        var h = At(Progress + RandomRange(-6f, 24f) / length, side * (RoadHalf + 1.5f));
                        Queue(minis[i], i * 0.25f, h.Position + Vector3.up * RandomRange(1.5f, 3f), -right * side * RandomRange(3f, 5f) - fwd * RandomRange(2f, 5f), 4f);
                    }
                    break;
                case Variant.Tower:
                    {
                        // Ковш опрокидывается: два резиновых и два мячика скатываются с высоты на дорогу.
                        Vector3 top = tower.position + Vector3.up * 7.2f;
                        for (int i = 0; i < 2; i++)
                            Queue(rubber[i], 0.3f + i * 0.4f, top + fwd * (i - 0.5f), -right * side * RandomRange(7f, 10f) - fwd * 2f, 0f);
                        for (int i = 0; i < 2; i++)
                            Queue(minis[i], 0.5f + i * 0.35f, top + fwd * (i * 0.8f), -right * side * RandomRange(6f, 9f) - fwd * 1f, 2f);
                        ChaosEffects?.Sound(SoundBank.Knock, top, 0.7f, 0.8f);
                        break;
                    }
            }
        }

        void Queue(ChaosBody body, float delay, Vector3 position, Vector3 velocity, float wobble)
        {
            body.Wobble = wobble;
            queue.Add((body, Time.time + delay, position, velocity));
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (variant == Variant.Tower && bin != null)
                bin.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Clamp01((Time.time - activeStart) / 0.5f) * 65f);
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var q = queue[i];
                if (Time.time < q.at) continue;
                queue.RemoveAt(i);
                q.body.NetId = BodyId(live.Count);
                Vector3 spin = Vector3.Cross(Vector3.up, q.velocity.normalized) * (q.velocity.magnitude / Mathf.Max(0.5f, q.body.Radius));
                // Шары-гиганты: пляжный ~4.4 м в поперечнике, резиновый ~3.5, стальной ~3.2, мячики роя ~2.4.
                q.body.transform.localScale = Vector3.one * (q.body.Kind == BodyKind.Beach ? 1.45f : q.body.Kind == BodyKind.Mini ? 1.3f : 1.35f);
                q.body.Spawn(Context.Track, RoadHalf, q.position, Random.rotation, q.velocity, spin, !Authority);
                live.Add(q.body);
                ChaosEffects?.Blast(q.position, BlastStyle.ColorPuff, 0.4f, new Color(1f, 0.95f, 0.8f, 0.6f));
            }
            // Потерянный (улетел с карты) шар исчезает с облачком.
            foreach (var b in live)
            {
                if (!b.Live || !b.Lost) continue;
                ChaosEffects?.Blast(b.transform.position, BlastStyle.ColorPuff, 0.8f);
                b.Despawn();
            }
        }

        protected override void OnEnd()
        {
            foreach (var b in live)
                if (b.Live) ChaosEffects?.Blast(b.transform.position, BlastStyle.Fake, 0.8f, new Color(1f, 0.8f, 0.9f, 0.8f));
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var b in live)
                if (b.Live) b.transform.localScale = Vector3.one * Mathf.Max(0.05f, 1f - progress);
            if (variant == Variant.Tower) tower.position -= Vector3.up * Time.deltaTime * 10f;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var b in live)
            {
                b.transform.localScale = Vector3.one;
                b.Despawn();
            }
            live.Clear();
            queue.Clear();
            if (tower != null) tower.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage == EventStage.Telegraph)
            {
                base.CollectHazards(list);
                return;
            }
            if (Stage != EventStage.Active) return;
            foreach (var b in live)
            {
                if (!b.Live) continue;
                Vector3 p = b.transform.position;
                list.Add(new HazardInfo
                {
                    Progress = Context.Track.Nearest(p),
                    Center = p,
                    Radius = b.Radius + 0.4f,
                    HalfLength = b.Radius,
                    Good = false,
                    Obstacle = b.Kind == BodyKind.Metal,
                    Since = StartedAt,
                    Velocity = b.Velocity,
                });
            }
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            foreach (var b in live)
            {
                if (!b.Live) continue;
                Vector3 p = b.transform.position;
                float progress = Context.Track.Nearest(p);
                float lat = Lateral(p, progress);
                if (Mathf.Abs(lat) > RoadHalf + b.Radius) continue;
                list.Add(new RoadBlock { Progress = progress, HalfLength = b.Radius, LateralMin = lat - b.Radius, LateralMax = lat + b.Radius });
            }
        }

        /// Физические тела события на трассе сейчас.
        public int LiveBodies
        {
            get
            {
                int n = 0;
                foreach (var b in live) if (b.Live) n++;
                return n;
            }
        }
    }
}
