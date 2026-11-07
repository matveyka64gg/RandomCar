using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Бочкопад: у обочины вырастает вышка с ковшом, ковш опрокидывается — бочки, взрывоопасные бочки «БАХ»
    /// и огромные кости кувыркаются на дорогу и катятся через неё (настоящая физика). Бочку «БАХ» взрывает
    /// сильный удар машины или чужой взрыв (цепная реакция с минами), кости остаются на дороге препятствием
    /// до конца события. Физику считает хост, взрыв бочки — его решение (сигнал всем).
    public sealed class RollersEvent : RaceEvent, IChaosReactive
    {
        public const int Barrels = 2, Tnt = 2, Dice = 2;
        const int SignalExplode = 2;

        readonly List<ChaosBody> bodies = new List<ChaosBody>();
        readonly List<ChaosBody> live = new List<ChaosBody>();
        readonly List<(ChaosBody body, float at, Vector3 velocity)> queue = new List<(ChaosBody, float, Vector3)>();
        TelegraphMarker marker;
        Transform tower;
        Transform bin;
        HazardPoint spot;
        HazardPoint towerPoint;
        float side;
        float activeStart;

        void Awake()
        {
            for (int i = 0; i < Barrels; i++) bodies.Add(Body("Barrel_" + i, false));
            for (int i = 0; i < Tnt; i++) bodies.Add(Body("Tnt_" + i, true));
            for (int i = 0; i < Dice; i++) bodies.Add(Body("Dice_" + i, false));
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            tower = Child("Tower");
            bin = tower.Find("Bin");
        }

        ChaosBody Body(string name, bool explosive)
        {
            var b = Child(name).GetComponent<ChaosBody>();
            b.Explosive = explosive;
            b.gameObject.SetActive(false);
            return b;
        }

        protected override void OnTelegraph()
        {
            live.Clear();
            queue.Clear();
            side = Placement.Lateral >= 0f ? 1f : -1f;
            spot = At(Progress, 0f);
            towerPoint = At(Progress + ZoneLength * 0.3f / Context.Track.Length, 0f);
            Center = spot.Position;
            Radius = MaxRadius;
            tower.gameObject.SetActive(true);
            if (bin != null) bin.localRotation = Quaternion.identity;
            PlaceTower(1f);
            marker.Show(spot.Position, spot.Forward, RoadHalf * 0.85f, false, true, ZoneLength * 0.5f / (RoadHalf * 0.85f));
            ChaosFx.Register(this);
        }

        void PlaceTower(float sunk)
        {
            Vector3 foot = Roadside(towerPoint, side, RoadHalf + 2.5f);
            tower.SetPositionAndRotation(foot - Vector3.up * 8f * sunk, Quaternion.LookRotation(towerPoint.Forward) * Quaternion.Euler(0f, side > 0f ? 180f : 0f, 0f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            PlaceTower(1f - EaseOutBack(Mathf.Clamp01(progress / 0.6f)));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            activeStart = Time.time;
            PlaceTower(0f);
            // Порядок высыпания по сиду: бочки, «БАХ» и кости вперемешку, с интервалом.
            var order = new List<int> { 0, 1, 2, 3, 4, 5 };
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Mathf.FloorToInt(RandomRange(0f, i + 0.999f));
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int k = 0; k < order.Count; k++)
            {
                var b = bodies[order[k]];
                Vector3 v = -towerPoint.Right * side * RandomRange(7f, 11f) - towerPoint.Forward * RandomRange(2f, 6f);
                queue.Add((b, Time.time + 0.35f + k * 0.38f, v));
            }
            ChaosEffects?.Sound(SoundBank.Knock, tower.position + Vector3.up * 6f, 0.8f, 0.7f);
            ChaosEffects?.Pop(tower.position + Vector3.up * 9f, "ПОЕХАЛИ!", new Color(1f, 0.8f, 0.3f), 1f);
            ChaosBody.HitCar += OnBodyHitCar;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (bin != null) bin.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Clamp01((Time.time - activeStart) / 0.5f) * 70f);
            Vector3 top = tower.position + Vector3.up * 7f;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var q = queue[i];
                if (Time.time < q.at) continue;
                queue.RemoveAt(i);
                q.body.NetId = BodyId(bodies.IndexOf(q.body));
                Vector3 spin = Vector3.Cross(Vector3.up, q.velocity.normalized) * 4f;
                q.body.Spawn(Context.Track, RoadHalf, top - towerPoint.Right * side * 0.4f, Quaternion.Euler(RandomRange(0f, 360f), RandomRange(0f, 360f), 0f), q.velocity, spin, !Authority);
                live.Add(q.body);
            }
            foreach (var b in live)
            {
                if (!b.Live || !b.Lost) continue;
                ChaosEffects?.Blast(b.transform.position, BlastStyle.ColorPuff, 0.8f);
                b.Despawn();
            }
        }

        void OnBodyHitCar(ChaosBody body, CarController car, float strength)
        {
            if (!Authority || !body.Explosive || strength < 6f || !live.Contains(body)) return;
            Signal(SignalExplode, bodies.IndexOf(body), body.transform.position);
        }

        /// Чужой взрыв рядом: бочка «БАХ» взрывается следом (решает хост).
        public void OnShock(Vector3 position, float radius, float strength, int generation)
        {
            if (!Authority || Stage != EventStage.Active) return;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (!b.Live || !b.Explosive) continue;
                float d = Vector3.Distance(b.transform.position, position);
                if (d <= 0.5f || d >= radius) continue;
                chainGeneration = generation;
                ChaosFx.NoteChain(generation);
                Signal(SignalExplode, i, b.transform.position);
                chainGeneration = 0;
            }
        }

        /// Звено цепной реакции текущего взрыва (у клиента сети — 0: важно только для проверки).
        int chainGeneration;

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if (code != SignalExplode || index < 0 || index >= bodies.Count) return;
            var b = bodies[index];
            if (!b.Live) return;
            Vector3 p = b.Live ? b.transform.position : value;
            b.Despawn();
            int generation = chainGeneration;
            ChaosEffects?.Blast(p, BlastStyle.Big, 1.8f);
            ChaosEffects?.Flash(p + Vector3.up * 2f, new Color(1f, 0.5f, 0.2f), 10f, 0.3f);
            ChaosEffects?.Sound(SoundBank.BigBoom, p, 1f, 0.8f);
            ChaosEffects?.Pop(p + Vector3.up * 3f, generation > 0 ? "ЦЕПНОЙ БАБАХ!" : "БАБАХ!", new Color(1f, 0.45f, 0.1f), 1.7f);
            ShakeCameraNear(p, 0.7f, 50f);
            float radius = ChaosPower.Current.Wild ? 7.5f : 4.8f;
            foreach (var r in Racers)
            {
                Vector3 d = r.Car.position - p;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > radius || !Simulates(r)) continue;
                Vector3 away = dist > 0.2f ? d / dist : Vector3.forward;
                float k = 1f - dist / radius * 0.5f;
                Vector3 spin = (Vector3.Cross(Vector3.up, away) * Random.Range(6f, 9f) + Vector3.up * Random.Range(-4f, 4f)) * k;
                r.Controller.ApplyLaunch(away * 14f * k + Vector3.up * 21f * k, spin, "взрыв бочки", generation);
            }
            // Волна задевает соседние бочки и мины (каждый компьютер считает её сам, сигналы — от хоста).
            ChaosEffects?.Shockwave(p, 11f, 15f, generation + 1);
        }

        protected override void OnEnd()
        {
            ChaosBody.HitCar -= OnBodyHitCar;
            foreach (var b in live)
                if (b.Live) ChaosEffects?.Blast(b.transform.position, BlastStyle.ColorPuff, 0.9f);
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var b in live)
                if (b.Live) b.transform.localScale = Vector3.one * Mathf.Max(0.05f, 1f - progress);
            PlaceTower(progress * progress);
        }

        protected override void Cleanup()
        {
            ChaosBody.HitCar -= OnBodyHitCar;
            ChaosFx.Unregister(this);
            marker.Hide();
            foreach (var b in live)
            {
                b.transform.localScale = Vector3.one;
                b.Despawn();
            }
            live.Clear();
            queue.Clear();
            tower.gameObject.SetActive(false);
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
                    Radius = b.Radius + (b.Explosive ? 2.5f : 0.6f),
                    HalfLength = b.Radius,
                    Good = false,
                    Obstacle = b.Kind == BodyKind.Dice,
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
    }
}
