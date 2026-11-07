using System.Collections.Generic;
using System.Text;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Race
{
    /// Телеметрия гонки для разработчика: время кругов всех машин, время вне дороги, удары, возвраты на трассу,
    /// смены позиций, эффекты событий. Вешается на объект с RaceManager на время проверки; на игру не влияет.
    public sealed class RaceTelemetry : MonoBehaviour
    {
        sealed class Stats
        {
            public readonly List<float> Laps = new List<float>();
            public float LapStart;
            public int LastLaps;
            public float OffRoad;
            public int Impacts;
            public int Recoveries;
            public float MaxSpeed;
            public int PositionGains;
            public int LastPosition;
            public int Effects;
            public bool WasOff;
            public readonly List<string> OffSpots = new List<string>();
        }

        /// Запас к полуширине дороги: колесо на кромке — ещё не «вне дороги».
        const float OffRoadMargin = 0.3f;

        RaceManager race;
        readonly Dictionary<Racer, Stats> stats = new Dictionary<Racer, Stats>();
        readonly Dictionary<CarController, System.Action<Vector3, float>> impactHandlers = new Dictionary<CarController, System.Action<Vector3, float>>();
        int lastAttempt = -1;

        void Awake() => race = GetComponent<RaceManager>();

        void OnDestroy()
        {
            foreach (var pair in impactHandlers) if (pair.Key != null) pair.Key.Impact -= pair.Value;
        }

        void ResetStats()
        {
            stats.Clear();
            foreach (var pair in impactHandlers) if (pair.Key != null) pair.Key.Impact -= pair.Value;
            impactHandlers.Clear();
            foreach (var r in race.Racers)
            {
                var s = new Stats { LastPosition = Position(r) };
                stats[r] = s;
                System.Action<Vector3, float> handler = (p, strength) => { if (strength > 3f) s.Impacts++; };
                r.Controller.Impact += handler;
                impactHandlers[r.Controller] = handler;
            }
        }

        int Position(Racer r)
        {
            int p = 1;
            foreach (var o in race.Racers) if (o.Score > r.Score) p++;
            return p;
        }

        void Update()
        {
            if (race == null) return;
            if (race.Attempt != lastAttempt)
            {
                lastAttempt = race.Attempt;
                ResetStats();
            }
            if (race.Phase != RacePhase.Running) return;
            float dt = Time.deltaTime;
            var course = race.Course;
            foreach (var r in race.Racers)
            {
                if (!stats.TryGetValue(r, out var s)) continue;
                if (r.Laps.Laps != s.LastLaps)
                {
                    s.Laps.Add((r.Finished ? r.FinishTime : race.RaceTime) - s.LapStart);
                    s.LapStart = race.RaceTime;
                    s.LastLaps = r.Laps.Laps;
                }
                if (r.Finished) continue;
                float lateral = Vector3.Dot(r.Car.position - course.PointAt(r.Progress), course.SideAt(r.Progress));
                bool off = Mathf.Abs(lateral) > course.HalfWidth + OffRoadMargin;
                if (off) s.OffRoad += dt;
                if (off && !s.WasOff && s.OffSpots.Count < 12) s.OffSpots.Add($"{r.Progress:F2}@{r.Controller.SpeedKmh:F0}/{lateral:+0;-0}{(Vector3.Dot(Vector3.Cross(course.DirectionAt(r.Progress), course.DirectionAt(r.Progress + 0.01f)), Vector3.up) > 0 ? "L" : "R")}");
                s.WasOff = off;
                s.MaxSpeed = Mathf.Max(s.MaxSpeed, r.Controller.SpeedKmh);
                int pos = Position(r);
                if (pos < s.LastPosition) s.PositionGains += s.LastPosition - pos;
                s.LastPosition = pos;
                s.Effects = r.Controller.EffectsReceived;
            }
        }

        /// Возврат на трассу (вызывает RaceManager).
        public void NoteRecovery(Racer r)
        {
            if (stats.TryGetValue(r, out var s)) s.Recoveries++;
        }

        public string Report()
        {
            var sb = new StringBuilder($"время гонки {race.RaceTime:F1} с\n");
            foreach (var r in race.Racers)
            {
                if (!stats.TryGetValue(r, out var s)) continue;
                sb.Append($"{r.Name,-7} место {Position(r)} круги [");
                for (int i = 0; i < s.Laps.Count; i++) sb.Append(i == 0 ? $"{s.Laps[i]:F2}" : $" {s.Laps[i]:F2}");
                sb.Append($"] вне дороги {s.OffRoad:F1} с, ударов {s.Impacts}, возвратов {s.Recoveries}, обгонов {s.PositionGains}, макс {s.MaxSpeed:F0} км/ч, эффектов {s.Effects}; вылеты {string.Join(" ", s.OffSpots)}\n");
            }
            return sb.ToString();
        }
    }
}
